// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Streaming;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Covers the loop that forgets the Discover runs nobody can come back for.</summary>
/// <remarks>
/// What the worker owns is the interval and the isolation; which run is due is the store's and is asserted where that
/// decision lives. The isolation is the part worth a test of its own here: this is a storage-limitation obligation over
/// rows composed from somebody's correspondence, so a loop that ended on the first failed pass would leave them in the
/// database until the process was restarted.
/// </remarks>
public sealed class DiscoveryRunRetentionWorkerTests
{
    /// <summary>A run is held for its whole window, so a worker that swept on startup would forget one a client still holds.</summary>
    [Fact]
    public async Task ExecuteAsync_BeforeTheFirstIntervalElapses_ForgetsNothing()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var timeProvider = new FakeTimeProvider();
        await EndedRunAsync(store, timeProvider);
        using var worker = WorkerOver(store, timeProvider, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Equal(1, store.HeldCount);
    }

    /// <summary>A pass is reported as a count, and nothing naming a run or what it composed reaches the log.</summary>
    [Fact]
    public async Task ExecuteAsync_APassThatForgotARun_ReportsTheCountWithoutNamingTheRun()
    {
        // Arrange
        var store = new InMemoryDiscoveryRunStore();
        var timeProvider = new FakeTimeProvider();
        var id = await EndedRunAsync(store, timeProvider);
        using var worker = WorkerOver(store, timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, () => store.HeldCount == 0);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        var reported = Assert.Single(logger.Messages);
        Assert.Contains("Forgot 1 Discover runs", reported, StringComparison.Ordinal);
        Assert.DoesNotContain(id.Value.ToString(), reported, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A failed pass is reported and the worker stays alive, so a later interval forgets what this one could not.</summary>
    [Fact]
    public async Task ExecuteAsync_APassThatFailed_ReportsItAndAsksAgainOnALaterInterval()
    {
        // Arrange
        var passes = 0;
        var store = Substitute.For<IDiscoveryRunStore>();
        store.RemoveForgottenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ =>
            {
                Interlocked.Increment(ref passes);

                throw new InvalidOperationException("the database is unavailable");
            });

        var timeProvider = new FakeTimeProvider();
        using var worker = WorkerOver(store, timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, () => Volatile.Read(ref passes) >= 2);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(worker.ExecuteTask!.IsFaulted);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("due to be forgotten failed", StringComparison.Ordinal));
    }

    /// <summary>Shutdown is not a failure: a rolling restart would otherwise read as a pass that broke on every replica.</summary>
    [Fact]
    public async Task ExecuteAsync_TheHostStopsWhileAPassIsUnderWay_ReportsNoFailure()
    {
        // Arrange
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var store = Substitute.For<IDiscoveryRunStore>();
        store.RemoveForgottenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(call => BlockedUntilCancelledAsync(entered, call.ArgAt<CancellationToken>(1)));

        var timeProvider = new FakeTimeProvider();
        using var worker = WorkerOver(store, timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, () => entered.Task.IsCompleted);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Empty(logger.Messages);
    }

    /// <summary>Moves the clock on until the worker has reached what the test is waiting for.</summary>
    /// <remarks>
    /// A loop rather than one advance, because the wait on the next interval is created after the pass before it
    /// returns, and an advance that arrives before that wait exists is simply lost. The attempt count is what bounds a
    /// worker that never gets there, so a hang is reported as a failed assertion rather than as a suite that stopped.
    /// </remarks>
    private static async Task AdvanceUntilAsync(FakeTimeProvider timeProvider, Func<bool> reached)
    {
        const int advanceAttempts = 200;
        var passObservationWindow = TimeSpan.FromMilliseconds(20);

        for (var attempt = 0; attempt < advanceAttempts && !reached(); attempt++)
        {
            timeProvider.Advance(DiscoveryRunRetentionWorker.Interval);

            await Task.Delay(passObservationWindow, TestContext.Current.CancellationToken);
        }

        Assert.True(reached(), "The worker never reached what the test was waiting for.");
    }

    private static async Task<int> BlockedUntilCancelledAsync(
        TaskCompletionSource entered,
        CancellationToken cancellationToken)
    {
        entered.TrySetResult();

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new InvalidOperationException("The database answered after all.");
    }

    private static async Task<DiscoveryRunId> EndedRunAsync(
        InMemoryDiscoveryRunStore store,
        FakeTimeProvider timeProvider)
    {
        var id = DiscoveryRunId.New();
        var now = timeProvider.GetUtcNow();

        await store.TryOpenAsync(id, SyntheticUser.Deployment, now, TestContext.Current.CancellationToken);
        await store.AppendAsync(
            id,
            new DiscoveryRunCompleted([], [], MailAnsweringRunSpend.Nothing),
            now,
            TestContext.Current.CancellationToken);

        return id;
    }

    /// <summary>Composes the worker over a container holding the store, exactly as the host resolves one per pass.</summary>
    private static DiscoveryRunRetentionWorker WorkerOver(
        IDiscoveryRunStore store,
        FakeTimeProvider timeProvider,
        out RecordingLogger<DiscoveryRunRetentionWorker> logger)
    {
        logger = new RecordingLogger<DiscoveryRunRetentionWorker>();

        var services = new ServiceCollection();
        services.AddSingleton(store);

        var serviceProvider = services.BuildServiceProvider();

        return new DiscoveryRunRetentionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            logger,
            timeProvider);
    }
}
