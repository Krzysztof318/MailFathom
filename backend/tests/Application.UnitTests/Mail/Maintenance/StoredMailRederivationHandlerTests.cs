// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Observability;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Maintenance;

/// <summary>Covers the work the deployment does once an operator has asked for a re-derivation and walked away.</summary>
/// <remarks>
/// What the segment owes is that the walk survives being stopped: the counts it committed are on the run, the position
/// its passes reached is what the next one resumes from, and something is in the queue to do the resuming. The three
/// together are what makes a mailbox of tens of thousands of messages finish without anybody watching.
/// </remarks>
public sealed class StoredMailRederivationHandlerTests
{
    /// <summary>How many emails one batch of a pass reads and commits together.</summary>
    private const int BatchSize = 50;

    /// <summary>What one bounded pass covers: the pass's batch size times its batch budget.</summary>
    private const int EmailsPerPass = BatchSize * 10;

    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static readonly StoredMailScope WholeAccount = new(MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work")), null);

    private readonly InMemoryStoredMailRederivationRunStore runs = new();
    private readonly IJobStore jobs = Substitute.For<IJobStore>();
    private readonly RecordingRederivationTelemetry telemetry = new();
    private readonly FakeTimeProvider timeProvider = new(Now);

    public StoredMailRederivationHandlerTests() => this.jobs
        .EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
        .Returns(JobEnqueueResult.Created(JobId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000002"))));

    /// <summary>A type names one payload contract, so a document of another shape is a defect rather than work.</summary>
    [Fact]
    public async Task RunAsync_APayloadOfAnotherContract_IsRefused()
    {
        // Arrange
        var handler = this.CreateHandler(new WalkStore(StoredMail(1)));

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() => handler.RunAsync(
            RunScheduledMailRulesJobPayload.For(WholeAccount.Account),
            TestContext.Current.CancellationToken));
    }

    /// <summary>A segment whose run has ended does nothing, because the work it was enqueued for is already done.</summary>
    [Fact]
    public async Task RunAsync_AScopeWhoseRunHasEnded_ReadsNoMailAndEnqueuesNothing()
    {
        // Arrange
        this.runs.Arrange(RunOf(segmentCount: 1) with { EndedAt = Now });

        var store = new WalkStore(StoredMail(1));

        // Act
        await this.CreateHandler(store).RunAsync(PayloadOf(WholeAccount), TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(store.CandidateScopes);
        Assert.Empty(this.EnqueuedRequests());
        Assert.Empty(this.telemetry.Runs);
    }

    /// <summary>A scope one attempt can finish is ended by it, and hands nothing on.</summary>
    [Fact]
    public async Task RunAsync_AScopeOneAttemptReachesTheEndOf_EndsTheRunAndEnqueuesNothing()
    {
        // Arrange
        this.runs.Arrange(RunOf(segmentCount: 1));

        // Act
        await this.CreateHandler(new WalkStore(StoredMail(3)))
            .RunAsync(PayloadOf(WholeAccount), TestContext.Current.CancellationToken);

        // Assert
        var run = this.runs.Find(WholeAccount)!;

        Assert.Equal(3, run.RederivedEmailCount);
        Assert.Equal(Now, run.EndedAt);
        Assert.False(run.IsOutstanding);
        Assert.Empty(this.EnqueuedRequests());
        Assert.True(this.telemetry.Runs.Single().ReachedEndOfScope);
    }

    /// <summary>An attempt stopped with mail ahead of it hands the rest to a segment of its own rather than failing.</summary>
    [Fact]
    public async Task RunAsync_AnAttemptStoppedWithMailRemaining_AdvancesTheSegmentAndEnqueuesIt()
    {
        // Arrange
        this.runs.Arrange(RunOf(segmentCount: 1));

        using CancellationTokenSource attempt = new();
        var store = new WalkStore(StoredMail(EmailsPerPass + 1), stopAfterBatches: 11, attempt);

        // Act
        await this.CreateHandler(store).RunAsync(PayloadOf(WholeAccount), attempt.Token);

        // Assert
        var run = this.runs.Find(WholeAccount)!;

        Assert.True(run.IsOutstanding);
        Assert.Equal(EmailsPerPass, run.RederivedEmailCount);
        Assert.Equal(2, run.SegmentCount);

        var enqueued = this.EnqueuedRequests().Single();

        Assert.Equal(JobType.RederiveStoredMail, enqueued.JobType);
        Assert.Equal(StoredMailRederivationRequests.KeyOf(run).Value, enqueued.Key.Value);
        Assert.Equal((false, true), (
            this.telemetry.Runs.Single().ReachedEndOfScope,
            this.telemetry.Runs.Single().HandedOnQueued));
    }

    /// <summary>
    /// A run that ended between the walk stopping and the segment writing down what carries the rest reports the end of
    /// the scope, because that is what happened: an overlapping attempt finished it. A segment that reported neither
    /// signal would end its span the way one killed where nobody recorded why does, which is what an operator reads as
    /// a deployment that stopped for no stated reason.
    /// </summary>
    [Fact]
    public async Task RunAsync_ARunThatEndedBeforeTheRestCouldBeHandedOn_ReportsTheEndOfTheScope()
    {
        // Arrange
        this.runs.Arrange(RunOf(segmentCount: 1));

        using CancellationTokenSource attempt = new();
        var store = new WalkStore(StoredMail(EmailsPerPass + 1), stopAfterBatches: 11, attempt);
        var handler = this.CreateHandler(store, new RunEndingOnceStopped(this.runs, attempt, Now));

        // Act
        await handler.RunAsync(PayloadOf(WholeAccount), attempt.Token);

        // Assert
        var run = this.runs.Find(WholeAccount)!;

        Assert.False(run.IsOutstanding);
        Assert.Equal(1, run.SegmentCount);
        Assert.Empty(this.EnqueuedRequests());
        Assert.Equal((true, (bool?)null), (
            this.telemetry.Runs.Single().ReachedEndOfScope,
            this.telemetry.Runs.Single().HandedOnQueued));
    }

    /// <summary>
    /// A queue at its bound is the one way a run stalls with nothing failing: it stays outstanding, nothing carries it,
    /// and no dead letter records it. The segment reports that rather than discarding it, because an operator watching
    /// the deployment would otherwise have only a progress figure that stopped moving.
    /// </summary>
    [Fact]
    public async Task RunAsync_AQueueThatRefusedTheRestOfTheWalk_ReportsTheRunAsCarriedByNothing()
    {
        // Arrange
        this.runs.Arrange(RunOf(segmentCount: 1));
        this.jobs
            .EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(JobEnqueueResult.RefusedAtCapacity());

        using CancellationTokenSource attempt = new();
        var store = new WalkStore(StoredMail(EmailsPerPass + 1), stopAfterBatches: 11, attempt);

        // Act
        await this.CreateHandler(store).RunAsync(PayloadOf(WholeAccount), attempt.Token);

        // Assert
        Assert.False(this.telemetry.Runs.Single().HandedOnQueued);
        Assert.Equal(2, this.runs.Find(WholeAccount)!.SegmentCount);
    }

    /// <summary>An attempt stopped inside a pass keeps what its committed batches re-read, not only what a whole pass reported.</summary>
    /// <remarks>
    /// This is the ordinary way an attempt ends rather than a rare one: the execution timeout is what stops most of
    /// them, and it lands inside a pass far more often than between two. Each batch therefore commits its counts with
    /// the position it reached, so the two can never disagree — a figure short of the mail that was really re-read
    /// would be permanent, because the next segment resumes past that position and never walks it again.
    /// </remarks>
    [Fact]
    public async Task RunAsync_AnAttemptStoppedInsideAPass_KeepsWhatItsCommittedBatchesReRead()
    {
        // Arrange
        this.runs.Arrange(RunOf(segmentCount: 1));

        using CancellationTokenSource attempt = new();
        var store = new WalkStore(StoredMail(EmailsPerPass + 1), stopAfterBatches: 3, attempt);

        // Act
        await this.CreateHandler(store).RunAsync(PayloadOf(WholeAccount), attempt.Token);

        // Assert
        var run = this.runs.Find(WholeAccount)!;

        Assert.True(run.IsOutstanding);
        Assert.Equal(BatchSize * 2, run.RederivedEmailCount);
        Assert.Equal(2, run.SegmentCount);
        Assert.Single(this.EnqueuedRequests());
    }

    /// <summary>Every pass is published beneath the segment's own report, which is what makes a slow walk attributable.</summary>
    [Fact]
    public async Task RunAsync_ASegmentRunningSeveralPasses_PublishesEachPassBeneathIt()
    {
        // Arrange
        this.runs.Arrange(RunOf(segmentCount: 1));

        // Act
        await this.CreateHandler(new WalkStore(StoredMail(EmailsPerPass + 3)))
            .RunAsync(PayloadOf(WholeAccount), TestContext.Current.CancellationToken);

        // Assert
        var published = this.telemetry.Runs.Single();

        Assert.Equal(WholeAccount.Account.Id, published.AccountId);
        Assert.Null(published.FolderAlias);
        Assert.Equal([EmailsPerPass, 3], [.. published.Passes.Select(pass => pass.RederivedEmailCount)]);
    }

    /// <summary>The counts an operator reads are the run's, so a second segment adds to what the first committed.</summary>
    [Fact]
    public async Task RunAsync_ASecondSegmentOfOneRun_AddsToWhatTheFirstCommitted()
    {
        // Arrange
        this.runs.Arrange(RunOf(segmentCount: 2) with { RederivedEmailCount = EmailsPerPass });

        // Act
        await this.CreateHandler(new WalkStore(StoredMail(2)))
            .RunAsync(PayloadOf(WholeAccount), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailsPerPass + 2, this.runs.Find(WholeAccount)!.RederivedEmailCount);
    }

    private static RederiveStoredMailJobPayload PayloadOf(StoredMailScope scope) =>
        RederiveStoredMailJobPayload.For(scope.Account, scope.Folder);

    /// <summary>Ends the scope's run on the first reading taken after the attempt was stopped, and reads through otherwise.</summary>
    /// <remarks>
    /// The walk raises the cancellation from the batch that met it, so the next reading of the run is the one the
    /// segment takes to write down what carries the rest of the scope. Ending it there is the race this covers: the
    /// walk saw an outstanding run, and by the time the segment wrote, an overlapping attempt had finished the scope.
    /// </remarks>
    private sealed class RunEndingOnceStopped(
        InMemoryStoredMailRederivationRunStore runs,
        CancellationTokenSource attempt,
        DateTimeOffset endedAt)
        : IStoredMailRederivationRunStore
    {
        private bool ended;

        public async Task<StoredMailRederivationRun?> FindAsync(
            StoredMailScope scope,
            CancellationToken cancellationToken)
        {
            if (attempt.IsCancellationRequested && !this.ended && runs.Find(scope) is { } outstanding)
            {
                this.ended = true;
                runs.Arrange(outstanding with { EndedAt = endedAt });
            }

            return await runs.FindAsync(scope, cancellationToken);
        }

        public Task SaveAsync(
            IPersistenceSession session,
            StoredMailRederivationRun run,
            CancellationToken cancellationToken) =>
            runs.SaveAsync(session, run, cancellationToken);
    }

    private static StoredMailRederivationRun RunOf(int segmentCount) => new()
    {
        RunId = StoredMailRederivationRunId.Create(Guid.Parse("0199a0c0-0000-7000-8000-00000000000b")),
        Scope = WholeAccount,
        RequestedAt = Now,
        SegmentCount = segmentCount,
    };

    /// <summary>Builds stored mail whose identifiers increase in the order the walk visits it.</summary>
    private static IReadOnlyList<StoredMailAwaitingRederivation> StoredMail(int count) =>
    [
        .. Enumerable.Range(1, count).Select(position => new StoredMailAwaitingRederivation(
            StoredEmailId.Create(Guid.Parse($"00000000-0000-0000-0000-{position:D12}")),
            EmailOccurrenceId.Create(
                MailAccountId.Create("work"),
                new MailFolderResolutionId(MailFolderAlias.Create("inbox"), MailFolderResolutionGeneration.First),
                ImapUidValidity.Create(5),
                ImapUid.Create((uint)position)))),
    ];

    private IReadOnlyList<JobEnqueueRequest> EnqueuedRequests() =>
    [
        .. this.jobs.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IJobStore.EnqueueAsync))
            .Select(call => (JobEnqueueRequest)call.GetArguments()[0]!),
    ];

    /// <summary>Builds the handler over one walk, optionally reading the run through a double of its own.</summary>
    /// <param name="store">The stored mail the walk finds.</param>
    /// <param name="runStore">What the segment reads the run through, which is the walk's own record unless a test moves it.</param>
    private StoredMailRederivationHandler CreateHandler(
        WalkStore store,
        IStoredMailRederivationRunStore? runStore = null)
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ => new CommittingSession());

        var commitPolicy = new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            new PersistenceConcurrencyOptions(),
            this.timeProvider);

        var contentStore = ContentStores.Substituted();
        byte[] rawMime = [1, 2, 3];
        contentStore
            .FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<StoredEmailContent?>(
                new StoredEmailContent(rawMime, rawMime.Length, SHA256.HashData(rawMime))));

        var mimeReader = Substitute.For<IEmailMimeReader>();
        mimeReader
            .ReadMetadataAsync(Arg.Any<RemoteEmailContent>(), Arg.Any<MailOwnerId>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(EmailMimeExtractionResult.Extracted(
                MetadataOf(call.Arg<RemoteEmailContent>()!.OccurrenceId))));

        return new StoredMailRederivationHandler(
            new StoredMailRederivation(
                store,
                this.runs,
                contentStore,
                mimeReader,
                commitPolicy,
                this.timeProvider,
                AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process)),
            runStore ?? this.runs,
            this.jobs,
            commitPolicy,
            this.telemetry);
    }

    private static ExtractedEmailMetadata MetadataOf(EmailOccurrenceId occurrenceId) =>
        new(
            occurrenceId,
            Subject: "Subject",
            SentAt: null,
            ReceivedAt: null,
            Participants: [],
            EmailThreadReferences.None,
            EmailAttachmentSummary.None,
            ExtractedEmailText.FromPlainTextBody("Body", "Body"),
            SenderAuthentication.NotEstablished());

    /// <summary>Stands in for the persisted walk state, and stops the attempt where a test says the deployment would.</summary>
    /// <remarks>
    /// The attempt is cancelled from inside the walk rather than on a timer, because what a test is arranging is the
    /// execution timeout elapsing between two passes — an instant the walk itself defines and a wall clock could only
    /// approximate.
    /// </remarks>
    private sealed class WalkStore(
        IReadOnlyList<StoredMailAwaitingRederivation> mail,
        int stopAfterBatches = int.MaxValue,
        CancellationTokenSource? attempt = null)
        : IStoredMailRederivationStore
    {
        private readonly Dictionary<StoredMailScope, StoredEmailId> positions = [];
        private int servedBatchCount;

        /// <summary>Which scope each candidate query was asked about, which is also how many batches were served.</summary>
        public List<StoredMailScope> CandidateScopes { get; } = [];

        public Task<StoredEmailId?> FindResumePositionAsync(
            StoredMailScope scope,
            CancellationToken cancellationToken) =>
            Task.FromResult(this.positions.TryGetValue(scope, out var position) ? position : (StoredEmailId?)null);

        public Task<IReadOnlyList<StoredMailAwaitingRederivation>> GetEmailsToRederiveAsync(
            StoredMailScope scope,
            StoredEmailId? resumeAfter,
            int batchSize,
            CancellationToken cancellationToken)
        {
            this.CandidateScopes.Add(scope);

            IReadOnlyList<StoredMailAwaitingRederivation> batch =
            [
                .. mail
                    .Where(email => resumeAfter is not { } position || email.StoredEmailId.Value > position.Value)
                    .Take(batchSize),
            ];

            if (++this.servedBatchCount >= stopAfterBatches)
            {
                attempt?.Cancel();
            }

            return Task.FromResult(batch);
        }

        public Task ApplyRederivedMetadataAsync(
            IPersistenceSession session,
            StoredEmailId storedEmailId,
            ExtractedEmailMetadata metadata,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveResumePositionAsync(
            IPersistenceSession session,
            StoredMailScope scope,
            StoredEmailId position,
            CancellationToken cancellationToken)
        {
            this.positions[scope] = position;

            return Task.CompletedTask;
        }

        public Task ClearResumePositionAsync(
            IPersistenceSession session,
            StoredMailScope scope,
            CancellationToken cancellationToken)
        {
            this.positions.Remove(scope);

            return Task.CompletedTask;
        }
    }

    /// <summary>Records the segment reports and the passes published beneath each of them.</summary>
    private sealed class RecordingRederivationTelemetry : IStoredMailRederivationTelemetry
    {
        public List<PublishedRun> Runs { get; } = [];

        public IStoredMailRederivationRunScope BeginRun(MailAccountId accountId, MailFolderAlias? folderAlias)
        {
            PublishedRun published = new(accountId, folderAlias);

            this.Runs.Add(published);

            return published;
        }

        /// <summary>One segment's report, which is also the scope the passes beneath it are added to.</summary>
        internal sealed class PublishedRun(MailAccountId accountId, MailFolderAlias? folderAlias)
            : IStoredMailRederivationRunScope
        {
            public MailAccountId AccountId => accountId;

            public MailFolderAlias? FolderAlias => folderAlias;

            public List<StoredMailRederivationPass> Passes { get; } = [];

            public bool ReachedEndOfScope { get; private set; }

            /// <summary>What the segment reported about the queue taking the rest of the walk, or nothing where it handed none on.</summary>
            public bool? HandedOnQueued { get; private set; }

            public IStoredMailRederivationPassScope BeginPass() => new PublishedPass(this);

            void IStoredMailRederivationRunScope.ReachedEndOfScope() => this.ReachedEndOfScope = true;

            void IStoredMailRederivationRunScope.HandedOn(bool queued) => this.HandedOnQueued = queued;

            public void Dispose()
            {
                // Nothing is released: what a test reads is what was recorded, and the recording outlives the scope.
            }

            private sealed class PublishedPass(PublishedRun run) : IStoredMailRederivationPassScope
            {
                public void Completed(StoredMailRederivationPass pass) => run.Passes.Add(pass);

                public void Dispose()
                {
                    // Nothing is released, for the reason the segment's report releases nothing.
                }
            }
        }
    }

    private sealed class CommittingSession : IPersistenceSession
    {
        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken) =>
            Task.FromResult(PersistenceCommitResult.Committed);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
