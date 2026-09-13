// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Coordination;
using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Covers the loop that runs the sweep of unlinked stored files once per interval.</summary>
/// <remarks>
/// What the worker owns is the interval and the isolation, not what a run decides about a file — that is asserted where
/// the sweep lives. A loop that ended on the first failed run would leave every later failed upload in storage for good.
/// </remarks>
public sealed class UnlinkedStoredFileSweepWorkerTests
{
    /// <summary>Guards against a hung worker. No assertion depends on how long a run actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private static readonly StoredFileId Unlinked = StoredFileId.Create(Guid.Parse("0197a3c0-0000-7000-8000-000000000002"));

    [Fact]
    public async Task ExecuteAsync_BeforeTheFirstIntervalElapses_RunsNoSweep()
    {
        // Arrange
        var files = Substitute.For<IStoredFileStore>();
        using var worker = CreateWorker(files, out _, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        await files.DidNotReceive().FindUnmentionedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A run is reported as a count, and nothing that names a file or its owner reaches the log.</summary>
    [Fact]
    public async Task ExecuteAsync_ARunThatRemovedAFile_ReportsTheCountWithoutNamingTheFile()
    {
        // Arrange
        var removed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var files = Substitute.For<IStoredFileStore>();
        files.FindUnmentionedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([new HeldStoredFile(Unlinked, SyntheticMailUser.Deployment)]);
        files.RemoveAsync(Arg.Any<MailUserId>(), Arg.Any<StoredFileId>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                removed.TrySetResult();

                return Task.CompletedTask;
            });
        using var worker = CreateWorker(files, out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, removed.Task);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        var reported = Assert.Single(logger.Messages);
        Assert.Contains("Removed 1 stored files", reported, StringComparison.Ordinal);
        Assert.DoesNotContain(Unlinked.ToString(), reported, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A failed run is reported and the worker stays alive, so a later interval sweeps what this one could not.</summary>
    [Fact]
    public async Task ExecuteAsync_ARunThatFailed_ReportsItAndSweepsAgainOnALaterInterval()
    {
        // Arrange
        var secondRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;
        var files = Substitute.For<IStoredFileStore>();
        files.FindUnmentionedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<HeldStoredFile>>(_ =>
            {
                if (Interlocked.Increment(ref runs) >= 2)
                {
                    secondRun.TrySetResult();
                }

                throw new InvalidOperationException("the database is unavailable");
            });
        using var worker = CreateWorker(files, out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, secondRun.Task);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(worker.ExecuteTask!.IsFaulted);
        Assert.Contains(logger.Messages, message => message.Contains("sweep of stored files no user record links to failed", StringComparison.Ordinal));
    }

    /// <summary>Shutdown is not a failure: a rolling restart would otherwise read as a sweep that broke on every replica.</summary>
    [Fact]
    public async Task ExecuteAsync_TheHostStopsWhileARunIsUnderWay_ReportsNoFailure()
    {
        // Arrange
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var files = Substitute.For<IStoredFileStore>();
        files.FindUnmentionedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => BlockedUntilCancelledAsync(entered, call.ArgAt<CancellationToken>(2)));
        using var worker = CreateWorker(files, out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, entered.Task);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Empty(logger.Messages);
    }

    /// <summary>Moves the clock on until the worker has reached what the test is waiting for.</summary>
    /// <remarks>
    /// A loop rather than one advance, because the wait on the next interval is created after the run before it returns,
    /// and an advance that arrives before that wait exists is simply lost.
    /// </remarks>
    private static async Task AdvanceUntilAsync(FakeTimeProvider timeProvider, Task reached)
    {
        const int advanceAttempts = 200;
        var runObservationWindow = TimeSpan.FromMilliseconds(20);

        for (var attempt = 0; attempt < advanceAttempts && !reached.IsCompleted; attempt++)
        {
            timeProvider.Advance(UnlinkedStoredFileSweepWorker.Interval);

            await Task.WhenAny(reached, Task.Delay(runObservationWindow, TestContext.Current.CancellationToken));
        }

        await reached.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyList<HeldStoredFile>> BlockedUntilCancelledAsync(
        TaskCompletionSource entered,
        CancellationToken cancellationToken)
    {
        entered.TrySetResult();

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new InvalidOperationException("The database answered after all.");
    }

    /// <summary>Composes the worker over the real sweep, substituted files, a record linking nothing, and a lease nothing competes for.</summary>
    private static UnlinkedStoredFileSweepWorker CreateWorker(
        IStoredFileStore files,
        out FakeTimeProvider timeProvider,
        out RecordingLogger<UnlinkedStoredFileSweepWorker> logger)
    {
        logger = new RecordingLogger<UnlinkedStoredFileSweepWorker>();
        timeProvider = new FakeTimeProvider();

        var links = Substitute.For<IUserRecordFileLinks>();
        links.ReadLinkedFilesAsync(Arg.Any<MailUserId>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<StoredFileId>());

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(files);
        services.AddSingleton(links);
        services.AddSingleton<IWorkLeaseRunner>(new GrantingLeaseRunner());
        services.AddSingleton(AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));
        services.AddScoped<UnlinkedStoredFileSweep>();

        var serviceProvider = services.BuildServiceProvider();

        return new UnlinkedStoredFileSweepWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            logger,
            timeProvider);
    }

    /// <summary>Stands in for the lease table on a replica nothing competes with.</summary>
    private sealed class GrantingLeaseRunner : IWorkLeaseRunner
    {
        public async Task<bool> TryRunUnderLeaseAsync(
            WorkScope scope,
            Func<CancellationToken, Task> work,
            CancellationToken cancellationToken)
        {
            await work(cancellationToken);

            return true;
        }
    }
}
