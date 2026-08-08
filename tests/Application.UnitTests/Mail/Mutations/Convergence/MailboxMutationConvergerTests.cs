// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Mutations.Convergence;

public sealed class MailboxMutationConvergerTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("personal");

    private static readonly MailFolderResolution InboxFolder = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        RemoteFolderPath.Create("INBOX", '/'));

    private static readonly RemoteFolderPath ArchivePath = RemoteFolderPath.Create("Archive", '/');

    private static readonly MailTransportSecurityPolicy TransportPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    /// <summary>
    /// The restart case, which is the whole point: the previous process wrote the record and stopped, and the first run
    /// after it carries the change the rest of the way with nobody asking for anything.
    /// </summary>
    [Fact]
    public async Task ConvergeAsync_AMutationLeftHalfFinishedByAStoppedProcess_IsCarriedToCompletion()
    {
        // Arrange
        var context = new ConvergerContext();
        var request = await context.LeaveOutstandingAsync(
            RelocationRequest(),
            record => record with { Stage = MailboxMutationStage.PlacementConfirmed, RequiresSourceRemoval = true });

        // Act
        var report = await context.Converger.ConvergeAsync(Account, CancellationToken.None);

        // Assert
        Assert.Equal(1, report.CompletedCount);
        Assert.Equal(MailboxMutationStage.Completed, context.Store.RecordOf(request).Stage);
        await context.WriteSession.Received(1).RelocateAsync(
            request.Occurrence,
            ArchivePath,
            Arg.Any<IMailboxMutationJournal>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The binding the record was written against is what a resumed attempt selects, not one resolved afresh.</summary>
    [Fact]
    public async Task ConvergeAsync_ResumingAMutation_OpensTheWriteSessionOnTheRecordedBinding()
    {
        // Arrange
        var context = new ConvergerContext();
        context.Store.BindFolder(InboxFolder);
        await context.LeaveOutstandingAsync(RelocationRequest(), record => record);

        // Act
        await context.Converger.ConvergeAsync(Account, CancellationToken.None);

        // Assert
        await context.WriteSessionFactory.Received(1).OpenForWritingAsync(
            Account,
            InboxFolder,
            TransportPolicy,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A server that is not answering fails the pass rather than spinning inside it, and the failure is what the caller
    /// backs the account off on. The record keeps the code, so what an operator reads is why rather than only that.
    /// </summary>
    [Fact]
    public async Task ConvergeAsync_WhenTheServerIsUnreachable_ReportsTheFailureAndAttemptsTheMutationOnce()
    {
        // Arrange
        var context = new ConvergerContext();
        var request = await context.LeaveOutstandingAsync(RelocationRequest(), record => record);
        context.FailRelocationWith(new MailboxUnavailableException(
            Account,
            new TimeoutException("The mail server did not answer within its budget.")));

        // Act
        var report = await context.Converger.ConvergeAsync(Account, CancellationToken.None);

        // Assert
        var record = context.Store.RecordOf(request);
        Assert.Equal(1, report.FailedCount);
        Assert.Equal(0, report.CompletedCount);
        Assert.Equal(1, record.AttemptCount);
        Assert.Equal(MailFathomErrorCode.MailboxUnavailable, record.LastFailure);
    }

    /// <summary>One broken change must not stop the ones that are not broken, which is the whole of "does not block unrelated work".</summary>
    [Fact]
    public async Task ConvergeAsync_WhenOneMutationFails_TheAccountsOtherMutationsStillConverge()
    {
        // Arrange
        var context = new ConvergerContext();
        var failing = await context.LeaveOutstandingAsync(RelocationRequest(), record => record);
        var healthy = await context.LeaveOutstandingAsync(DeleteRequest(uid: 43U), record => record);
        context.FailRelocationWith(new MailboxUnavailableException(Account, new TimeoutException("Not answering.")));

        // Act
        var report = await context.Converger.ConvergeAsync(Account, CancellationToken.None);

        // Assert
        Assert.Equal(1, report.FailedCount);
        Assert.Equal(1, report.CompletedCount);
        Assert.Equal(MailFathomErrorCode.MailboxUnavailable, context.Store.RecordOf(failing).LastFailure);
        Assert.Equal(MailboxMutationStage.Completed, context.Store.RecordOf(healthy).Stage);
    }

    /// <summary>
    /// A refusal the server has already given is an answer rather than a bad moment, so the change stops at once
    /// instead of being attempted once per run until its bound is spent — and the pass counts it as given up on rather
    /// than as failed, because counting it as failed would promise an attempt nobody will make and would back the
    /// account off from a server that is working.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConvergeAsync_WhenTheServerRefusesTheChangeOutright_GivesItUpWithoutFailingThePass(
        bool isUnsupported)
    {
        // Arrange
        var context = new ConvergerContext();
        var request = await context.LeaveOutstandingAsync(RelocationRequest(), record => record);
        var expectedFailure = isUnsupported
            ? MailFathomErrorCode.MailboxMutationUnsupported
            : MailFathomErrorCode.MailboxMutationDestinationMissing;
        context.FailRelocationWith(isUnsupported
            ? new MailboxMutationUnsupportedException(
                Account,
                InboxFolder.Alias,
                MailboxMutation.Relocate,
                "UIDPLUS extension (RFC 4315)")
            : new MailboxDestinationFolderMissingException(
                Account,
                InboxFolder.Alias,
                MailboxMutation.Relocate,
                new InvalidOperationException("The folder could not be found.")));

        // Act
        var report = await context.Converger.ConvergeAsync(Account, CancellationToken.None);

        // Assert
        var record = context.Store.RecordOf(request);
        Assert.Equal(MailboxMutationStage.Abandoned, record.Stage);
        Assert.Equal(1, record.AttemptCount);
        Assert.Equal(expectedFailure, record.LastFailure);
        Assert.Equal(1, report.DeadLetteredCount);
        Assert.Equal(0, report.FailedCount);
        Assert.Contains(
            report.Outstanding,
            group => group.Lifecycle == MailboxMutationLifecycle.DeadLettered && group.Count == 1);
    }

    /// <summary>
    /// The command that may never be issued twice is never issued again, whichever mutation it belonged to. This is the
    /// assertion the whole unknown-outcome design exists for.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConvergeAsync_AnUnacknowledgedPlacement_IsNeverIssuedAgain(bool isCopy)
    {
        // Arrange
        var context = new ConvergerContext();
        var request = isCopy ? CopyRequest() : RelocationRequest();
        await context.LeaveOutstandingAsync(
            request,
            record => record with { Stage = MailboxMutationStage.PlacementIssued });

        // Act
        var report = await context.Converger.ConvergeAsync(Account, CancellationToken.None);

        // Assert
        Assert.Equal(1, report.DeferredCount);
        await context.WriteSessionFactory.DidNotReceive().OpenForWritingAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderResolution>(),
            Arg.Any<MailTransportSecurityPolicy>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The one unknown outcome the mailbox itself can settle: a relocation carried by <c>MOVE</c> whose source has since
    /// been seen to leave its folder. The server removed it, so the command ran.
    /// </summary>
    [Fact]
    public async Task ConvergeAsync_AnUnacknowledgedNativeRelocationWhoseSourceHasGone_IsCompletedFromTheObservation()
    {
        // Arrange
        var context = new ConvergerContext();
        var request = await context.LeaveOutstandingAsync(
            RelocationRequest(),
            record => record with
            {
                Stage = MailboxMutationStage.PlacementIssued,
                RequiresSourceRemoval = false,
                SourceRemovalObservedAt = context.Clock.GetUtcNow(),
            });

        // Act
        var report = await context.Converger.ConvergeAsync(Account, CancellationToken.None);

        // Assert
        Assert.Equal(1, report.CompletedCount);
        Assert.Equal(MailboxMutationStage.Completed, context.Store.RecordOf(request).Stage);
        await context.WriteSessionFactory.DidNotReceive().OpenForWritingAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderResolution>(),
            Arg.Any<MailTransportSecurityPolicy>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An outcome nothing can establish still has to stop being apparently in flight, because a change that looks busy
    /// forever is exactly the failure mode this design exists to remove.
    /// </summary>
    [Fact]
    public async Task ConvergeAsync_AnUnknownOutcomeThatOutlastsItsGrace_IsDeadLetteredAndStaysVisible()
    {
        // Arrange
        var context = new ConvergerContext(unknownOutcomeGrace: TimeSpan.FromHours(6));
        var request = await context.LeaveOutstandingAsync(
            CopyRequest(),
            record => record with { Stage = MailboxMutationStage.PlacementIssued });

        // Act
        context.Clock.Advance(TimeSpan.FromHours(7));
        var report = await context.Converger.ConvergeAsync(Account, CancellationToken.None);

        // Assert
        var record = context.Store.RecordOf(request);
        Assert.Equal(1, report.DeadLetteredCount);
        Assert.Equal(MailboxMutationStage.Abandoned, record.Stage);
        Assert.Equal(MailFathomErrorCode.MailboxMutationOutcomeUnknown, record.LastFailure);
        Assert.Equal(MailboxMutationLifecycle.DeadLettered, record.Lifecycle);
    }

    /// <summary>A record nothing will attempt again is still read back, because being seen is what giving up on it buys.</summary>
    [Fact]
    public async Task ConvergeAsync_AnAlreadyDeadLetteredMutation_IsReportedAndNotAttempted()
    {
        // Arrange
        var context = new ConvergerContext();
        await context.LeaveOutstandingAsync(
            RelocationRequest(),
            record => record with { Stage = MailboxMutationStage.Abandoned });

        // Act
        var report = await context.Converger.ConvergeAsync(Account, CancellationToken.None);

        // Assert
        Assert.True(report.ChangedNothing);
        Assert.Contains(
            report.Outstanding,
            group => group.Lifecycle == MailboxMutationLifecycle.DeadLettered
                && group.Mutation == MailboxMutation.Relocate
                && group.Count == 1);
        await context.WriteSessionFactory.DidNotReceive().OpenForWritingAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderResolution>(),
            Arg.Any<MailTransportSecurityPolicy>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Most passes have nothing to do, and one that does nothing must not cost a mail server anything.</summary>
    [Fact]
    public async Task ConvergeAsync_AnAccountWithNothingOutstanding_TouchesNoMailServer()
    {
        // Arrange
        var context = new ConvergerContext();

        // Act
        var report = await context.Converger.ConvergeAsync(Account, CancellationToken.None);

        // Assert
        Assert.True(report.ChangedNothing);
        Assert.Empty(report.Outstanding);
        Assert.Equal(0, report.DeferredCount);
        await context.WriteSessionFactory.DidNotReceive().OpenForWritingAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderResolution>(),
            Arg.Any<MailTransportSecurityPolicy>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A backlog is drained over runs rather than turning one run into an unbounded sequence of round trips.</summary>
    [Fact]
    public async Task ConvergeAsync_MoreOutstandingThanOnePassTakes_ConvergesTheOldestAndLeavesTheRest()
    {
        // Arrange
        var context = new ConvergerContext(maxMutationsPerPass: 2);

        // Recorded one at a time, because the store stamps each record as it is written and the pass takes the oldest
        // first; writing them together would leave which two it takes to whichever call happened to run first.
        foreach (var uid in Enumerable.Range(41, 3).Select(offset => (uint)offset))
        {
            await context.LeaveOutstandingAsync(DeleteRequest(uid), record => record);
        }

        // Act
        var report = await context.Converger.ConvergeAsync(Account, CancellationToken.None);

        // Assert
        Assert.Equal(2, report.CompletedCount);
        Assert.Contains(
            report.Outstanding,
            group => group.Lifecycle == MailboxMutationLifecycle.Pending && group.Count == 1);
    }

    /// <summary>The counts and the age are what an operator reads, so they have to name the kind as well as the lifecycle.</summary>
    [Fact]
    public async Task ConvergeAsync_OutstandingMutations_AreCountedByKindAndLifecycle()
    {
        // Arrange
        var context = new ConvergerContext();
        var recordedAt = context.Clock.GetUtcNow();
        await context.LeaveOutstandingAsync(
            CopyRequest(),
            record => record with { Stage = MailboxMutationStage.PlacementIssued, RecordedAt = recordedAt });
        await context.LeaveOutstandingAsync(
            RelocationRequest(),
            record => record with { Stage = MailboxMutationStage.Abandoned, RecordedAt = recordedAt });

        // Act
        var report = await context.Converger.ConvergeAsync(Account, CancellationToken.None);

        // Assert
        var outstanding = report.Outstanding
            .Select(group => (
                Mutation: group.Mutation.Name,
                Lifecycle: group.Lifecycle.Name,
                group.Count,
                group.OldestRecordedAt))
            .OrderBy(group => group.Mutation, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ExpectedOutstandingCounts(recordedAt), outstanding);
    }

    private static (string Mutation, string Lifecycle, int Count, DateTimeOffset OldestRecordedAt)[]
        ExpectedOutstandingCounts(DateTimeOffset recordedAt) =>
        [
            ("copy", "converging", 1, recordedAt),
            ("relocate", "dead-lettered", 1, recordedAt),
        ];

    private static EmailOccurrenceId Occurrence(uint uid) => EmailOccurrenceId.Create(
        Account,
        InboxFolder.Id,
        ImapUidValidity.Create(7U),
        ImapUid.Create(uid));

    private static MailboxMutationRequest RelocationRequest() => MailboxMutationRequest.Relocate(
        StoredEmailId.Create(Guid.CreateVersion7()),
        Occurrence(42U),
        MailboxMutationRequester.Rule("file-newsletters", 3),
        ArchivePath);

    private static MailboxMutationRequest CopyRequest() => MailboxMutationRequest.Copy(
        StoredEmailId.Create(Guid.CreateVersion7()),
        Occurrence(42U),
        MailboxMutationRequester.Rule("keep-a-copy", 4),
        ArchivePath);

    private static MailboxMutationRequest DeleteRequest(uint uid) => MailboxMutationRequest.Delete(
        StoredEmailId.Create(Guid.CreateVersion7()),
        Occurrence(uid),
        MailboxMutationRequester.Rule("drop-notifications", 5),
        AuthoredDeleteEmailDisposition.RetainLocalCopy);

    /// <summary>Assembles the converger over the same in-memory record store the performer's own tests use.</summary>
    /// <remarks>
    /// The performer is the real one rather than a substitute, because what convergence delegates to it — the attempt
    /// count, the settled answers, the stage a resumed sequence continues from — is exactly what these tests are about.
    /// Only the mail server below it is substituted.
    /// </remarks>
    private sealed class ConvergerContext
    {
        internal ConvergerContext(
            int maxMutationsPerPass = 50,
            TimeSpan? unknownOutcomeGrace = null,
            int maximumAttempts = 5)
        {
            var persistenceSession = Substitute.For<IPersistenceSession>();
            persistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(persistenceSession);
            var commitPolicy = new OptimisticConcurrencyRetryPolicy(
                sessionFactory,
                new PersistenceConcurrencyOptions { MaximumCommitAttempts = 1 },
                TimeProvider.System);

            this.WriteSession = Substitute.For<IMailboxWriteSession>();
            this.WriteSession.RelocateAsync(
                    Arg.Any<EmailOccurrenceId>(),
                    Arg.Any<RemoteFolderPath>(),
                    Arg.Any<IMailboxMutationJournal>(),
                    Arg.Any<CancellationToken>())
                .Returns(RemoteEmailPlacement.NotReported());
            this.WriteSession.CopyAsync(
                    Arg.Any<EmailOccurrenceId>(),
                    Arg.Any<RemoteFolderPath>(),
                    Arg.Any<IMailboxMutationJournal>(),
                    Arg.Any<CancellationToken>())
                .Returns(RemoteEmailPlacement.NotReported());

            this.WriteSessionFactory = Substitute.For<IMailboxWriteSessionFactory>();
            this.WriteSessionFactory.OpenForWritingAsync(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<MailFolderResolution>(),
                    Arg.Any<MailTransportSecurityPolicy>(),
                    Arg.Any<CancellationToken>())
                .Returns(this.WriteSession);

            this.Performer = new MailboxMutationPerformer(
                this.Store,
                this.WriteSessionFactory,
                commitPolicy,
                this.AuditTrail,
                new MailboxMutationOptions { MaximumAttempts = maximumAttempts });

            var transportSecurityPolicyReader = Substitute.For<IMailTransportSecurityPolicyReader>();
            transportSecurityPolicyReader.GetPolicy(Account).Returns(TransportPolicy);

            this.Converger = new MailboxMutationConverger(
                this.Store,
                this.Performer,
                transportSecurityPolicyReader,
                commitPolicy,
                this.AuditTrail,
                new MailboxConvergenceOptions
                {
                    MaxMutationsPerPass = maxMutationsPerPass,
                    UnknownOutcomeGrace = unknownOutcomeGrace ?? TimeSpan.FromHours(6),
                },
                this.Clock);
        }

        internal InMemoryMailboxMutationRecordStore Store { get; } = new();

        internal RecordingMailboxMutationAuditTrail AuditTrail { get; } = new();

        internal FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero));

        internal IMailboxWriteSession WriteSession { get; }

        internal IMailboxWriteSessionFactory WriteSessionFactory { get; }

        internal MailboxMutationPerformer Performer { get; }

        internal MailboxMutationConverger Converger { get; }

        /// <summary>Writes a record down and leaves it in the state a stopped process would have.</summary>
        internal async Task<MailboxMutationRequest> LeaveOutstandingAsync(
            MailboxMutationRequest request,
            Func<MailboxMutationRecord, MailboxMutationRecord> stoppedAt)
        {
            await this.Store.OpenAsync(
                Substitute.For<IPersistenceSession>(),
                request,
                CancellationToken.None);
            this.Store.Arrange(request, stoppedAt);

            return request;
        }

        internal void FailRelocationWith(Exception failure) =>
            this.WriteSession.RelocateAsync(
                    Arg.Any<EmailOccurrenceId>(),
                    Arg.Any<RemoteFolderPath>(),
                    Arg.Any<IMailboxMutationJournal>(),
                    Arg.Any<CancellationToken>())
                .ThrowsAsync(failure);
    }
}
