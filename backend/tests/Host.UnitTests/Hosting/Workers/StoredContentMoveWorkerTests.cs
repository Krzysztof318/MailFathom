// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Observability;
using MailFathom.Application.Persistence;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Covers the loop that carries the move one bounded pass per interval.</summary>
/// <remarks>
/// What the worker owns is the interval and the isolation, not what a pass makes of the payloads it finds — that is
/// asserted where the pass lives. A failed pass says nothing about whether payloads remain, and everything an earlier
/// pass repointed is durable on its own, so a loop that gave up on the first failure is the defect this exists to catch:
/// it would leave a deployment half moved with nothing but one warning to say so.
/// </remarks>
public sealed class StoredContentMoveWorkerTests
{
    /// <summary>Guards against a hung worker. No assertion depends on how long a pass actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

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

    /// <summary>A pass does not end the worker, because a move outlives every interval it takes to carry.</summary>
    [Fact]
    public async Task ExecuteAsync_AfterAPassHasBeenCarried_CarriesAFurtherOneOnALaterInterval()
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

    /// <summary>A deployment nobody asked for a move is a single-row read per interval and nothing in the log.</summary>
    [Fact]
    public async Task ExecuteAsync_NoMoveToCarry_ReportsNothing()
    {
        // Arrange
        var passes = new CountedPasses(expected: 1);
        var runStore = RunStoreRecording(passes);
        using var worker = CreateWorker(runStore, out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, passes.Reached(1));
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Empty(logger.Messages);
    }

    /// <summary>A failed pass is not a failed move, so the worker stays alive and the next interval resumes it.</summary>
    [Fact]
    public async Task ExecuteAsync_APassThatFailed_ReportsItWithoutEndingTheWorker()
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

    /// <summary>A competing writer winning a race is a deferral, because the committed position is what the next pass resumes from.</summary>
    [Fact]
    public async Task ExecuteAsync_APassThatLostARace_ReportsADeferralRatherThanAFailure()
    {
        // Arrange
        var passes = new CountedPasses(expected: 1);
        var runStore = RunStoreRecording(
            passes,
            _ => throw new PersistenceConcurrencyConflictException("A competing writer won the race."));
        using var worker = CreateWorker(runStore, out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, passes.Reached(1));
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(
            logger.Messages,
            message => message.Contains("concurrency conflict", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("pass failed", StringComparison.Ordinal));
    }

    /// <summary>Shutdown is not a failure: a rolling restart would otherwise read as a move that broke on every replica.</summary>
    [Fact]
    public async Task ExecuteAsync_APassTheHostStopped_ReportsNoFailure()
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

    /// <summary>Moves the clock on until the worker has reached what the test is waiting for.</summary>
    /// <remarks>
    /// A loop rather than one advance, because the wait on the next tick is created after the pass before it returns:
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

    /// <summary>A pass that reaches the host's shutdown instead of finishing, which cancels rather than fails.</summary>
    private static Task<StoredContentMoveRun?> BlockedUntilStopped(CancellationToken stoppingToken)
    {
        var blocked = new TaskCompletionSource<StoredContentMoveRun?>(TaskCreationOptions.RunContinuationsAsynchronously);

        stoppingToken.Register(() => blocked.TrySetCanceled(stoppingToken));

        return blocked.Task;
    }

    /// <summary>
    /// Answers the one read every pass begins with, counting the passes and then doing whatever the test arranged.
    /// </summary>
    /// <param name="passes">Records that a pass reached the store, which is what a test waits on rather than a clock.</param>
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

    private static StoredContentMoveWorker CreateWorker(
        IStoredContentMoveRunStore runStore,
        out FakeTimeProvider timeProvider,
        out RecordingLogger<StoredContentMoveWorker> logger)
    {
        logger = new RecordingLogger<StoredContentMoveWorker>();
        timeProvider = new FakeTimeProvider();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(runStore);
        services.AddSingleton(Substitute.For<IStoredContentMoveStore>());
        services.AddSingleton(Substitute.For<IEmailContentObjectBackend>());
        services.AddSingleton(Substitute.For<IStoredContentMoveTelemetry>());
        services.AddSingleton(new RawMimeMemoryBudget(long.MaxValue));
        services.AddSingleton(new StoredContentMoveOptions());
        services.AddSingleton(Substitute.For<IPersistenceSessionFactory>());
        services.AddSingleton(new PersistenceConcurrencyOptions());
        services.AddSingleton(AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
        services.AddScoped<StoredContentMove>();

        var serviceProvider = services.BuildServiceProvider();

        return new StoredContentMoveWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new ContentMoveOptions { Interval = Interval }),
            logger,
            timeProvider);
    }

    /// <summary>Counts the passes a worker carried, and hands a test something to wait on rather than a delay.</summary>
    /// <remarks>
    /// A tick and the pass it causes are two things: advancing the clock returns before the pass has run, and a periodic
    /// timer coalesces a second tick nobody was waiting for into the first. So each interval is advanced only once the
    /// pass before it has arrived here, which is what makes the count an assertion rather than a race.
    /// </remarks>
    private sealed class CountedPasses(int expected)
    {
        private readonly TaskCompletionSource[] arrivals =
        [
            .. Enumerable.Range(0, expected).Select(_ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)),
        ];

        private int carried;

        /// <summary>Completes once the numbered pass has reached the store.</summary>
        /// <param name="passNumber">Which pass to wait for, counted from one.</param>
        internal Task Reached(int passNumber) => this.arrivals[passNumber - 1].Task;

        /// <summary>Records that a pass reached the store.</summary>
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
