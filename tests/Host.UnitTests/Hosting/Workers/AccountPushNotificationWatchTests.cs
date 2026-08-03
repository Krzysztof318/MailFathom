// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
        await SynchronizationTestHost.AdvanceUntilAsync(harness.Clock, waiting, TimeSpan.FromMinutes(5), DeadlockGuard);

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
