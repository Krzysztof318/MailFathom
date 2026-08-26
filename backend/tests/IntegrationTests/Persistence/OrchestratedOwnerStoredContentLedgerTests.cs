// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the maintained per-owner stored-content figure says what a recomputation over the payloads says.</summary>
/// <remarks>
/// <para>
/// The figure exists so a per-owner storage ceiling can be consulted before every message without a sum over one
/// person's whole mailbox, and it is the only number in this schema that duplicates something derivable from the rows
/// beneath it. What makes that safe is exactly this claim, and nothing below a real database can settle it: every
/// movement is a composed statement joining a message to its account to reach the owner column, and the removals are
/// issued in front of a <c>RemoveRange</c> whose effect on the payload is PostgreSQL's own cascade. A figure that had
/// stopped tracking would reach an operator as a ceiling that refused mail there was room for, or admitted mail there
/// was not — neither of which fails anywhere.
/// </para>
/// <para>
/// The suite shares one database and one owner, and two other classes write a content row through the context rather
/// than through the port — deliberately, because what they are about is the schema and the cascade. So this class
/// re-derives once before it acts, which is a supported operation and leaves the counter holding what the payloads
/// hold; every movement afterwards is this class's own, and the agreement asserted at the end is therefore about the
/// port rather than about what the rest of the suite happened to leave behind.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedOwnerStoredContentLedgerTests(MailFathomOrchestrationFixture orchestration)
{
    private const string FolderAlias = "owner-stored-content";

    private const uint OverwrittenUid = 700;

    private const uint ErasedUid = 701;

    private const uint UntouchedUid = 702;

    /// <summary>Stored with its own metadata in one transaction, which is what an arriving message does.</summary>
    private const uint FirstArrivalUid = 703;

    private const int FirstPayloadByteCount = 4_096;

    private const int OverwritingPayloadByteCount = 9_216;

    private const int ErasedPayloadByteCount = 2_048;

    private const int UntouchedPayloadByteCount = 1_024;

    private const int FirstArrivalPayloadByteCount = 3_072;

    /// <summary>The moment the erasure below reports, which nothing in this class has already observed.</summary>
    private static readonly DateTimeOffset Observation = new(2026, 5, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadStoredContentBytesAsync_AfterStoresAnOverwriteAndAnErasure_AgreesWithARecomputation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, FolderAlias, cancellationToken);
        var owner = MailOwnerId.Create(await ReadSoleOwnerAsync(services, cancellationToken));

        var storedEmailIds = await StoreEmailsAsync(
            services,
            binding,
            [OverwrittenUid, ErasedUid, UntouchedUid],
            cancellationToken);

        // The baseline the movements below are measured from, taken as a recomputation so it holds what the payloads
        // hold rather than whatever an earlier class left in the counter.
        var baseline = await RederiveAsync(services, owner, cancellationToken);

        // Act
        await SaveContentAsync(services, binding, storedEmailIds, OverwrittenUid, FirstPayloadByteCount, cancellationToken);
        await SaveContentAsync(services, binding, storedEmailIds, ErasedUid, ErasedPayloadByteCount, cancellationToken);
        await SaveContentAsync(services, binding, storedEmailIds, UntouchedUid, UntouchedPayloadByteCount, cancellationToken);

        var afterStores = await ReadAsync(services, owner, cancellationToken);

        // A re-synchronization replaces the payload in place, so the figure moves by the difference rather than by the
        // whole of what arrived.
        await SaveContentAsync(
            services,
            binding,
            storedEmailIds,
            OverwrittenUid,
            OverwritingPayloadByteCount,
            cancellationToken);

        var afterOverwrite = await ReadAsync(services, owner, cancellationToken);

        // The row leaves and its payload leaves with it through the cascade, which nothing below the content store
        // observes: the figure gives those bytes back because the removal hands them back in the same transaction.
        await EraseAsync(services, storedEmailIds[ErasedUid], cancellationToken);

        var afterErasure = await ReadAsync(services, owner, cancellationToken);

        // What an account run actually does: the message's metadata and its payload are committed together, so the row
        // naming the account is still pending in the session when the figure moves.
        await StoreArrivingEmailAsync(services, binding, FirstArrivalUid, FirstArrivalPayloadByteCount, cancellationToken);

        var afterArrival = await ReadAsync(services, owner, cancellationToken);

        // Assert
        Assert.Equal(
            baseline + FirstPayloadByteCount + ErasedPayloadByteCount + UntouchedPayloadByteCount,
            afterStores);
        Assert.Equal(afterStores + (OverwritingPayloadByteCount - FirstPayloadByteCount), afterOverwrite);
        Assert.Equal(afterOverwrite - ErasedPayloadByteCount, afterErasure);
        Assert.Equal(afterErasure + FirstArrivalPayloadByteCount, afterArrival);

        // The claim the maintained figure exists on: recomputing from the payloads themselves reaches the same number,
        // and reading it back afterwards still does.
        Assert.Equal(afterArrival, await RederiveAsync(services, owner, cancellationToken));
        Assert.Equal(afterArrival, await ReadAsync(services, owner, cancellationToken));
    }

    private static Task<long> ReadAsync(
        OrchestratedMailFathomServices services,
        MailOwnerId owner,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOwnerStoredContentLedger>()
                .ReadStoredContentBytesAsync(owner, token),
            cancellationToken);

    private static Task<long> RederiveAsync(
        OrchestratedMailFathomServices services,
        MailOwnerId owner,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IOwnerStoredContentLedger>()
                .RederiveStoredContentBytesAsync(owner, token),
            cancellationToken);

    private static Task<Guid> ReadSoleOwnerAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .OwnerAccounts
                .AsNoTracking()
                .Select(owner => owner.Id)
                .SingleAsync(token),
            cancellationToken);

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

                    storedEmailIds[uid] = await repository.UpsertMetadataAsync(
                        session, SyntheticMailAccount.Owner,
                        SyntheticEmail.RemoteMetadataOf(occurrenceId, $"{FolderAlias}-{uid}"),
                        extractedMetadata: null,
                        StoredEmailContentAvailability.Available,
                        token);
                }
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);

        return storedEmailIds;
    }

    /// <summary>Stores one message the way an account run does: its metadata and its payload in one transaction.</summary>
    private static async Task StoreArrivingEmailAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        uint uid,
        int byteCount,
        CancellationToken cancellationToken)
    {
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);

        var commitResult = await services.CommitAsync(
            async (scope, session, token) =>
            {
                var storedEmailId = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session, SyntheticMailAccount.Owner,
                    SyntheticEmail.RemoteMetadataOf(occurrenceId, $"{FolderAlias}-{uid}"),
                    extractedMetadata: null,
                    StoredEmailContentAvailability.Available,
                    token);

                await scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                    session,
                    storedEmailId,
                    occurrenceId,
                    PlacedEmailContent.InDatabase(SyntheticEmail.RawMimeOf($"{FolderAlias}-{uid}", byteCount)),
                    token);
            },
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    private static async Task SaveContentAsync(
        OrchestratedMailFathomServices services,
        MailFolderResolution binding,
        IReadOnlyDictionary<uint, StoredEmailId> storedEmailIds,
        uint uid,
        int byteCount,
        CancellationToken cancellationToken)
    {
        var occurrenceId = SyntheticEmail.OccurrenceIn(binding, uid);

        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IEmailContentStore>().SaveContentAsync(
                session,
                storedEmailIds[uid],
                occurrenceId,
                PlacedEmailContent.InDatabase(SyntheticEmail.RawMimeOf($"{FolderAlias}-{uid}", byteCount)),
                token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }

    private static async Task EraseAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        var commitResult = await services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IStoredEmailReconciliationStore>()
                .ApplyReconciliationOutcomeAsync(
                    session,
                    new ReconciledFolderOutcome(
                        StillPresent: [],
                        ConfirmedUnchanged: [],
                        Disappeared: [storedEmailId],
                        RemovedByOwnMutation: [],
                        RemotelyDeletedEmailDisposition.EraseLocalCopy,
                        Observation),
                    token),
            cancellationToken);

        Assert.Equal(PersistenceCommitResult.Committed, commitResult);
    }
}
