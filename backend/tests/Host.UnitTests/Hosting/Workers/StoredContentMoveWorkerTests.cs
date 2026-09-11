// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Coordination;
using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Observability;
using MailFathom.Application.Persistence;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Covers the loop that carries the move one bounded pass per interval, on the replica holding it.</summary>
/// <remarks>
/// What the worker owns is the interval, the isolation, and the move's lease, not what a pass makes of the payloads it
/// finds — that is asserted where the pass lives. A failed pass says nothing about whether payloads remain, and
/// everything an earlier pass repointed is durable on its own, so a loop that gave up on the first failure is the defect
/// this exists to catch: it would leave a deployment half moved with nothing but one warning to say so.
/// </remarks>
public sealed class StoredContentMoveWorkerTests
{
    /// <summary>Guards against a hung worker. No assertion depends on how long a pass actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private static readonly StoredContentMoveRun RunningMove = new()
    {
        RequestedAt = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero),
        State = StoredContentMoveState.Running,
        Kind = EmailContentKind.IncomingMessage,
    };

    /// <summary>Nothing is carried until an interval elapses, which is what keeps the move out of a busy first minute.</summary>
    [Fact]
    public async Task ExecuteAsync_BeforeTheFirstIntervalElapses_CarriesNoPass()
    {
        // Arrange
        var runStore = Substitute.For<IStoredContentMoveRunStore>();
        using var worker = CreateWorker(runStore, out _, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        await runStore.DidNotReceiveWithAnyArgs().FindAsync(CancellationToken.None);
    }

    /// <summary>The worker outlives every interval, because a move outlives every interval it takes to carry.</summary>
    [Fact]
    public async Task ExecuteAsync_AfterAnIntervalHasBeenAnswered_AsksAgainOnALaterOne()
    {
        // Arrange
        var passes = new CountedPasses(expected: 2);
        var runStore = RunStoreRecording(passes);
        using var worker = CreateWorker(runStore, out var timeProvider, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, passes.Reached(1));
        await AdvanceUntilAsync(timeProvider, passes.Reached(2));

        // Assert
        Assert.False(worker.ExecuteTask!.IsCompleted);
        await worker.StopAsync(CancellationToken.None);
    }

    /// <summary>A deployment nobody asked for a move pays a single-row read per interval: no lease, and nothing in the log.</summary>
    [Fact]
    public async Task ExecuteAsync_NoMoveToCarry_AsksForNoLeaseAndReportsNothing()
    {
        // Arrange
        var passes = new CountedPasses(expected: 1);
        var runStore = RunStoreRecording(passes);
        var leases = Substitute.For<IWorkLeaseStore>();
        using var worker = CreateWorker(runStore, out var timeProvider, out var logger, leases);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, passes.Reached(1));
        await worker.StopAsync(CancellationToken.None);

        // Assert
        await leases.DidNotReceiveWithAnyArgs().ClaimAsync(default!, default!, default, CancellationToken.None);
        Assert.Empty(logger.Messages);
    }

    /// <summary>A move that could not even be read is reported and asked about again, rather than ending the worker.</summary>
    [Fact]
    public async Task ExecuteAsync_TheMoveCouldNotBeRead_ReportsItWithoutEndingTheWorker()
    {
        // Arrange
        var passes = new CountedPasses(expected: 2);
        var runStore = RunStoreRecording(passes, _ => throw new InvalidOperationException("the database is unavailable"));
        using var worker = CreateWorker(runStore, out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, passes.Reached(1));
        await AdvanceUntilAsync(timeProvider, passes.Reached(2));
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(worker.ExecuteTask!.IsFaulted);
        Assert.Contains(logger.Messages, message => message.Contains("failed", StringComparison.Ordinal));
    }

    /// <summary>A failed pass is not a failed move, so the worker stays alive and a later interval carries the next one.</summary>
    [Fact]
    public async Task ExecuteAsync_APassThatFailed_ReportsItAndCarriesTheNextOne()
    {
        // Arrange
        var passes = new CountedPasses(expected: 2);
        var contentStore = Substitute.For<IStoredContentMoveStore>();
        contentStore
            .GetPayloadsToMoveAsync(default, default, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs<Task<IReadOnlyList<DatabaseBackedPayload>>>(_ =>
            {
                passes.Record();

                throw new InvalidOperationException("the database is unavailable");
            });
        using var worker = CreateWorker(RunStoreAnswering(RunningMove), out var timeProvider, out var logger, contentStore: contentStore);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, passes.Reached(1));
        await AdvanceUntilAsync(timeProvider, passes.Reached(2));
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(worker.ExecuteTask!.IsFaulted);
        Assert.Contains(logger.Messages, message => message.Contains("pass of the stored-content move failed", StringComparison.Ordinal));
    }

    /// <summary>A competing writer winning a race is a deferral, because the committed position is what the next pass resumes from.</summary>
    [Fact]
    public async Task ExecuteAsync_APassThatLostARace_ReportsADeferralRatherThanAFailure()
    {
        // Arrange
        var recordAttempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runStore = RunStoreAnswering(RunningMove);
        runStore
            .SaveAsync(default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(_ =>
            {
                recordAttempted.TrySetResult();

                throw new PersistenceConcurrencyConflictException("A competing writer won the race.");
            });
        using var worker = CreateWorker(runStore, out var timeProvider, out var logger, contentStore: ContentStoreHoldingNothing());

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, recordAttempted.Task);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(
            logger.Messages,
            message => message.Contains("concurrency conflict", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("pass of the stored-content move failed", StringComparison.Ordinal));
    }

    /// <summary>Shutdown is not a failure: a rolling restart would otherwise read as a move that broke on every replica.</summary>
    [Fact]
    public async Task ExecuteAsync_ReadingTheMoveWhenTheHostStops_ReportsNoFailure()
    {
        // Arrange
        var passes = new CountedPasses(expected: 1);
        var runStore = RunStoreRecording(passes, BlockedUntilStopped);
        using var worker = CreateWorker(runStore, out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, passes.Reached(1));
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Empty(logger.Messages);
    }

    /// <summary>A replica whose move another one holds carries nothing of it, and keeps asking on its own interval.</summary>
    [Fact]
    public async Task ExecuteAsync_AnotherReplicaHoldsTheMove_CarriesNoPassAndAsksAgainOnTheInterval()
    {
        // Arrange
        var contentStore = Substitute.For<IStoredContentMoveStore>();
        var leases = new ScriptedWorkLeaseStore { HeldElsewhere = true };
        using var worker = CreateWorker(RunStoreAnswering(RunningMove), out var timeProvider, out _, leases, contentStore);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, leases.WaitForClaimAsync(TestContext.Current.CancellationToken));
        await AdvanceUntilAsync(timeProvider, leases.WaitForClaimAsync(TestContext.Current.CancellationToken));
        await worker.StopAsync(CancellationToken.None);

        // Assert
        await contentStore.DidNotReceiveWithAnyArgs().GetPayloadsToMoveAsync(default, default, default, CancellationToken.None);
    }

    /// <summary>
    /// A replica that can no longer show it holds the move stops the pass it has under way on that renewal, rather than
    /// letting it run on until the lease has expired and another replica may already be carrying a pass of its own.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_HoldLostWhileAPassIsUnderWay_CancelsThePassBeforeTheLeaseCouldExpire()
    {
        // Arrange
        var passEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var passCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var settings = new ContentMoveOptions
        {
            Interval = Interval,
            LeaseDuration = TimeSpan.FromMinutes(2),
            LeaseRenewalInterval = TimeSpan.FromSeconds(30),
        };
        var leases = new ScriptedWorkLeaseStore();
        using var worker = CreateWorker(
            RunStoreAnswering(RunningMove),
            out var timeProvider,
            out _,
            leases,
            ContentStoreNeverAnswering(passEntered, passCancelled),
            settings);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, passEntered.Task);
        var enteredAt = timeProvider.GetUtcNow();
        leases.HeldElsewhere = true;
        timeProvider.Advance(settings.LeaseRenewalInterval);
        await passCancelled.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(timeProvider.GetUtcNow() - enteredAt < settings.LeaseDuration);
        await worker.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// The holder keeps the move through the interval after its pass, which is what stops a second replica starting the
    /// next pass sooner and so keeps the rate the deployment's whatever the replica count is.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_InTheIntervalAfterAPass_RefusesTheMoveToAnotherReplica()
    {
        // Arrange
        var passRecorded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runStore = RunStoreAnswering(RunningMove);
        runStore
            .SaveAsync(default!, default!, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(_ =>
            {
                passRecorded.TrySetResult();

                return Task.CompletedTask;
            });
        var leases = new ScriptedWorkLeaseStore();
        using var worker = CreateWorker(runStore, out var timeProvider, out _, leases, ContentStoreHoldingNothing());

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, passRecorded.Task);
        var secondReplicaClaim = await leases.ClaimAsync(
            StoredContentMoveWorker.MoveScope,
            WorkLeaseHolder.NewHold(),
            TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(secondReplicaClaim);
        await worker.StopAsync(CancellationToken.None);
    }

    /// <summary>A replica that stops gracefully gives the move back, so another replica need not wait out the lease.</summary>
    [Fact]
    public async Task ExecuteAsync_HostStopsWhileAPassIsUnderWay_ReleasesTheMove()
    {
        // Arrange
        var passEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var passCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leases = new ScriptedWorkLeaseStore();
        using var worker = CreateWorker(
            RunStoreAnswering(RunningMove),
            out var timeProvider,
            out _,
            leases,
            ContentStoreNeverAnswering(passEntered, passCancelled));

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, passEntered.Task);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal([StoredContentMoveWorker.MoveScope], leases.Releases);
        Assert.Empty(leases.HeldScopes);
    }

    /// <summary>Moves the clock on until the worker has reached what the test is waiting for.</summary>
    /// <remarks>
    /// A loop rather than one advance, because the wait on the next interval is created after the pass before it returns:
    /// an advance that arrives before that wait exists is simply lost, and the next one fires it. What the loop costs is
    /// that a test cannot count the passes an elapsed interval produced, which is why none of them does.
    /// </remarks>
    private static async Task AdvanceUntilAsync(FakeTimeProvider timeProvider, Task reached)
    {
        const int advanceAttempts = 200;
        var passObservationWindow = TimeSpan.FromMilliseconds(20);

        for (var attempt = 0; attempt < advanceAttempts && !reached.IsCompleted; attempt++)
        {
            timeProvider.Advance(Interval);

            await Task.WhenAny(reached, Task.Delay(passObservationWindow, TestContext.Current.CancellationToken));
        }

        await reached.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
    }

    /// <summary>A read that reaches the host's shutdown instead of finishing, which cancels rather than fails.</summary>
    private static Task<StoredContentMoveRun?> BlockedUntilStopped(CancellationToken stoppingToken)
    {
        var blocked = new TaskCompletionSource<StoredContentMoveRun?>(TaskCreationOptions.RunContinuationsAsynchronously);

        stoppingToken.Register(() => blocked.TrySetCanceled(stoppingToken));

        return blocked.Task;
    }

    /// <summary>
    /// Answers the one read every interval begins with, counting the reads and then doing whatever the test arranged.
    /// </summary>
    /// <param name="passes">Records that an interval reached the store, which is what a test waits on rather than a clock.</param>
    /// <param name="answer">What the read does, defaulting to the deployment that has never been asked for a move.</param>
    private static IStoredContentMoveRunStore RunStoreRecording(
        CountedPasses passes,
        Func<CancellationToken, Task<StoredContentMoveRun?>>? answer = null)
    {
        var runStore = Substitute.For<IStoredContentMoveRunStore>();

        runStore.FindAsync(Arg.Any<CancellationToken>()).Returns(call =>
        {
            passes.Record();

            return answer is null
                ? Task.FromResult<StoredContentMoveRun?>(null)
                : answer(call.Arg<CancellationToken>());
        });

        return runStore;
    }

    /// <summary>Answers every read of the move with the same run, which is what a pass and the read before it both see.</summary>
    private static IStoredContentMoveRunStore RunStoreAnswering(StoredContentMoveRun run)
    {
        var runStore = Substitute.For<IStoredContentMoveRunStore>();

        runStore.FindAsync(Arg.Any<CancellationToken>()).Returns(run);

        return runStore;
    }

    /// <summary>Names no payload of any kind, so a pass walks to the end of the content and records it at once.</summary>
    private static IStoredContentMoveStore ContentStoreHoldingNothing()
    {
        var contentStore = Substitute.For<IStoredContentMoveStore>();

        contentStore
            .GetPayloadsToMoveAsync(default, default, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<DatabaseBackedPayload>>([]));

        return contentStore;
    }

    /// <summary>Models a database that never answers the pass's first read, and says when the pass gave up on it.</summary>
    private static IStoredContentMoveStore ContentStoreNeverAnswering(TaskCompletionSource entered, TaskCompletionSource cancelled)
    {
        var contentStore = Substitute.For<IStoredContentMoveStore>();

        contentStore
            .GetPayloadsToMoveAsync(default, default, default, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(call => NeverAnswerUntilCancelledAsync(entered, cancelled, call.ArgAt<CancellationToken>(3)));

        return contentStore;
    }

    private static async Task<IReadOnlyList<DatabaseBackedPayload>> NeverAnswerUntilCancelledAsync(
        TaskCompletionSource entered,
        TaskCompletionSource cancelled,
        CancellationToken cancellationToken)
    {
        entered.TrySetResult();

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            cancelled.TrySetResult();

            throw;
        }

        throw new InvalidOperationException("The database answered after all.");
    }

    /// <summary>Composes the worker over substitutes, holding the move through a lease table that grants it unless a test says otherwise.</summary>
    /// <remarks>
    /// The lease settings sit at the top of their ranges unless a test states its own, because a hold reads a clock that
    /// jumped past its renewal window as a process that was paused and stops its pass — which is right, and is not what a
    /// test stepping its clock to reach an interval means to model.
    /// </remarks>
    private static StoredContentMoveWorker CreateWorker(
        IStoredContentMoveRunStore runStore,
        out FakeTimeProvider timeProvider,
        out RecordingLogger<StoredContentMoveWorker> logger,
        IWorkLeaseStore? leases = null,
        IStoredContentMoveStore? contentStore = null,
        ContentMoveOptions? settings = null)
    {
        logger = new RecordingLogger<StoredContentMoveWorker>();
        timeProvider = new FakeTimeProvider();

        var session = Substitute.For<IPersistenceSession>();
        session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(session);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(runStore);
        services.AddSingleton(contentStore ?? Substitute.For<IStoredContentMoveStore>());
        services.AddSingleton(Substitute.For<IEmailContentObjectBackend>());
        services.AddSingleton(Substitute.For<IStoredContentMoveTelemetry>());
        services.AddSingleton(new RawMimeMemoryBudget(long.MaxValue));
        services.AddSingleton(new StoredContentMoveOptions());
        services.AddSingleton(sessionFactory);
        services.AddSingleton(new PersistenceConcurrencyOptions());
        services.AddSingleton(AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));
        services.AddSingleton(leases ?? new ScriptedWorkLeaseStore());
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
        services.AddScoped<StoredContentMove>();

        var serviceProvider = services.BuildServiceProvider();

        return new StoredContentMoveWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(settings ?? new ContentMoveOptions
            {
                Interval = Interval,
                LeaseDuration = TimeSpan.FromHours(1),
                LeaseRenewalInterval = TimeSpan.FromMinutes(30),
            }),
            logger,
            NullLoggerFactory.Instance,
            timeProvider);
    }

    /// <summary>Counts the intervals a worker answered, and hands a test something to wait on rather than a delay.</summary>
    /// <remarks>
    /// An elapsed interval and the read it causes are two things: advancing the clock returns before the read has run.
    /// So each interval is advanced only once the read before it has arrived here, which is what makes the count an
    /// assertion rather than a race.
    /// </remarks>
    private sealed class CountedPasses(int expected)
    {
        private readonly TaskCompletionSource[] arrivals =
        [
            .. Enumerable.Range(0, expected).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)),
        ];

        private int carried;

        /// <summary>Completes once the numbered interval has reached the store.</summary>
        /// <param name="passNumber">Which interval to wait for, counted from one.</param>
        internal Task Reached(int passNumber) => this.arrivals[passNumber - 1].Task;

        /// <summary>Records that an interval reached the store.</summary>
        internal void Record()
        {
            var reached = Interlocked.Increment(ref this.carried);

            if (reached <= this.arrivals.Length)
            {
                this.arrivals[reached - 1].TrySetResult();
            }
        }
    }
}
