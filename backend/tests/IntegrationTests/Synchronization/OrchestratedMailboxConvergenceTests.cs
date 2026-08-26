// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Application.Resilience;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
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

/// <summary>Proves that a change nobody finished converges by itself, against a real mail server and a real database.</summary>
/// <remarks>
/// <para>
/// Three tests, because there are three claims no substitute can establish. The first is that a mutation a stopped
/// process left in a non-final state completes on the next start with no operator action, which needs a server that
/// really copied a message, a database that really kept the stage across the stop, and the production convergence pass
/// reading both. The second is that a change whose destination folder was removed stops instead of being attempted
/// again — which needs a server that really answers that the folder is not there, since the whole point of the
/// translation is what a real mail library raises. The third is the count only a real mailbox can settle: a copy whose
/// answer was never read leaves one message in the destination folder and convergence leaves it at one.
/// </para>
/// <para>
/// Everything else about convergence — the grace period, the counts, which stage a resumed sequence continues from — is
/// a rule the unit suite already exercises against substitutes and buys nothing here.
/// </para>
/// </remarks>
[Collection(OrchestratedInfrastructureCollectionDefinition.Name)]
public sealed class OrchestratedMailboxConvergenceTests(MailFathomOrchestrationFixture orchestration)
{
    private const string ArchiveFolderName = "ConvergenceArchive";

    private const string RemovedFolderName = "ConvergenceRemovedTarget";

    private const string CopyFolderName = "ConvergenceCopyTarget";

    /// <summary>The alias this class owns, bound to the real inbox so a write session selects it.</summary>
    private static readonly MailFolderResolution Inbox = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("convergence-inbox"),
        RemoteFolderPath.Create(OrchestratedMailbox.InboxPath, hierarchyDelimiter: '.'));

    private static readonly RemoteFolderPath ArchivePath =
        RemoteFolderPath.Create(ArchiveFolderName, hierarchyDelimiter: '.');

    private static readonly RemoteFolderPath RemovedPath =
        RemoteFolderPath.Create(RemovedFolderName, hierarchyDelimiter: '.');

    private static readonly RemoteFolderPath CopyPath =
        RemoteFolderPath.Create(CopyFolderName, hierarchyDelimiter: '.');

    private static readonly MailboxMutationRequester Requester =
        MailboxMutationRequester.Rule("converge-to-archive", "1");

    /// <summary>
    /// The restart case the whole design exists for. A process stopped between the copy and the expunge left the
    /// message in both folders and a record saying so; the next process runs a convergence pass, which nobody asked for,
    /// and the mailbox ends in the state that was requested.
    /// </summary>
    [Fact]
    public async Task ConvergeAsync_AMutationAStoppedProcessLeftUnfinished_CompletesWithNoOperatorAction()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        await mailbox.RecreateFolderAsync(ArchiveFolderName, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await CommitInboxBindingAsync(services, cancellationToken);

        var subject = $"converge-relocate-{Guid.NewGuid():N}";
        var occurrence = await mailbox.DeliverAndLocateAsync(Inbox.Id, subject, cancellationToken);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            occurrence,
            subject,
            cancellationToken);
        var request = MailboxMutationRequest.Relocate(storedEmailId, SyntheticMailAccount.Owner, occurrence, Requester, ArchivePath);

        await StopAfterTheCopyAsync(services, request, occurrence, cancellationToken);

        // The state a stopped process leaves: the copy landed, the source is still there, and the record is the only
        // thing that knows the two are one relocation.
        Assert.Contains(
            await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken),
            email => email.Subject == subject);
        Assert.Equal(
            MailboxMutationStage.PlacementConfirmed,
            (await ReadRecordRowAsync(services, occurrence, cancellationToken)).Stage);

        // Act
        var report = await services.InScopeAsync(
            (scope, token) => ConvergeWithoutMoveExtensionAsync(scope, token),
            cancellationToken);

        // Assert
        Assert.Equal(1, report.CompletedCount);
        Assert.DoesNotContain(
            await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken),
            email => email.Subject == subject);
        Assert.Single(
            await mailbox.ReadAsync(ArchiveFolderName, cancellationToken),
            email => email.Subject == subject);
        Assert.Equal(
            MailboxMutationStage.Completed,
            (await ReadRecordRowAsync(services, occurrence, cancellationToken)).Stage);
    }

    /// <summary>
    /// A destination folder somebody removed is an answer the server has already given, so the change reaches its
    /// terminal visible stage on the first pass rather than being attempted once per run until its bound is spent.
    /// </summary>
    [Fact]
    public async Task ConvergeAsync_AMutationWhoseTargetFolderWasRemoved_StopsVisiblyInsteadOfRetrying()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        // Created and removed rather than recreated and removed: the server has to have accepted the name for its
        // absence to mean a folder somebody deleted, and a folder this suite selected cannot be deleted at all.
        await mailbox.CreateFolderAsync(RemovedFolderName, cancellationToken);
        await mailbox.DeleteFolderAsync(RemovedFolderName, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await CommitInboxBindingAsync(services, cancellationToken);

        var subject = $"converge-missing-target-{Guid.NewGuid():N}";
        var occurrence = await mailbox.DeliverAndLocateAsync(Inbox.Id, subject, cancellationToken);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            occurrence,
            subject,
            cancellationToken);
        var request = MailboxMutationRequest.Relocate(storedEmailId, SyntheticMailAccount.Owner, occurrence, Requester, RemovedPath);
        await RecordIntentAsync(services, request, cancellationToken);

        // Act
        var report = await services.InScopeAsync(
            (scope, token) => scope.GetRequiredService<MailboxMutationConverger>()
                .ConvergeAsync(SyntheticMailAccount.Account, token),
            cancellationToken);

        // Assert
        var row = await ReadRecordRowAsync(services, occurrence, cancellationToken);
        Assert.Equal(MailboxMutationStage.Abandoned, row.Stage);
        Assert.Equal(1, row.AttemptCount);
        Assert.Equal(MailFathomErrorCode.MailboxMutationDestinationMissing.Value, row.LastFailureCode);
        Assert.Contains(
            report.Outstanding,
            group => group.Lifecycle == MailboxMutationLifecycle.DeadLettered
                && group.Mutation == MailboxMutation.Relocate);

        // The message is where it was: a refusal that had copied first would have left one in a folder nobody can name.
        Assert.Contains(
            await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken),
            email => email.Subject == subject);
    }

    /// <summary>
    /// The one command that may never be repeated, left in the state that would repeat it. A process stopped between
    /// the <c>UID COPY</c> and its answer left a message in the destination folder and a record that cannot know
    /// whether it did; the pass settles the record without issuing anything, so the folder still holds one message and
    /// not two.
    /// </summary>
    /// <remarks>
    /// GreenMail advertises <c>UIDPLUS</c>, so the copy ran the <c>COPYUID</c> path — which is the one worth proving
    /// here, because it is the path a deployment gets and the one on which a repeat would be indistinguishable from a
    /// message the owner copied deliberately. The grace period is set to nothing rather than waited out: what the wait
    /// is for is a later synchronization run settling the placement, and no such run can settle a copy at all.
    /// </remarks>
    [Fact]
    public async Task ConvergeAsync_ACopyWhoseAnswerWasNeverRead_LeavesOneMessageAndStopsClaimingToKnow()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var mailbox = new OrchestratedMailbox(orchestration.MailServer);
        await mailbox.RecreateFolderAsync(CopyFolderName, cancellationToken);

        await using var services = await OrchestratedMailFathomServices.StartAsync(orchestration, cancellationToken);
        await CommitInboxBindingAsync(services, cancellationToken);

        var subject = $"converge-copy-{Guid.NewGuid():N}";
        var occurrence = await mailbox.DeliverAndLocateAsync(Inbox.Id, subject, cancellationToken);
        var storedEmailId = await StoredSyntheticEmail.MetadataOnlyAsync(
            services,
            occurrence,
            subject,
            cancellationToken);
        var request = MailboxMutationRequest.Copy(storedEmailId, SyntheticMailAccount.Owner, occurrence, Requester, CopyPath);

        await StopAfterTheCopyCommandAsync(services, request, occurrence, cancellationToken);

        // The state a stopped process leaves: the message is in both folders, and the record says only that the command
        // went out.
        Assert.Single(await mailbox.ReadAsync(CopyFolderName, cancellationToken), email => email.Subject == subject);
        Assert.Equal(
            MailboxMutationStage.PlacementIssued,
            (await ReadRecordRowAsync(services, occurrence, cancellationToken)).Stage);

        // Act
        var report = await services.InScopeAsync(ConvergeWithoutGraceAsync, cancellationToken);

        // Assert
        Assert.Contains(
            report.Outstanding,
            group => group.Lifecycle == MailboxMutationLifecycle.DeadLettered
                && group.Mutation == MailboxMutation.Copy);
        Assert.Single(await mailbox.ReadAsync(CopyFolderName, cancellationToken), email => email.Subject == subject);
        Assert.Contains(
            await mailbox.ReadAsync(OrchestratedMailbox.InboxPath, cancellationToken),
            email => email.Subject == subject);

        var row = await ReadRecordRowAsync(services, occurrence, cancellationToken);
        Assert.Equal(MailboxMutationStage.Abandoned, row.Stage);
        Assert.Equal(MailFathomErrorCode.MailboxMutationOutcomeUnknown.Value, row.LastFailureCode);
    }

    /// <summary>Runs the production convergence pass with nothing left to wait for.</summary>
    /// <remarks>
    /// Everything below the options is the production registration, including the write session factory and the pool
    /// behind it: a copy that must not be reissued has to be proven against the connection a deployment gets rather than
    /// against one a test assembled.
    /// </remarks>
    private static async Task<MailboxConvergenceReport> ConvergeWithoutGraceAsync(
        IServiceProvider scope,
        CancellationToken cancellationToken)
    {
        var commitPolicy = scope.GetRequiredService<OptimisticConcurrencyRetryPolicy>();
        var store = scope.GetRequiredService<IMailboxMutationRecordStore>();
        var converger = new MailboxMutationConverger(
            store,
            new MailboxMutationPerformer(
                store,
                scope.GetRequiredService<IMailboxWriteSessionFactory>(),
                commitPolicy,
                scope.GetRequiredService<IMailboxMutationAuditTrail>(),
                new MailboxMutationOptions()),
            scope.GetRequiredService<IMailTransportSecurityPolicyReader>(),
            commitPolicy,
            scope.GetRequiredService<IMailboxMutationAuditTrail>(),
            new MailboxConvergenceOptions { UnknownOutcomeGrace = TimeSpan.Zero },
            TimeProvider.System);

        return await converger.ConvergeAsync(SyntheticMailAccount.Account, cancellationToken);
    }

    /// <summary>Copies the message for real and writes down only that the command went out.</summary>
    /// <remarks>
    /// The durable record is left at the one stage a retry may not act on, which is what a process dying between the
    /// command and its answer produces. The session is given a journal of its own so the confirmation the server did
    /// send reaches nothing durable.
    /// </remarks>
    private static async Task StopAfterTheCopyCommandAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRequest request,
        EmailOccurrenceId occurrence,
        CancellationToken cancellationToken)
    {
        var recordId = await RecordIntentAsync(services, request, cancellationToken);

        await services.InScopeAsync(
            async (scope, token) =>
            {
                var account = SyntheticMailAccount.AccountId;
                await using var session = await scope.GetRequiredService<IMailboxWriteSessionFactory>()
                    .OpenForWritingAsync(
                        account,
                        Inbox,
                        scope.GetRequiredService<IMailTransportSecurityPolicyReader>().GetPolicy(account),
                        token);

                return await session.CopyAsync(
                    occurrence,
                    CopyPath,
                    new InMemoryMailboxMutationJournal(),
                    token);
            },
            cancellationToken);

        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (scope, session, token) =>
                {
                    var store = scope.GetRequiredService<IMailboxMutationRecordStore>();
                    await store.CountAttemptAsync(session, recordId, token);
                    await store.RecordPlacementIssuedAsync(session, recordId, requiresSourceRemoval: false, token);
                },
                cancellationToken));
    }

    /// <summary>Runs the production convergence pass over a connection whose server advertises no <c>MOVE</c>.</summary>
    /// <remarks>
    /// Only the client factory is substituted, for the reason the sibling class states: the capability mask is what a
    /// test has to control, and a relocation resumed on the native path would have nothing left to do. The store, the
    /// commit policy, the performer, and the converger itself are the production ones.
    /// </remarks>
    private static async Task<MailboxConvergenceReport> ConvergeWithoutMoveExtensionAsync(
        IServiceProvider scope,
        CancellationToken cancellationToken)
    {
        await using var pool = CreateMoveMaskedPool(scope);
        var commitPolicy = scope.GetRequiredService<OptimisticConcurrencyRetryPolicy>();
        var store = scope.GetRequiredService<IMailboxMutationRecordStore>();
        var converger = new MailboxMutationConverger(
            store,
            new MailboxMutationPerformer(
                store,
                new MailKitImapWriteSessionFactory(pool, CreateTelemetry()),
                commitPolicy,
                scope.GetRequiredService<IMailboxMutationAuditTrail>(),
                new MailboxMutationOptions()),
            scope.GetRequiredService<IMailTransportSecurityPolicyReader>(),
            commitPolicy,
            scope.GetRequiredService<IMailboxMutationAuditTrail>(),
            new MailboxConvergenceOptions(),
            TimeProvider.System);

        return await converger.ConvergeAsync(SyntheticMailAccount.Account, cancellationToken);
    }

    /// <summary>Issues the copy half of a relocation and stops, leaving the record exactly where a crash would.</summary>
    private static async Task StopAfterTheCopyAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRequest request,
        EmailOccurrenceId occurrence,
        CancellationToken cancellationToken)
    {
        var recordId = await RecordIntentAsync(services, request, cancellationToken);

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

        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                async (scope, session, token) =>
                {
                    var store = scope.GetRequiredService<IMailboxMutationRecordStore>();
                    await store.CountAttemptAsync(session, recordId, token);
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

    private static Task<MailboxMutationRecordId> RecordIntentAsync(
        OrchestratedMailFathomServices services,
        MailboxMutationRequest request,
        CancellationToken cancellationToken) => services.CommitProducingAsync(
            async (scope, session, token) => (await scope.GetRequiredService<IMailboxMutationRecordStore>()
                .OpenAsync(session, request, token)).Id,
            cancellationToken);

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
    /// Read from the row rather than through the port, because a completed mutation has left the outstanding answer by
    /// design and what is asserted here is the completion itself.
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
                    mutation.AttemptCount,
                    mutation.LastFailureCode))
                .SingleAsync(token),
            cancellationToken);
    }

    private static async Task CommitInboxBindingAsync(
        OrchestratedMailFathomServices services,
        CancellationToken cancellationToken) =>
        Assert.Equal(
            PersistenceCommitResult.Committed,
            await services.CommitAsync(
                (scope, session, token) => scope.GetRequiredService<IMailFolderResolutionStore>().SaveResolutionAsync(
                    session,
                    SyntheticMailAccount.Account,
                    Inbox,
                    token),
                cancellationToken));

    /// <summary>The columns of one mutation record a test reads back.</summary>
    private sealed record MailboxMutationRow(MailboxMutationStage Stage, int AttemptCount, int? LastFailureCode);
}
