// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Covers mode selection, degradation, recycling, and the wait a notification cuts short.</summary>
public sealed class AccountPushNotificationWatchTests
{
    /// <summary>Guards against a wait that never ends. No assertion depends on how long one actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private static readonly MailAccountId PrimaryAccount = MailAccountId.Create("primary");

    private static readonly MailFolderResolution InboxBinding = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("INBOX"),
        RemoteFolderPath.Create("INBOX", '/'));

    private static readonly MailFolderResolution ArchiveBinding = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("archive"),
        RemoteFolderPath.Create("Archive", '/'));

    /// <summary>An operator who asked for push and got it needs the confirmation as much as the contradiction.</summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_ServerAdvertisesPush_WatchesTheFolderAndReportsPush()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);

        // Act
        await harness.WatchInboxAsync();

        // Assert
        Assert.Equal(["INBOX"], harness.NotificationSessions.OpenedFolderAliases);
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("Folder primary/INBOX is now synchronized in Push mode", StringComparison.Ordinal));
    }

    /// <summary>The configured mode and the effective one differ here, which is the whole reason the effective one is reported.</summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_ServerAdvertisesNoPush_ReportsPollingAndNamesTheServer()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        harness.NotificationSessions.AdvertisesPush = false;

        // Act
        await harness.WatchInboxAsync();

        // Assert
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("advertises no IDLE capability", StringComparison.Ordinal));
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("Folder primary/INBOX is now synchronized in Polling mode", StringComparison.Ordinal));
    }

    /// <summary>An account that never asked for push must open no connection it did not ask for.</summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_AccountIsConfiguredForPolling_OpensNoSession()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Polling);

        // Act
        await harness.WatchInboxAsync();

        // Assert
        Assert.Empty(harness.NotificationSessions.OpenedFolderAliases);
    }

    /// <summary>The point of push: the account's next pass starts on the server's word rather than on its interval.</summary>
    [Fact]
    public async Task WaitForNextPassAsync_WatchedFolderChanges_ReturnsWithoutTheIntervalElapsing()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        await harness.WatchInboxAsync();
        var session = harness.NotificationSessions.SessionWatching("INBOX")!;

        // Act
        var waiting = harness.Watch.WaitForNextPassAsync(
            harness.Options,
            harness.Options.Interval,
            TestContext.Current.CancellationToken);
        await session.WaitStarted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        session.ReportFolderChange();
        await waiting.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("Mail server reported a change in primary/INBOX", StringComparison.Ordinal));
    }

    /// <summary>A wait nothing reported into still costs exactly the delay the supervisor decided on.</summary>
    [Fact]
    public async Task WaitForNextPassAsync_NothingIsReported_HoldsUntilTheSuppliedDelayElapses()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        await harness.WatchInboxAsync();
        var session = harness.NotificationSessions.SessionWatching("INBOX")!;

        // Act
        var waiting = harness.Watch.WaitForNextPassAsync(
            harness.Options,
            harness.Options.Interval,
            TestContext.Current.CancellationToken);
        await session.WaitStarted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        var endedEarly = waiting.IsCompleted;
        await SynchronizationTestHost.AdvanceUntilAsync(harness.Clock, waiting, harness.Options.Interval, DeadlockGuard);

        // Assert
        Assert.False(endedEarly);
    }

    /// <summary>A delay longer than the renewal bound is served by several commands rather than by one that outlives the server's patience.</summary>
    [Fact]
    public async Task WaitForNextPassAsync_DelayOutlastsTheRenewalInterval_RenewsInsteadOfWaitingInOneCommand()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        harness.Options.PushRenewalInterval = TimeSpan.FromMinutes(20);
        await harness.WatchInboxAsync();
        var session = harness.NotificationSessions.SessionWatching("INBOX")!;

        // Act
        var waiting = harness.Watch.WaitForNextPassAsync(
            harness.Options,
            TimeSpan.FromMinutes(60),
            TestContext.Current.CancellationToken);

        // The clock moves once per wait the session has actually entered. Advancing on a schedule of its own would
        // outrun the session between two commands and collapse the renewals this test exists to observe.
        for (var renewal = 0; renewal < 3; renewal++)
        {
            await session.WaitsEntered.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
            harness.Clock.Advance(harness.Options.PushRenewalInterval);
        }

        await waiting.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(session.WaitCount >= 3, $"A 60-minute delay renewed every 20 minutes should issue at least three waits, not {session.WaitCount}.");
    }

    /// <summary>Repeated failures stop the retrying, and the folder keeps synchronizing by polling while they do.</summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_PushKeepsFailing_DegradesToPollingOnceTheBoundIsSpent()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        harness.Options.MaxConsecutivePushFailures = 3;
        harness.NotificationSessions.OpenFailure = new MailboxUnavailableException(
            PrimaryAccount,
            InboxBinding.Alias,
            new IOException("The server closed the connection."));

        // Act
        await harness.WatchInboxAsync();
        await harness.WatchInboxAsync();
        await harness.WatchInboxAsync();
        await harness.WatchInboxAsync();

        // Assert
        Assert.Equal(3, harness.NotificationSessions.OpenedFolderAliases.Count);
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("failed 3 times in a row, so the folder is synchronized by polling", StringComparison.Ordinal));
    }

    /// <summary>Degradation is temporary: a server that starts serving push again is found without a restart.</summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_DegradationPeriodElapses_AttemptsPushAgain()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        harness.Options.MaxConsecutivePushFailures = 1;
        harness.Options.PushDegradationPeriod = TimeSpan.FromMinutes(15);
        harness.NotificationSessions.OpenFailure = new MailboxUnavailableException(
            PrimaryAccount,
            InboxBinding.Alias,
            new IOException("The server closed the connection."));
        await harness.WatchInboxAsync();

        // Act
        await harness.WatchInboxAsync();
        var attemptsWhileDegraded = harness.NotificationSessions.OpenedFolderAliases.Count;
        harness.NotificationSessions.OpenFailure = null;
        harness.Clock.Advance(harness.Options.PushDegradationPeriod + TimeSpan.FromSeconds(1));
        await harness.WatchInboxAsync();

        // Assert
        Assert.Equal(1, attemptsWhileDegraded);
        Assert.Equal(2, harness.NotificationSessions.OpenedFolderAliases.Count);
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("Folder primary/INBOX is now synchronized in Push mode", StringComparison.Ordinal));
    }

    /// <summary>A long-lived connection is where a rotated credential would otherwise outlive the reload that replaced it.</summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_ConfigurationSnapshotIsSuperseded_RecyclesTheSession()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        await harness.WatchInboxAsync();
        var firstSession = harness.NotificationSessions.SessionWatching("INBOX")!;

        // Act
        harness.PublishReloadedSnapshot();
        await harness.WatchInboxAsync();

        // Assert
        Assert.True(firstSession.IsDisposed);
        Assert.Equal(2, harness.NotificationSessions.OpenedFolderAliases.Count);
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("was recycled because a newly published configuration snapshot", StringComparison.Ordinal));
    }

    /// <summary>The same snapshot is the same session; a run that changed nothing must not reconnect.</summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_SnapshotIsUnchanged_KeepsTheOpenSession()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        await harness.WatchInboxAsync();
        var firstSession = harness.NotificationSessions.SessionWatching("INBOX")!;

        // Act
        await harness.WatchInboxAsync();

        // Assert
        Assert.False(firstSession.IsDisposed);
        Assert.Single(harness.NotificationSessions.OpenedFolderAliases);
    }

    /// <summary>An alias that resolved to nothing this run leaves a connection idling on a folder nothing reads.</summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_FolderNoLongerResolves_ClosesItsSession()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        await harness.WatchInboxAsync();
        var session = harness.NotificationSessions.SessionWatching("INBOX")!;

        // Act
        await harness.WatchAsync([]);

        // Assert
        Assert.True(session.IsDisposed);
    }

    /// <summary>Shutdown releases every connection the account was holding.</summary>
    [Fact]
    public async Task DisposeAsync_SessionsAreOpen_ClosesEveryOneOfThem()
    {
        // Arrange
        var harness = CreateHarness(MailSynchronizationMode.Push);
        await harness.WatchInboxAsync();
        var session = harness.NotificationSessions.SessionWatching("INBOX")!;

        // Act
        await harness.Watch.DisposeAsync();
        harness.Dispose();

        // Assert
        Assert.True(session.IsDisposed);
    }

    /// <summary>
    /// A session that connects and then serves nothing must still reach the bound. Counting a successful connection as
    /// evidence would reset the count on every attempt, and the account would reconnect and fail a wait as fast as the
    /// server could answer, for as long as the process ran.
    /// </summary>
    [Fact]
    public async Task WaitForNextPassAsync_SessionOpensAndEveryWaitFails_StillDegradesToPolling()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        harness.Options.MaxConsecutivePushFailures = 3;

        // Act
        for (var attempt = 0; attempt < harness.Options.MaxConsecutivePushFailures; attempt++)
        {
            await harness.WatchInboxAsync();
            harness.NotificationSessions.SessionWatching("INBOX")!.WaitFailure =
                new IOException("The server closed the connection.");

            await harness.Watch.WaitForNextPassAsync(
                harness.Options,
                harness.Options.Interval,
                TestContext.Current.CancellationToken);
        }

        await harness.WatchInboxAsync();

        // Assert
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("failed 3 times in a row, so the folder is synchronized by polling", StringComparison.Ordinal));

        // The degraded folder is not reconnected on the run that follows, which is what the bound is for.
        Assert.Equal(3, harness.NotificationSessions.OpenedFolderAliases.Count);
    }

    /// <summary>
    /// The bound counts failures since the last thing that worked, so a wait that returned has to clear the ones before
    /// it. A folder that failed its way to the edge of the bound, served one wait, and then failed once more has
    /// produced a single failure since that wait, and degrading it there would degrade a server that recovered.
    /// </summary>
    [Fact]
    public async Task WaitForNextPassAsync_AWaitReturnsBetweenFailures_ClearsTheFailuresCountedBeforeIt()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        harness.Options.MaxConsecutivePushFailures = 3;

        for (var attempt = 0; attempt < harness.Options.MaxConsecutivePushFailures - 1; attempt++)
        {
            await harness.WatchInboxAsync();
            harness.NotificationSessions.SessionWatching("INBOX")!.WaitFailure =
                new IOException("The server closed the connection.");

            await harness.Watch.WaitForNextPassAsync(
                harness.Options,
                harness.Options.Interval,
                TestContext.Current.CancellationToken);
        }

        // Act
        await harness.WatchInboxAsync();
        var servingSession = harness.NotificationSessions.SessionWatching("INBOX")!;
        var servedWait = harness.Watch.WaitForNextPassAsync(
            harness.Options,
            harness.Options.Interval,
            TestContext.Current.CancellationToken);
        await SynchronizationTestHost.AdvanceUntilAsync(
            harness.Clock,
            servedWait,
            harness.Options.Interval,
            DeadlockGuard);

        servingSession.WaitFailure = new IOException("The server closed the connection.");
        await harness.Watch.WaitForNextPassAsync(
            harness.Options,
            harness.Options.Interval,
            TestContext.Current.CancellationToken);
        await harness.WatchInboxAsync();

        // Assert
        Assert.DoesNotContain(
            harness.Logger.Messages,
            message => message.Contains("so the folder is synchronized by polling", StringComparison.Ordinal));

        // The fourth connection is the evidence: a folder that had reached the bound would not be reconnected at all.
        Assert.Equal(4, harness.NotificationSessions.OpenedFolderAliases.Count);
    }

    /// <summary>
    /// A decline says the server serves no subscription, and a later attempt that failed for another reason says
    /// nothing about the capability at all. Leaving the older answer standing would send the account down the
    /// per-folder fallback that belongs to a decline, on the strength of an attempt that never got an answer.
    /// </summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_SubscriptionFailsAfterAnEarlierDecline_DoesNotFallBackPerFolder()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        await harness.WatchAsync([InboxBinding, ArchiveBinding]);
        var inboxSession = harness.NotificationSessions.SessionWatching("INBOX")!;

        harness.Clock.Advance(harness.Options.PushDegradationPeriod + TimeSpan.FromMinutes(1));
        harness.NotificationSessions.SubscriptionOpenFailure = new IOException("The server closed the connection.");

        // Act
        await harness.WatchAsync([InboxBinding, ArchiveBinding]);

        // Assert
        Assert.True(
            inboxSession.IsDisposed,
            "A subscription that failed leaves the account on its interval, so the per-folder sessions a decline had opened are released.");
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("Folder primary/INBOX is now synchronized in Polling mode", StringComparison.Ordinal));
    }

    /// <summary>
    /// One connection covering every folder is the whole point of asking for a subscription, so a server that serves
    /// one must leave the account holding no per-folder session at all.
    /// </summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_ServerServesASubscription_WatchesEveryFolderOverOneConnection()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        harness.NotificationSessions.AdvertisesSubscription = true;

        // Act
        await harness.WatchAsync([InboxBinding, ArchiveBinding]);

        // Assert
        Assert.Equal(["INBOX", "ARCHIVE"], harness.NotificationSessions.Subscription!.WatchedFolderAliases);
        Assert.Empty(harness.NotificationSessions.OpenedFolderAliases);
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("watches 2 folders through one push subscription", StringComparison.Ordinal));
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("Folder primary/ARCHIVE is now synchronized in Push mode", StringComparison.Ordinal));
    }

    /// <summary>
    /// A server is entitled to refuse a subscription naming more mailboxes than it will track, so the list is bounded
    /// and the folders past the bound are synchronized on the account's interval rather than dropped.
    /// </summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_MoreFoldersThanTheSubscriptionMayName_LeavesTheRestOnTheInterval()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        harness.NotificationSessions.AdvertisesSubscription = true;
        harness.Options.MaxSubscribedFolders = 1;

        // Act
        await harness.WatchAsync([InboxBinding, ArchiveBinding]);

        // Assert
        Assert.Equal(["INBOX"], harness.NotificationSessions.Subscription!.WatchedFolderAliases);
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("Folder primary/ARCHIVE is now synchronized in Polling mode", StringComparison.Ordinal));

        // The overflow folder is polled rather than given a connection of its own, which is what the bound is for.
        Assert.Empty(harness.NotificationSessions.OpenedFolderAliases);
    }

    /// <summary>A server without the capability leaves the account exactly where it was before subscriptions existed.</summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_ServerServesNoSubscription_WatchesEachFolderOverItsOwnConnection()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);

        // Act
        await harness.WatchAsync([InboxBinding, ArchiveBinding]);

        // Assert
        Assert.Equal(["INBOX", "ARCHIVE"], harness.NotificationSessions.OpenedFolderAliases);
        Assert.Null(harness.NotificationSessions.Subscription);
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("advertises no NOTIFY capability", StringComparison.Ordinal));
    }

    /// <summary>Reading a capability costs a connection, so a server that has declined once is not asked again every run.</summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_SubscriptionAlreadyDeclined_DoesNotAskAgainUntilTheRetryPeriodPasses()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        await harness.WatchInboxAsync();

        // Act
        await harness.WatchInboxAsync();
        harness.Clock.Advance(harness.Options.PushDegradationPeriod);
        await harness.WatchInboxAsync();

        // Assert
        Assert.Equal(2, harness.NotificationSessions.SubscriptionAttempts.Count);
    }

    /// <summary>The account's next pass starts on the server's word, and the line names the folder the server reported.</summary>
    [Fact]
    public async Task WaitForNextPassAsync_SubscriptionReportsAFolder_ReturnsAndNamesThatFolder()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        harness.NotificationSessions.AdvertisesSubscription = true;
        await harness.WatchAsync([InboxBinding, ArchiveBinding]);
        var subscription = harness.NotificationSessions.Subscription!;

        // Act
        var waiting = harness.Watch.WaitForNextPassAsync(
            harness.Options,
            harness.Options.Interval,
            TestContext.Current.CancellationToken);
        await subscription.WaitStarted.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        subscription.ReportFolderChange(ArchiveBinding.Alias);
        await waiting.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("Mail server reported a change in primary/ARCHIVE", StringComparison.Ordinal));
    }

    /// <summary>
    /// A subscription that keeps failing leaves the whole account on its interval rather than being answered with one
    /// connection per folder at a server that has just refused one.
    /// </summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_SubscriptionKeepsFailing_LeavesTheAccountPolledWithoutOpeningPerFolderSessions()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        harness.NotificationSessions.AdvertisesSubscription = true;
        harness.NotificationSessions.SubscriptionOpenFailure = new InvalidOperationException("The server refused the connection.");

        // Act
        for (var attempt = 0; attempt < harness.Options.MaxConsecutivePushFailures; attempt++)
        {
            await harness.WatchAsync([InboxBinding, ArchiveBinding]);
        }

        // Assert
        Assert.Empty(harness.NotificationSessions.OpenedFolderAliases);
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("so its folders are synchronized by polling", StringComparison.Ordinal));
    }

    /// <summary>A long-lived connection is where a rotated credential would otherwise stay in use, subscription or not.</summary>
    [Fact]
    public async Task WatchResolvedFoldersAsync_NewSnapshotPublished_RecyclesTheSubscription()
    {
        // Arrange
        using var harness = CreateHarness(MailSynchronizationMode.Push);
        harness.NotificationSessions.AdvertisesSubscription = true;
        await harness.WatchInboxAsync();
        var subscription = harness.NotificationSessions.Subscription!;
        harness.PublishReloadedSnapshot();

        // Act
        await harness.WatchInboxAsync();

        // Assert
        Assert.True(subscription.IsDisposed);
        Assert.NotSame(subscription, harness.NotificationSessions.Subscription);
        Assert.Contains(
            harness.Logger.Messages,
            message => message.Contains("Push subscription for primary was recycled", StringComparison.Ordinal));
    }

    private static WatchHarness CreateHarness(MailSynchronizationMode mode)
    {
        var options = SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX");
        options.Accounts[0].Mode = mode;

        return new WatchHarness(options);
    }

    /// <summary>Holds the one watch a test drives, together with the container its sessions resolve from.</summary>
    private sealed class WatchHarness : IDisposable
    {
        private readonly ServiceProvider services;
        private readonly StubSettingsSnapshot<MailSynchronizationOptions> settings;

        internal WatchHarness(MailSynchronizationOptions options)
        {
            this.Options = options;
            this.Clock = new FakeTimeProvider();
            this.NotificationSessions = new FakeMailboxNotificationSessionFactory(this.Clock);
            this.settings = new StubSettingsSnapshot<MailSynchronizationOptions>(options);
            this.services = SynchronizationTestHost.BuildServiceProvider(
                options,
                this.settings,
                Substitute.For<IMailboxSessionFactory>(),
                this.Clock,
                this.NotificationSessions);
            this.Logger = new RecordingLogger<AccountPushNotificationWatch>();
            this.Watch = new AccountPushNotificationWatch(
                PrimaryAccount,
                this.services.GetRequiredService<IServiceScopeFactory>(),
                this.Logger,
                this.Clock);
        }

        internal MailSynchronizationOptions Options { get; private set; }

        internal FakeTimeProvider Clock { get; }

        internal FakeMailboxNotificationSessionFactory NotificationSessions { get; }

        internal RecordingLogger<AccountPushNotificationWatch> Logger { get; }

        internal AccountPushNotificationWatch Watch { get; }

        /// <summary>Brings the watch in line with a run that resolved the inbox, which is what a supervisor does after every pass.</summary>
        internal Task WatchInboxAsync() => this.WatchAsync([InboxBinding]);

        internal Task WatchAsync(IReadOnlyList<MailFolderResolution> resolvedFolders) =>
            this.Watch.WatchResolvedFoldersAsync(
                this.Options,
                this.Options.Accounts[0],
                resolvedFolders,
                TestContext.Current.CancellationToken);

        /// <summary>Publishes a snapshot the reload contract would produce, which is a new instance whatever changed in it.</summary>
        internal void PublishReloadedSnapshot()
        {
            var reloaded = SynchronizationTestHost.CreateSingleAccountOptions(enabled: true, "INBOX");
            reloaded.Accounts[0].Mode = this.Options.Accounts[0].Mode;
            this.settings.Current = reloaded;
            this.Options = reloaded;
        }

        public void Dispose() => this.services.Dispose();
    }
}
