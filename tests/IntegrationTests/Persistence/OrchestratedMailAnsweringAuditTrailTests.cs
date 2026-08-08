// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Answering.Audit;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MailFathom.IntegrationTests.Persistence;

/// <summary>Proves the answering record against real PostgreSQL, where its two lifetime rules actually live.</summary>
/// <remarks>
/// <para>
/// Two tests, because there are two claims no substitute can establish. The first is that an entry written for an
/// account that keeps a record reads back through the paginated port with the mail it named, that erasing one of those
/// messages reaches it through the schema's own cascade rather than through a rule somebody remembers, and that
/// retention then erases what has outlived the window. The second is that an account keeping no record writes nothing —
/// the default the privacy posture rests on, which the same database has to show as an absence beside the presences
/// above.
/// </para>
/// <para>
/// Everything else about the record — which accounts of a scope owe an entry, what one states, and what a lost append
/// costs — is a rule the unit suite exercises against substitutes and buys nothing here.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailAnsweringAuditTrailTests(MailFathomOrchestrationFixture orchestration)
{
    private const string EndpointAlias = "integration-answering";
    private const string InstructionsVersion = "0a1b2c3d4e5f";

    /// <summary>The clock this class stamps a run with, which is the one the retention pass in the container reads.</summary>
    private static readonly TimeProvider Clock = TimeProvider.System;

    /// <summary>The alias this class owns, so nothing else's occurrences land in the folder it stores mail under.</summary>
    private static readonly MailFolderResolution Inbox = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("answering-audit-inbox"),
        RemoteFolderPath.Create("AnsweringAuditInbox", hierarchyDelimiter: '.'));

    /// <summary>
    /// One entry per run reads back with the mail it named and which of it the answer cited; erasing one of those
    /// messages reaches the entry through the cascade; and retention erases an entry that has outlived the window.
    /// </summary>
    [Fact]
    public async Task AnsweringRecord_ARunOverAnAccountThatKeepsOne_FollowsTheMailAndTheWindow()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var services = await OrchestratedMailFathomServices.StartAsync(
            orchestration,
            cancellationToken,
            answeringAuditTrailEnabled: true);

        await CommitInboxBindingAsync(services, cancellationToken);

        var read = await StoreMetadataAsync(services, uid: 9_101, cancellationToken);
        var cited = await StoreMetadataAsync(services, uid: 9_102, cancellationToken);
        var recentRun = ObservationOver(Clock.GetUtcNow(), [read, cited], [cited]);
        var expiredRun = ObservationOver(
            Clock.GetUtcNow() - SyntheticMailAccount.AnsweringAuditRetention - TimeSpan.FromDays(1),
            [read],
            []);

        // Act
        await RecordAsync(services, recentRun, cancellationToken);
        await RecordAsync(services, expiredRun, cancellationToken);

        var written = await ReadRecordAsync(services, cancellationToken);

        await EraseStoredEmailAsync(services, read, cancellationToken);

        var afterErasure = await ReadRecordAsync(services, cancellationToken);
        var erasedCount = await EraseExpiredAsync(services, cancellationToken);
        var afterRetention = await ReadRecordAsync(services, cancellationToken);

        // Assert
        var recentEntry = written.Single(entry => entry.RunId == recentRun.RunId);

        Assert.Equal(
            [(read, 0, false), (cited, 1, true)],
            recentEntry.Emails.Select(email => (email.StoredEmailId, email.Position, email.WasCited)));
        Assert.Equal(
            (EndpointAlias, InstructionsVersion, MailAnsweringRunOutcome.Answered),
            (recentEntry.ChatEndpointAlias, recentEntry.InstructionsVersion, recentEntry.Outcome));

        // The erased message is gone from the run that read it, while the run itself stays: the entry records that a
        // question was answered, and only the mail it named follows that mail's own deletion path.
        var afterErasureEntry = afterErasure.Single(entry => entry.RunId == recentRun.RunId);

        Assert.Equal([(cited, 1, true)], afterErasureEntry.Emails.Select(email =>
            (email.StoredEmailId, email.Position, email.WasCited)));

        // The position survives the gap, which is what says something was read and is gone rather than that the run was
        // shorter than it was.
        Assert.Equal(1, Assert.Single(afterErasureEntry.Emails).Position);

        Assert.Equal(1, erasedCount);
        Assert.DoesNotContain(afterRetention, entry => entry.RunId == expiredRun.RunId);
        Assert.Contains(afterRetention, entry => entry.RunId == recentRun.RunId);
    }

    /// <summary>An account that never asked for a record accumulates none, which is what off by default has to mean.</summary>
    [Fact]
    public async Task AnsweringRecord_AnAccountThatKeepsNone_KeepsNothing()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await CommitInboxBindingAsync(services, cancellationToken);

        var read = await StoreMetadataAsync(services, uid: 9_201, cancellationToken);
        var run = ObservationOver(Clock.GetUtcNow(), [read], []);

        // Act
        await RecordAsync(services, run, cancellationToken);

        // Assert
        Assert.DoesNotContain(await ReadRecordAsync(services, cancellationToken), entry => entry.RunId == run.RunId);
    }

    /// <summary>Builds the record of one finished run over this account, as the use case would hand it over.</summary>
    private static MailAnsweringRunObservation ObservationOver(
        DateTimeOffset completedAt,
        IReadOnlyList<StoredEmailId> retrieved,
        IReadOnlyList<StoredEmailId> cited)
    {
        var observation = new MailAnsweringRunObservation(
            MailAnsweringRunId.Create(Guid.CreateVersion7()),
            MailboxScope.Create([SyntheticMailAccount.AccountId], []),
            completedAt - TimeSpan.FromSeconds(9));

        observation.RecordComposition(EndpointAlias, InstructionsVersion);
        observation.RecordRetrieval(new MailAnsweringRetrievalReport(
            [.. retrieved.Select(PassageOf)],
            retrieved.Count,
            retrieved.Count,
            MailAnsweringRunDegradation.None));
        observation.RecordOutcome(MailAnsweringRunOutcome.Answered, cited, completedAt);

        return observation;
    }

    private static EmailKnowledgePassage PassageOf(StoredEmailId storedEmailId) => new()
    {
        StoredEmailId = storedEmailId,
        AccountId = SyntheticMailAccount.AccountId,
        FolderAlias = Inbox.Alias,
        Subject = "Quarterly invoice",
        ReceivedAt = Clock.GetUtcNow(),
        Text = "the invoice is attached",
    };

    private static Task<bool> RecordAsync(
        OrchestratedMailFathomServices services,
        MailAnsweringRunObservation observation,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                await scope.GetRequiredService<IMailAnsweringAuditTrail>().RecordAsync(observation, token);

                return true;
            },
            cancellationToken);

    /// <summary>Reads the whole of this account's record through the port an operator's page is served from.</summary>
    private static Task<IReadOnlyList<MailAnsweringAuditEntry>> ReadRecordAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                var queryResult = MailAnsweringAuditQuery.Create(
                    SyntheticMailAccount.AccountId,
                    completedFrom: null,
                    completedBefore: null,
                    MailAnsweringAuditQuery.MaximumPageSize,
                    cursor: null);

                var page = await scope.GetRequiredService<IMailAnsweringAuditEntryStore>()
                    .ReadPageAsync(queryResult.Query!, token);

                return page.Entries;
            },
            cancellationToken);

    private static Task<int> EraseExpiredAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailAnsweringAuditTrailRetention>()
                .EraseExpiredAsync(SyntheticMailAccount.AccountId, token),
            cancellationToken);

    /// <summary>Erases one stored email, which is the cascade an entry naming it has to ride.</summary>
    /// <remarks>
    /// Deleted through the context rather than through a disposition, because what is under test is the schema: a
    /// set-based delete is the bluntest form of the question, and nothing about a disposition would make the foreign
    /// key's answer any more real.
    /// </remarks>
    private static Task<int> EraseStoredEmailAsync(
        OrchestratedMailFathomServices services,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        var erasedId = storedEmailId.Value;

        return services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailFathomDbContext>()
                .StoredEmails
                .Where(email => email.Id == erasedId)
                .ExecuteDeleteAsync(token),
            cancellationToken);
    }

    private static async Task CommitInboxBindingAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IMailFolderResolutionStore>().SaveResolutionAsync(
                    session,
                    SyntheticMailAccount.AccountId,
                    Inbox,
                    token),
                cancellationToken));

    /// <summary>Stores one email's metadata, which is what an entry's foreign key needs to point at.</summary>
    /// <remarks>
    /// No mail server is involved: the occurrence is fabricated against this class's own folder binding, because what
    /// is under test is the schema rather than anything IMAP decides. The UIDs are this class's own so nothing else in
    /// the suite collides with them.
    /// </remarks>
    private static Task<StoredEmailId> StoreMetadataAsync(
        OrchestratedMailFathomServices services,
        uint uid,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                await using var session = await scope.GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                var occurrence = EmailOccurrenceId.Create(
                    SyntheticMailAccount.AccountId,
                    Inbox.Id,
                    ImapUidValidity.Create(1),
                    ImapUid.Create(uid));

                var storedEmailId = await scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                    session,
                    SyntheticEmail.RemoteMetadataOf(occurrence, $"answering-audit-{uid}"),
                    extractedMetadata: null,
                    StoredEmailContentAvailability.ExceededSizeLimit,
                    token);

                Assert.Equal(PersistenceCommitResult.Committed, await session.CommitAsync(token));

                return storedEmailId;
            },
            cancellationToken);
}
