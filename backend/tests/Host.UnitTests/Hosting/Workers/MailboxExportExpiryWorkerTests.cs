// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Coordination;
using MailFathom.Application.Mail.Export;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Exports;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Covers the loop that asks once per interval which export archives have come due.</summary>
/// <remarks>
/// What the worker owns is the interval, the isolation, and what a pass is allowed to say about itself — never which
/// archive goes, which is asserted where the sweep lives. A loop that ended on the first failed pass would leave every
/// expired archive in the bucket for good, which is a mailbox this deployment keeps a second copy of indefinitely.
/// </remarks>
public sealed class MailboxExportExpiryWorkerTests
{
    /// <summary>Guards against a hung worker. No assertion depends on how long a pass actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private static readonly MailboxExportId Due =
        MailboxExportId.Create(new Guid("0199a0c0-0000-7000-8000-000000000004"));

    [Fact]
    public async Task ExecuteAsync_BeforeTheFirstIntervalElapses_SweepsNothing()
    {
        // Arrange
        var exports = Substitute.For<IMailboxExportStore>();
        using var worker = CreateWorker(exports, Substitute.For<IMailboxExportArchiveStore>(), out _, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        await exports.DidNotReceive()
            .FindDueForExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A pass is reported as a count, because an account, an export, or an object key in a log is the thing the archive was written from.</summary>
    [Fact]
    public async Task ExecuteAsync_APassThatDeletedAnArchive_ReportsTheCountWithoutNamingTheExport()
    {
        // Arrange
        var deleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exports = Substitute.For<IMailboxExportStore>();
        exports.FindDueForExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => [ExpiredArchive()]);
        exports.SaveAsync(Arg.Any<MailboxExport>(), Arg.Any<MailboxExportState>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var archives = Substitute.For<IMailboxExportArchiveStore>();
        archives.DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                deleted.TrySetResult();

                return Task.CompletedTask;
            });

        using var worker = CreateWorker(exports, archives, out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, deleted.Task);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        var reported = Assert.Single(logger.Messages);
        Assert.Contains("Deleted 1 mailbox export archives", reported, StringComparison.Ordinal);
        Assert.DoesNotContain(Due.Value.ToString(), reported, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mailbox-exports/", reported, StringComparison.Ordinal);
    }

    /// <summary>A pass that deleted nothing says nothing, so an idle deployment does not write a line every interval for years.</summary>
    [Fact]
    public async Task ExecuteAsync_APassWithNothingDue_ReportsNothing()
    {
        // Arrange
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exports = Substitute.For<IMailboxExportStore>();
        exports.FindDueForExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                asked.TrySetResult();

                return [];
            });

        using var worker = CreateWorker(exports, Substitute.For<IMailboxExportArchiveStore>(), out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, asked.Task);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Empty(logger.Messages);
    }

    /// <summary>A failed pass is reported and the worker stays alive, so a later interval deletes what this one could not.</summary>
    [Fact]
    public async Task ExecuteAsync_APassThatFailed_ReportsItAndSweepsAgainOnALaterInterval()
    {
        // Arrange
        var secondPass = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var passes = 0;
        var exports = Substitute.For<IMailboxExportStore>();
        exports.FindDueForExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<MailboxExport>>(_ =>
            {
                if (Interlocked.Increment(ref passes) >= 2)
                {
                    secondPass.TrySetResult();
                }

                throw new InvalidOperationException("the database is unavailable");
            });

        using var worker = CreateWorker(exports, Substitute.For<IMailboxExportArchiveStore>(), out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, secondPass.Task);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(worker.ExecuteTask!.IsFaulted);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("due for expiry failed", StringComparison.Ordinal));
    }

    /// <summary>Shutdown is not a failure: a rolling restart would otherwise read as a sweep that broke on every replica.</summary>
    [Fact]
    public async Task ExecuteAsync_TheHostStopsWhileAPassIsUnderWay_ReportsNoFailure()
    {
        // Arrange
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exports = Substitute.For<IMailboxExportStore>();
        exports.FindDueForExpiryAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => BlockedUntilCancelledAsync(entered, call.ArgAt<CancellationToken>(2)));

        using var worker = CreateWorker(exports, Substitute.For<IMailboxExportArchiveStore>(), out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, entered.Task);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Empty(logger.Messages);
    }

    private static MailboxExport ExpiredArchive() => new(
        Due,
        MailAccountId.Create("work"),
        FolderPath: null,
        MailboxExportState.Completed,
        RequestedAt: new DateTimeOffset(2026, 9, 13, 8, 0, 0, TimeSpan.Zero),
        MessageCount: 4,
        ByteCount: 40960,
        ArchiveByteLength: 20480,
        ObjectLocator: "mailbox-exports/expired",
        CompletedAt: new DateTimeOffset(2026, 9, 13, 8, 5, 0, TimeSpan.Zero),
        ExpiresAt: new DateTimeOffset(2026, 9, 15, 8, 5, 0, TimeSpan.Zero),
        FailureCode: null);

    /// <summary>Moves the clock on until the worker has reached what the test is waiting for.</summary>
    /// <remarks>
    /// A loop rather than one advance, because the wait on the next interval is created after the pass before it
    /// returns, and an advance that arrives before that wait exists is simply lost.
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

    private static async Task<IReadOnlyList<MailboxExport>> BlockedUntilCancelledAsync(
        TaskCompletionSource entered,
        CancellationToken cancellationToken)
    {
        entered.TrySetResult();

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new InvalidOperationException("The database answered after all.");
    }

    /// <summary>Composes the worker over the real sweep, substituted storage, and a lease nothing competes for.</summary>
    private static MailboxExportExpiryWorker CreateWorker(
        IMailboxExportStore exports,
        IMailboxExportArchiveStore archives,
        out FakeTimeProvider timeProvider,
        out RecordingLogger<MailboxExportExpiryWorker> logger)
    {
        logger = new RecordingLogger<MailboxExportExpiryWorker>();
        timeProvider = new FakeTimeProvider();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(exports);
        services.AddSingleton(archives);
        services.AddSingleton(Substitute.For<IMailboxExportAuditor>());
        services.AddSingleton<IWorkLeaseRunner>(new GrantingLeaseRunner());
        services.AddSingleton(AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));
        services.AddScoped<MailboxExportExpirySweep>();

        var serviceProvider = services.BuildServiceProvider();

        return new MailboxExportExpiryWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            Interval,
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
