// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the backward pass reads its window from PostgreSQL and writes both endings of a remote deletion.</summary>
/// <remarks>
/// <para>
/// Three claims no unit test reaches. The window is two bounded queries whose sizes the budget decides and whose order
/// the server produces, so what a folder actually offers is a property of the translated SQL. The tombstone is a column
/// three read models filter on and the next window filters on again, and nothing else in the suite ever sets it — every
/// one of those clauses could be deleted and the rest of the suite would stay green. And erasure is a single
/// <c>RemoveRange</c> that leans on <c>ON DELETE CASCADE</c> to take the raw MIME, the search document, and the repair
/// request with it, which is a guarantee the database gives rather than one the code makes.
/// </para>
/// <para>
/// The outcomes are stated rather than produced by a synchronization run against the mail server. What the server said
/// and how <c>MailboxReconciler</c> turns it into a window is decided without a database and is covered in the unit
/// suite; what is under test here is the effect of applying that window to real rows. Each test owns its own folder
/// alias, because a tombstone one of them writes is exactly what would change what another one finds.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedStoredEmailReconciliationTests(MailFathomOrchestrationFixture orchestration)
{
    /// <summary>The word every seeded body carries, so one search reaches every row a folder holds.</summary>
    private const string ReconciledTerm = "reconciled";

    /// <summary>The moment an earlier pass recorded, which every window below is ordered against.</summary>
    private static readonly DateTimeOffset EarlierObservation = new(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The moment the window under test reports, later than what the rows already carry.</summary>
    private static readonly DateTimeOffset LaterObservation = EarlierObservation.AddHours(1);

    /// <summary>The window reserves half of itself for mail somebody has already asked the server about.</summary>
    /// <remarks>
    /// Both halves are ordered by the server and both are capped by it, so the sequence this returns is the whole of
    /// what the reservation means: never-observed mail enters in UID order, previously observed mail enters oldest
    /// observation first, and neither group can crowd the other out of a window it has to share.
    /// </remarks>
    [Fact]
    public async Task GetReconciliationWindowAsync_AFolderMixingObservedAndNeverObservedMail_ReservesHalfOfItForTheObservedOldestFirst()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-window", cancellationToken);

        var neverObservedUids = new uint[] { 205, 201, 203 };
        var observedUidsOldestFirst = new uint[] { 202, 204, 200 };

        await StoreEmailsAsync(services, binding, [.. neverObservedUids, .. observedUidsOldestFirst], cancellationToken);
        await ObserveAsync(services, binding, observedUidsOldestFirst, cancellationToken);

        // Act
        var wholeWindow = await ReadWindowAsync(services, binding, maxEmailCount: 4, cancellationToken);
        var halvedWindow = await ReadWindowAsync(services, binding, maxEmailCount: 2, cancellationToken);

        // Assert
        // Two of four, because three previously observed rows can fill the half reserved for them. The never-observed
        // pair is the lowest two UIDs rather than the two stored first, and the observed pair is the two oldest
        // observations rather than the two lowest UIDs — 200 carries the newest and is left behind by both orders.
        Assert.Equal([201u, 203u, 202u, 204u], wholeWindow);

        // The reserve is a share of the window rather than a fixed count, so halving the window halves both groups.
        Assert.Equal([201u, 202u], halvedWindow);
    }

    /// <summary>Applies a window that reports flags, confirms mail, and loses one message, then reads back all three.</summary>
    /// <remarks>
    /// One test rather than four, because the four claims are one window's outcome and separating them would pay for
    /// four seeded folders to assert what one application produces. The tombstone is the reason the reads are here at
    /// all: <c>StoredEmailSelectionPredicate</c>, the summary lookup, and the window query each exclude an expunged row
    /// through a clause of their own, and this is the only row in the suite that has ever been one.
    /// </remarks>
    [Fact]
    public async Task ApplyReconciliationOutcomeAsync_AWindowReportingFlagsAndOneDisappearance_WritesEachOutcomeAndHidesTheTombstone()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-tombstone", cancellationToken);

        const uint FlaggedUid = 300;
        const uint ConfirmedUid = 301;
        const uint DisappearedUid = 302;

        var storedEmailIds = await StoreEmailsAsync(
            services,
            binding,
            [FlaggedUid, ConfirmedUid, DisappearedUid],
            cancellationToken);

        // An earlier pass, so the flags the second window must leave alone were written by the production path rather
        // than staged into the row by the arrangement.
        await ApplyOutcomeAsync(
            services,
            new ReconciledFolderOutcome(
                [
                    new ObservedEmailFlags(storedEmailIds[FlaggedUid], SeenAt(EarlierObservation)),
                    new ObservedEmailFlags(storedEmailIds[ConfirmedUid], FlaggedAt(EarlierObservation)),
                ],
                ConfirmedUnchanged: [],
                Disappeared: [],
                RemovedByOwnMutation: [],
                RemotelyDeletedEmailDisposition.RetainTombstone,
                EarlierObservation),
            cancellationToken);

        // Act
        await ApplyOutcomeAsync(
            services,
            new ReconciledFolderOutcome(
                StillPresent: [],
                ConfirmedUnchanged: [storedEmailIds[ConfirmedUid]],
                Disappeared: [storedEmailIds[DisappearedUid]],
                RemovedByOwnMutation: [],
                RemotelyDeletedEmailDisposition.RetainTombstone,
                LaterObservation),
            cancellationToken);

        // Assert
        var rows = await ReadRowsAsync(services, binding, cancellationToken);

        var flagged = rows[FlaggedUid];
        Assert.True(flagged.IsRemotelySeen);
        Assert.Equal(EarlierObservation, flagged.RemoteFlagsObservedAt);
        Assert.Null(flagged.RemoteExpungeObservedAt);

        // The server said nothing changed, so the flags stay as they were read and only the queue position moves.
        var confirmed = rows[ConfirmedUid];
        Assert.True(confirmed.IsRemotelyFlagged);
        Assert.False(confirmed.IsRemotelySeen);
        Assert.Equal(LaterObservation, confirmed.RemoteFlagsObservedAt);
        Assert.Null(confirmed.RemoteExpungeObservedAt);

        // The observation timestamp is written alongside the tombstone deliberately: it is what takes a row the server
        // will never answer about again out of every later window.
        var tombstoned = rows[DisappearedUid];
        Assert.Equal(LaterObservation, tombstoned.RemoteExpungeObservedAt);
        Assert.Equal(LaterObservation, tombstoned.RemoteFlagsObservedAt);

        var timeline = await ReadTimelineAsync(services, binding, cancellationToken);
        Assert.Equal(new[] { FlaggedUid, ConfirmedUid }, StoredUidsOf(timeline, rows));

        var matches = await SearchAsync(services, binding, cancellationToken);
        Assert.Equal(2, matches.Count);
        Assert.DoesNotContain(tombstoned.Id, matches.Select(match => match.Summary.StoredEmailId.Value));

        var tombstonedSummary = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailSummaryReader>()
                .FindAsync(storedEmailIds[DisappearedUid], token),
            cancellationToken);
        Assert.Null(tombstonedSummary);

        Assert.DoesNotContain(
            DisappearedUid,
            await ReadWindowAsync(services, binding, maxEmailCount: 10, cancellationToken));
    }

    /// <summary>Erases what the account asked to erase, and refuses to erase what a fresher pass has since seen.</summary>
    /// <remarks>
    /// The cascade is the point of running this against PostgreSQL. Nothing loads the content row, the search document,
    /// or the repair request before the removal, so if the foreign keys did not carry the delete they would be left
    /// referencing a row that no longer exists — which a substitute could not tell anyone. The spared row is asserted in
    /// the same test because it is the same code path reaching the opposite conclusion, and it is the failure that
    /// matters most: a window replayed after a commit conflict must not delete mail a later pass has proved is there.
    /// </remarks>
    [Fact]
    public async Task ApplyReconciliationOutcomeAsync_UnderEraseLocalCopy_RemovesEveryCopyCascadedFromTheRowAndSparesAFresherObservation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "reconciliation-erasure", cancellationToken);

        const uint ErasedUid = 400;
        const uint SparedUid = 401;

        var storedEmailIds = await StoreEmailsAsync(services, binding, [ErasedUid, SparedUid], cancellationToken);
        var erasedId = storedEmailIds[ErasedUid];

        var contentCommit = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                session,
                erasedId,
                new RemoteEmailContent(
                    SyntheticEmail.OccurrenceIn(binding, ErasedUid),
                    SyntheticEmail.RawMimeOf("reconciliation-erasure", totalByteCount: 4096)),
                token),
            cancellationToken);
        Assert.Equal(PersistenceCommitResult.Committed, contentCommit);

        await services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IEmailContentRepairRequestStore>().RecordAsync(
                    new EmailContentRepairRequest(erasedId, EmailContentDefect.HashMismatch),
                    token);

                return true;
            },
            cancellationToken);

        await ObserveAsync(services, binding, [SparedUid], cancellationToken, observedAt: LaterObservation);

        // Act
        await ApplyOutcomeAsync(
            services,
            new ReconciledFolderOutcome(
                StillPresent: [],
                ConfirmedUnchanged: [],
                Disappeared: [erasedId, storedEmailIds[SparedUid]],
                RemovedByOwnMutation: [],
                RemotelyDeletedEmailDisposition.EraseLocalCopy,
                EarlierObservation),
            cancellationToken);

        // Assert
        var derivedCopyCounts = await ReadDerivedCopyCountsAsync(services, erasedId, cancellationToken);
        Assert.Equal(new DerivedCopyCounts(0, 0, 0, 0), derivedCopyCounts);

        var rows = await ReadRowsAsync(services, binding, cancellationToken);
        Assert.DoesNotContain(ErasedUid, rows.Keys);

        var spared = rows[SparedUid];
        Assert.Equal(LaterObservation, spared.RemoteFlagsObservedAt);
        Assert.Null(spared.RemoteExpungeObservedAt);
    }

    private static RemoteEmailFlagSnapshot SeenAt(DateTimeOffset observedAt) => new(
        observedAt,
        IsSeen: true,
        IsAnswered: false,
        IsFlagged: false,
        IsDraft: false,
        IsDeleted: false,
        Keywords: RemoteEmailKeywords.None);

    private static RemoteEmailFlagSnapshot FlaggedAt(DateTimeOffset observedAt) => new(
        observedAt,
        IsSeen: false,
        IsAnswered: false,
        IsFlagged: true,
        IsDraft: false,
        IsDeleted: false,
        Keywords: RemoteEmailKeywords.None);

    /// <summary>Writes one email per UID through the production upsert, indexed so a test can name a row by its UID.</summary>
    private static async Task<IReadOnlyDictionary<uint, StoredEmailId>> StoreEmailsAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        IReadOnlyList<uint> uids,
        CancellationToken cancellationToken)
    {
        var storedEmailIds = new Dictionary<uint, StoredEmailId>();

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var repository = scope.GetRequiredService<IEmailMetadataRepository>();

                foreach (var uid in uids)
                {
                    var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);
                    var subject = $"reconciled-{uid}";

                    storedEmailIds[uid] = await repository.UpsertMetadataAsync(
                        session,
                        SyntheticEmail.RemoteMetadataOf(occurrenceId, subject),
                        SyntheticEmail.ExtractionOf(
                            occurrenceId,
                            subject,
                            SyntheticEmail.BodyTextContaining(ReconciledTerm, wordCount: 10),
                            "recipient@mailfathom.test"),
                        StoredEmailContentAvailability.Available,
                        token);
                }
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailIds;
    }

    /// <summary>Records an observation against each named UID, one window per UID so their timestamps are ordered.</summary>
    /// <remarks>
    /// Separate windows rather than one, because the queue order this arranges is the order of the observations
    /// themselves: a single window would write one timestamp to every row it named and leave the ordering the
    /// reservation is read through undefined.
    /// </remarks>
    private static async Task ObserveAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        IReadOnlyList<uint> uidsOldestFirst,
        CancellationToken cancellationToken,
        DateTimeOffset? observedAt = null)
    {
        var rows = await ReadRowsAsync(services, binding, cancellationToken);
        var firstObservation = observedAt ?? EarlierObservation;

        foreach (var (index, uid) in uidsOldestFirst.Index())
        {
            var recordedAt = firstObservation.AddMinutes(index);

            await ApplyOutcomeAsync(
                services,
                new ReconciledFolderOutcome(
                    [new ObservedEmailFlags(StoredEmailId.Create(rows[uid].Id), SeenAt(recordedAt))],
                    ConfirmedUnchanged: [],
                    Disappeared: [],
                    RemovedByOwnMutation: [],
                    RemotelyDeletedEmailDisposition.RetainTombstone,
                    recordedAt),
                cancellationToken);
        }
    }

    private static async Task ApplyOutcomeAsync(
        OrchestratedMailFathomServices services,
        ReconciledFolderOutcome outcome,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IStoredEmailReconciliationStore>()
                .ApplyReconciliationOutcomeAsync(session, outcome, token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    private static async Task<IReadOnlyList<uint>> ReadWindowAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        int maxEmailCount,
        CancellationToken cancellationToken)
    {
        var window = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailReconciliationStore>()
                .GetReconciliationWindowAsync(
                    SyntheticMailAccount.AccountId,
                    binding.Id,
                    ImapUidValidity.Create(SyntheticEmail.UidValidity),
                    maxEmailCount,
                    token),
            cancellationToken);

        return [.. window.Select(awaiting => awaiting.Uid.Value)];
    }

    private static Task<IReadOnlyList<EmailSummary>> ReadTimelineAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IStoredEmailTimelineReader>().ReadPageAsync(
                EmailTimelineFilter.Create(
                    ScopeOf(scope, binding),
                    senderAddress: null,
                    recipientAddress: null,
                    subjectFragment: null,
                    receivedOnOrAfter: null,
                    receivedBefore: null,
                    isRemotelySeen: null,
                    isRemotelyFlagged: null,
                    keyword: null,
                    hasAttachments: null,
                    EmailTimelineDirection.NewestFirst),
                continueAfter: null,
                limit: 50,
                token),
            cancellationToken);

    private static Task<IReadOnlyList<EmailSearchMatch>> SearchAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var reader = scope.GetRequiredService<IEmailSearchIndexReader>();
                var selection = MailboxEmailSelection.Create(
                    ScopeOf(scope, binding),
                    senderAddress: null,
                    recipientAddress: null,
                    subjectFragment: null,
                    receivedOnOrAfter: null,
                    receivedBefore: null,
                    isRemotelySeen: null,
                    isRemotelyFlagged: null,
                    keyword: null,
                    hasAttachments: null);
                var queryText = EmailSearchQueryText.Create(ReconciledTerm);

                var candidates = await reader.ReadRankedCandidatesAsync(selection, queryText, limit: 50, token);

                return await reader.ReadMatchesAsync(
                    selection,
                    queryText,
                    EmailSearchSnippetBounds.Default,
                    candidates,
                    token);
            },
            cancellationToken);

    private static MailboxScope ScopeOf(IServiceProvider scope, MailFolderResolution binding) =>
        OrchestratedMailboxScope.Readable(scope, [binding.Alias.Value]);

    /// <summary>Reads every row one folder holds, keyed by UID so an assertion names a message rather than an identifier.</summary>
    private static async Task<IReadOnlyDictionary<uint, StoredEmailEntity>> ReadRowsAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        CancellationToken cancellationToken)
    {
        var alias = binding.Alias.Value;
        var generation = binding.Generation.Value;

        var rows = await services.InScopeAsync(
            (scope, token) => scope
                .GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .AsNoTracking()
                .Where(email => email.MailFolder.Alias == alias && email.MailFolder.ResolutionGeneration == generation)
                .ToArrayAsync(token),
            cancellationToken);

        return rows.ToDictionary(row => row.Uid);
    }

    /// <summary>Counts what the database should have removed along with one stored email.</summary>
    private static Task<DerivedCopyCounts> ReadDerivedCopyCountsAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var dbContext = scope.GetRequiredService<MailFathomDbContext>();
                var id = storedEmailId.Value;

                return new DerivedCopyCounts(
                    await dbContext.StoredEmails.AsNoTracking().CountAsync(email => email.Id == id, token),
                    await dbContext.EmailMessageContents.AsNoTracking()
                        .CountAsync(content => content.StoredEmailId == id, token),
                    await dbContext.EmailSearchDocuments.AsNoTracking()
                        .CountAsync(document => document.StoredEmailId == id, token),
                    await dbContext.EmailContentRepairRequests.AsNoTracking()
                        .CountAsync(request => request.StoredEmailId == id, token));
            },
            cancellationToken);

    private static IEnumerable<uint> StoredUidsOf(
        IReadOnlyList<EmailSummary> summaries,
        IReadOnlyDictionary<uint, StoredEmailEntity> rows)
    {
        var uidsById = rows.ToDictionary(row => row.Value.Id, row => row.Key);

        return summaries.Select(summary => uidsById[summary.StoredEmailId.Value]).Order();
    }

    /// <summary>The four rows one erased email is reachable from, so a missing cascade names which one survived.</summary>
    private sealed record DerivedCopyCounts(
        int StoredEmails,
        int MessageContents,
        int SearchDocuments,
        int RepairRequests);
}
