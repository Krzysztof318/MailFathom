// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Persistence;
using MailFathom.Application.Resilience;
using MailFathom.Application.Synchronization;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Mail.MailKit.Writes;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Resilience;
using MailFathom.IntegrationTests.Mailbox;
using MailFathom.IntegrationTests.Orchestration;
using MailFathom.IntegrationTests.Persistence;
using MailKit.Net.Imap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MailFathom.IntegrationTests.Synchronization;

/// <summary>Proves the durable mutation record against real PostgreSQL and a real mail server together.</summary>
/// <remarks>
/// <para>
/// Two tests, because there are two claims neither substitute can establish. The first is that a process stopped
/// between the copy and the expunge completes to exactly one message rather than two: it needs a server that really
/// copied a message and a database that really kept the stage across the stop. The second is that the idempotency
/// identity is enforced by the database — two writers who cannot see each other's uncommitted row both insert, and only
/// the unique index can refuse one of them.
/// </para>
/// <para>
/// Everything else about the record — which stage each mutation passes through, what a resumed attempt skips, when a
/// mutation is abandoned — is a rule the unit suite already exercises against substitutes and buys nothing here.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailboxMutationRecordTests(MailFathomOrchestrationFixture orchestration)
{
    private const string ArchiveFolderName = "MutationRecordArchive";

    /// <summary>The alias this class owns, bound to the real inbox so a write session selects it.</summary>
    private static readonly MailFolderResolution Inbox = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("mutation-record-inbox"),
        RemoteFolderPath.Create(OrchestratedMailbox.InboxPath, hierarchyDelimiter: '.'));

    private static readonly RemoteFolderPath ArchivePath =
        RemoteFolderPath.Create(ArchiveFolderName, hierarchyDelimiter: '.');

    private static readonly MailboxMutationRequester Requester =
        MailboxMutationRequester.Rule("file-to-archive", "1");

    /// <summary>
    /// A relocation whose process stopped after the copy landed and before the source was removed. The message is in
    /// both folders at that point, which is exactly the state nothing can tell apart from a move somebody made by hand;
    /// the record is the only thing that knows, and the next run has to finish rather than copy again.
    /// </summary>
    [Fact]
    public async Task PerformAsync_ResumingARelocationStoppedBetweenTheCopyAndTheExpunge_LeavesExactlyOneMessage()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        await mailbox.RecreateFolderAsync(ArchiveFolderName, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        Assert.Equal(
            PersistenceCommitResult.Committed,
            await CommitInboxBindingAsync(services, cancellationToken));

        var subject = $"resume-relocate-{Guid.NewGuid():N}";
        var occurrence = await DeliverAndLocateAsync(mailbox, subject, cancellationToken);
        var storedEmailId = await StoreMetadataAsync(services, occurrence, subject, cancellationToken);
        var request = MailboxMutationRequest.Relocate(storedEmailId, occurrence, Requester, ArchivePath);

        await StopAfterTheCopyAsync(services, request, occurrence, cancellationToken);

        // The arrangement is asserted rather than assumed. A record that reached PlacementConfirmed owing no source
        // removal describes a relocation the placement already finished, and resuming from it would prove nothing
        // about the window this test exists for — so the row is read back before the act.
        var stoppedRow = await ReadRecordRowAsync(services, occurrence, cancellationToken);
        Assert.Equal(MailboxMutationStage.PlacementConfirmed, stoppedRow.Stage);
        Assert.True(stoppedRow.RequiresSourceRemoval);

        // The state a crash leaves: the copy landed and the source is still there, so the message is in both folders.
        Assert.Contains(
            await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken),
            email => email.Subject == subject);
        Assert.Single(await mailbox.ReadAsync(ArchiveFolderName, cancellationToken), email => email.Subject == subject);

        // Act
        var outcome = await services.InScopeAsync(
            (scope, token) => ResumeWithoutMoveExtensionAsync(scope, request, token),
            cancellationToken);

        // Assert
        var inbox = await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken);
        var archived = await mailbox.ReadAsync(ArchiveFolderName, cancellationToken);

        Assert.Equal(MailboxMutationStatus.Performed, outcome.Status);
        Assert.DoesNotContain(inbox, email => email.Subject == subject);
        Assert.Single(archived, email => email.Subject == subject);

        var row = await ReadRecordRowAsync(services, occurrence, cancellationToken);
        Assert.Equal(MailboxMutationStage.Completed, row.Stage);

        // The COPYUID the first run's copy was answered with survived the stop, so the identity the record names is the
        // one the server gave rather than one searched for afterwards.
        Assert.Equal(
            (await mailbox.ReadUidValidityAsync(ArchiveFolderName, cancellationToken)).Value,
            row.PlacementUidValidity);
        Assert.NotNull(row.PlacementUid);
    }

    /// <summary>
    /// Two writers asking for the same change at the same moment, each in a scope of its own the way two workers are.
    /// Neither can see the other's uncommitted insert, so no check either of them could make would help; the unique
    /// index is what refuses the second, and the losing commit reports the conflict its caller loops on.
    /// </summary>
    [Fact]
    public async Task OpenAsync_ForTheSameIdentityInTwoConcurrentSessions_IsRefusedByTheDatabaseAndLeavesOneRecord()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        var binding = await OrchestratedFolderBinding.CommitAsync(services, "mutation-identity", cancellationToken);
        var occurrence = SyntheticEmail.OccurrenceIn(binding, uid: 4242U);
        var storedEmailId = await StoreMetadataAsync(services, occurrence, "mutation-identity", cancellationToken);
        var request = MailboxMutationRequest.SetSeen(storedEmailId, occurrence, Requester, isSeen: true);

        // Act
        var commits = await services.InTwoScopesAsync(
            async (firstScope, secondScope, token) =>
            {
                await using var first = await firstScope.GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);
                await using var second = await secondScope.GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                // Both open before either commits, which is the whole point: neither transaction can see the other's
                // pending row, so both reach the insert.
                await firstScope.GetRequiredService<IMailboxMutationRecordStore>().OpenAsync(first, request, token);
                await secondScope.GetRequiredService<IMailboxMutationRecordStore>().OpenAsync(second, request, token);

                return (First: await first.CommitAsync(token), Second: await second.CommitAsync(token));
            },
            cancellationToken);

        // Assert
        Assert.Equal(PersistenceCommitResult.Committed, commits.First);
        Assert.Equal(PersistenceCommitResult.ConcurrencyConflict, commits.Second);

        var outstanding = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<IMailboxMutationRecordStore>()
                .ReadOutstandingAsync(SyntheticMailAccount.AccountId, limit: 100, token),
            cancellationToken);
        Assert.Single(outstanding, candidate => candidate.Record.Request.Occurrence == occurrence);
    }

    /// <summary>Issues the copy half of a relocation and stops, leaving the record exactly where a crash would.</summary>
    /// <remarks>
    /// The record is driven through the production store, and the copy is a real <c>UID COPY</c> against the real
    /// server, so what the resumed attempt below finds is the state a stopped process actually leaves rather than a
    /// description of it. <c>MOVE</c> is masked because it is the fallback sequence that has the window this is about.
    /// </remarks>
    private static async Task StopAfterTheCopyAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRequest request,
        EmailOccurrenceId occurrence,
        CancellationToken cancellationToken)
    {
        var recordId = await CommitForAsync(
            services,
            async (scope, session, token) => (await scope.GetRequiredService<IMailboxMutationRecordStore>()
                .OpenAsync(session, request, token)).Id,
            cancellationToken);

        var placement = await services.InScopeAsync(
            async (scope, token) =>
            {
                await using var pool = CreateMoveMaskedPool(scope);
                var factory = new MailKitImapWriteSessionFactory(pool, CreateTelemetry());
                await using var session = await factory.OpenForWritingAsync(
                    SyntheticMailAccount.AccountId,
                    Inbox,
                    scope.GetRequiredService<IMailTransportSecurityPolicyReader>()
                        .GetPolicy(SyntheticMailAccount.AccountId),
                    token);

                return await session.CopyAsync(
                    occurrence,
                    ArchivePath,
                    new InMemoryMailboxMutationJournal(),
                    token);
            },
            cancellationToken);

        // The two stages the stopped run had reached, written through the production store so the resumed attempt reads
        // a row nothing here shaped by hand.
        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (scope, session, token) =>
                {
                    var store = scope.GetRequiredService<IMailboxMutationRecordStore>();
                    await store.CountAttemptAsync(session, recordId, token);

                    // The same call the fallback path makes, so the row says a source removal is still owed. Reaching
                    // the stage through the generic advance would leave that false and arrange a relocation the copy
                    // had already finished — which is not the state this test exists to resume from.
                    await store.RecordPlacementIssuedAsync(session, recordId, requiresSourceRemoval: true, token);
                    await store.AdvanceAsync(
                        session,
                        recordId,
                        MailboxMutationStage.PlacementConfirmed,
                        placement,
                        token);
                },
                cancellationToken));
    }

    /// <summary>Runs the production performer over a connection whose server advertises no <c>MOVE</c>.</summary>
    /// <remarks>
    /// Only the client factory is substituted, for the reason the sibling class states: the capability mask is what a
    /// test has to control and the production registration hands the pool a plain <see cref="ImapClient" />. The store,
    /// the commit policy, and the performer itself are the production ones.
    /// </remarks>
    private static async Task<MailboxMutationOutcome> ResumeWithoutMoveExtensionAsync(
        IServiceProvider scope,
        MailboxMutationRequest request,
        CancellationToken cancellationToken)
    {
        await using var pool = CreateMoveMaskedPool(scope);
        var performer = new MailboxMutationPerformer(
            scope.GetRequiredService<IMailboxMutationRecordStore>(),
            new MailKitImapWriteSessionFactory(pool, CreateTelemetry()),
            scope.GetRequiredService<OptimisticConcurrencyRetryPolicy>(),
            scope.GetRequiredService<IMailboxMutationAuditTrail>(),
            new MailboxMutationOptions());

        return await performer.PerformAsync(
            request,
            Inbox,
            scope.GetRequiredService<IMailTransportSecurityPolicyReader>()
                .GetPolicy(SyntheticMailAccount.AccountId),
            cancellationToken);
    }

    private static MailboxWriteConnectionPool CreateMoveMaskedPool(IServiceProvider scope) => new(
        () => CapabilityMaskedImapClient.HidingCapabilities(ImapCapabilities.Move),
        scope.GetRequiredService<IServiceScopeFactory>(),
        scope.GetRequiredService<OutboundOperationExecutor>(),
        scope.GetRequiredService<ITransientFailureClassifier>(),
        new MailboxWriteSessionOptions(),
        TimeProvider.System,
        NullLogger<MailboxWriteConnectionPool>.Instance);

    private static MailboxMutationTelemetry CreateTelemetry() =>
        new(NullLogger<MailboxMutationTelemetry>.Instance, TimeProvider.System);

    /// <summary>Reads the one recorded mutation for an occurrence straight out of its table.</summary>
    /// <remarks>
    /// Read from the row rather than through the port, because the port's reader answers which mutations are
    /// outstanding and a completed one has left that answer by design. What is asserted here is the completion itself
    /// and the identity the server named, both of which are columns.
    /// </remarks>
    private static Task<MailboxMutationRow> ReadRecordRowAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrence,
        CancellationToken cancellationToken)
    {
        var alias = occurrence.FolderResolutionId.Alias.Value;
        var generation = occurrence.FolderResolutionId.Generation.Value;
        var uid = occurrence.Uid.Value;

        return services.InScopeAsync(
            async (scope, token) => await scope
                .GetRequiredService<MailFathomDbContext>()
                .MailboxMutations
                .AsNoTracking()
                .Where(mutation => mutation.MailFolder.Alias == alias
                    && mutation.MailFolder.ResolutionGeneration == generation
                    && mutation.Uid == uid)
                .Select(mutation => new MailboxMutationRow(
                    mutation.Stage,
                    mutation.RequiresSourceRemoval,
                    mutation.PlacementUidValidity,
                    mutation.PlacementUid,
                    mutation.AttemptCount))
                .SingleAsync(token),
            cancellationToken);
    }

    private static Task<PersistenceCommitResult> CommitInboxBindingAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) => services.CommitAsync(
            (scope, session, token) => scope.GetRequiredService<IMailFolderResolutionStore>().SaveResolutionAsync(
                session,
                SyntheticMailAccount.AccountId,
                Inbox,
                token),
            cancellationToken);

    private static Task<StoredEmailId> StoreMetadataAsync(
        OrchestratedMailFathomServices services,
        EmailOccurrenceId occurrence,
        string subject,
        CancellationToken cancellationToken) => CommitForAsync(
            services,
            (scope, session, token) => scope.GetRequiredService<IEmailMetadataRepository>().UpsertMetadataAsync(
                session,
                SyntheticEmail.RemoteMetadataOf(occurrence, subject),
                extractedMetadata: null,
                StoredEmailContentAvailability.ExceededSizeLimit,
                token),
            cancellationToken);

    /// <summary>Commits one write and hands back what it produced, which the shared helper cannot because it reports the commit.</summary>
    /// <remarks>The commit result is asserted here instead, so arrangement that silently conflicted fails where it happened.</remarks>
    private static Task<TResult> CommitForAsync<TResult>(
        OrchestratedMailFathomServices services,
        Func<IServiceProvider, IPersistenceSession, CancellationToken, Task<TResult>> write,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                await using var session = await scope.GetRequiredService<IPersistenceSessionFactory>()
                    .BeginSessionAsync(token);

                var produced = await write(scope, session, token);

                Assert.Equal(PersistenceCommitResult.Committed, await session.CommitAsync(token));

                return produced;
            },
            cancellationToken);

    private static async Task<EmailOccurrenceId> DeliverAndLocateAsync(
        OrchestratedMailbox mailbox,
        string subject,
        CancellationToken cancellationToken)
    {
        await mailbox.DeliverAsync(subject, cancellationToken);

        var inbox = await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken);
        var delivered = Assert.Single(inbox, email => email.Subject == subject);

        return EmailOccurrenceId.Create(
            SyntheticMailAccount.AccountId,
            Inbox.Id,
            await mailbox.ReadUidValidityAsync(OrchestratedMailbox.InboxPath, cancellationToken),
            delivered.Uid);
    }

    /// <summary>The columns of one mutation record a test reads back.</summary>
    private sealed record MailboxMutationRow(
        MailboxMutationStage Stage,
        bool RequiresSourceRemoval,
        uint? PlacementUidValidity,
        uint? PlacementUid,
        int AttemptCount);
}
