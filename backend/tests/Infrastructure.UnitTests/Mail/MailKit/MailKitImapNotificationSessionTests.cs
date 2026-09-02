// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Synchronization;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit;

/// <summary>Covers the long-lived IDLE session: capability, renewal, notification, recovery, and what it must never fetch.</summary>
public sealed class MailKitImapNotificationSessionTests
{
    /// <summary>Guards against a wait that never ends. No assertion depends on how long one actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    /// <summary>The renewal bound every test hands the session, so an elapsed wait is always the clock and never a real timer.</summary>
    private static readonly TimeSpan RenewalInterval = TimeSpan.FromMinutes(20);

    [Fact]
    public async Task OpenAsync_ServerAdvertisesIdle_WatchesTheFolderInPushMode()
    {
        // Arrange
        using var resilience = MailKitImapSessionTestContext.CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Idle };
        var folder = MailKitImapSessionTestContext.CreateSelectedFolder();

        // Act
        var result = await MailKitImapSessionTestContext.OpenNotificationSessionAsync(
            resilience,
            client,
            folder,
            new FakeTimeProvider());

        // Assert
        Assert.Equal(MailSynchronizationMode.Push, result.EffectiveMode);
        Assert.NotNull(result.Session);

        await result.Session.DisposeAsync();
    }

    /// <summary>A server without IDLE is an ordinary answer: the folder is polled and no connection is left holding a slot.</summary>
    [Fact]
    public async Task OpenAsync_ServerAdvertisesNoIdle_ReportsPollingAndClosesTheConnection()
    {
        // Arrange
        using var resilience = MailKitImapSessionTestContext.CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.None };
        var folder = MailKitImapSessionTestContext.CreateSelectedFolder();

        // Act
        var result = await MailKitImapSessionTestContext.OpenNotificationSessionAsync(
            resilience,
            client,
            folder,
            new FakeTimeProvider());

        // Assert
        Assert.Equal(MailSynchronizationMode.Polling, result.EffectiveMode);
        Assert.Null(result.Session);
        Assert.Equal(1, client.DisposeCount);
    }

    /// <summary>The renewal bound is armed on the injected clock, so it holds for exactly what the caller asked for.</summary>
    [Fact]
    public async Task WaitForFolderChangeAsync_NothingIsReported_HoldsUntilTheSuppliedWaitElapses()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var resilience = MailKitImapSessionTestContext.CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Idle };
        var folder = MailKitImapSessionTestContext.CreateSelectedFolder();
        await using var session = await OpenWatchingSessionAsync(resilience, client, folder, clock);

        // Act
        var waiting = session.WaitForFolderChangeAsync(RenewalInterval, TestContext.Current.CancellationToken);
        await client.IdleEntered.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        clock.Advance(RenewalInterval - TimeSpan.FromSeconds(1));
        var endedEarly = waiting.IsCompleted;
        clock.Advance(TimeSpan.FromSeconds(1));
        var outcome = await waiting.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(endedEarly);
        Assert.Equal(MailboxNotificationOutcome.WaitElapsed, outcome);
        Assert.Equal(1, client.IdleCount);
    }

    /// <summary>Renewal is one IDLE command after another over the same connection, never a reconnection.</summary>
    [Fact]
    public async Task WaitForFolderChangeAsync_CalledAgainAfterAnElapsedWait_ReissuesIdleOnTheSameConnection()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var resilience = MailKitImapSessionTestContext.CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Idle };
        var folder = MailKitImapSessionTestContext.CreateSelectedFolder();
        await using var session = await OpenWatchingSessionAsync(resilience, client, folder, clock);

        // Act
        await ElapseOneWaitAsync(session, client, clock);
        await ElapseOneWaitAsync(session, client, clock);

        // Assert
        Assert.Equal(2, client.IdleCount);
        Assert.Equal(1, client.ConnectCount);
    }

    /// <summary>A reported change is the whole product of a wait; what changed is left to the synchronization pass.</summary>
    [Fact]
    public async Task WaitForFolderChangeAsync_ServerReportsNewMail_ReportsTheFolderChanged()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var resilience = MailKitImapSessionTestContext.CreateSingleAttemptResilience();
        var folder = MailKitImapSessionTestContext.CreateSelectedFolder();
        var client = new FakeImapClient
        {
            Capabilities = ImapCapabilities.Idle,
            IdleBehavior = _ =>
            {
                folder.CountChanged += Raise.Event<EventHandler<EventArgs>>(folder, EventArgs.Empty);

                return Task.CompletedTask;
            },
        };
        await using var session = await OpenWatchingSessionAsync(resilience, client, folder, clock);

        // Act
        var outcome = await session
            .WaitForFolderChangeAsync(RenewalInterval, TestContext.Current.CancellationToken)
            .WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxNotificationOutcome.FolderChanged, outcome);
    }

    /// <summary>
    /// An expunge and a flag change are reasons to synchronize too; watching arrival alone would defer both to the
    /// interval. A removal is watched through both of the events that can carry it, because a connection with quick
    /// resynchronization enabled reports one and never the other — a session subscribed to a single one of them would
    /// stop noticing deletions on exactly the servers that support the most synchronization machinery.
    /// </summary>
    [Theory]
    [InlineData("MessageExpunged")]
    [InlineData("MessagesVanished")]
    [InlineData("MessageFlagsChanged")]
    public async Task WaitForFolderChangeAsync_ServerReportsAnExpungeOrAFlagChange_ReportsTheFolderChanged(string reportedEvent)
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var resilience = MailKitImapSessionTestContext.CreateSingleAttemptResilience();
        var folder = MailKitImapSessionTestContext.CreateSelectedFolder();
        var client = new FakeImapClient
        {
            Capabilities = ImapCapabilities.Idle,
            IdleBehavior = _ =>
            {
                RaiseFolderEvent(folder, reportedEvent);

                return Task.CompletedTask;
            },
        };
        await using var session = await OpenWatchingSessionAsync(resilience, client, folder, clock);

        // Act
        var outcome = await session
            .WaitForFolderChangeAsync(RenewalInterval, TestContext.Current.CancellationToken)
            .WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxNotificationOutcome.FolderChanged, outcome);
    }

    /// <summary>A session outlives a dropped connection: the next wait establishes a new one and reselects read-only.</summary>
    [Fact]
    public async Task WaitForFolderChangeAsync_ConnectionDropsWhileIdling_ReestablishesItForTheNextWait()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var resilience = MailKitImapSessionTestContext.CreateSingleAttemptResilience();
        var folder = MailKitImapSessionTestContext.CreateSelectedFolder();
        var droppedClient = new FakeImapClient
        {
            Capabilities = ImapCapabilities.Idle,
            Folder = folder,
            IdleException = new IOException("The server closed the connection."),
        };
        droppedClient.AuthenticationMechanisms.Add("PLAIN");
        var recoveredClient = new FakeImapClient { Capabilities = ImapCapabilities.Idle, Folder = folder };
        recoveredClient.AuthenticationMechanisms.Add("PLAIN");
        var factory = MailKitImapSessionTestContext.CreateNotificationSessionFactory(
            resilience,
            MailKitImapSessionTestContext.ConnectionSequence(droppedClient, recoveredClient),
            clock);
        var opened = await factory.OpenAsync(
            MailKitImapSessionTestContext.PrimaryAccount,
            MailKitImapSessionTestContext.InboxFolder,
            MailKitImapSessionTestContext.TlsOnConnectWithPlainPolicy,
            TestContext.Current.CancellationToken);
        await using var session = opened.Session!;

        // Act
        await Assert.ThrowsAsync<MailboxUnavailableException>(
            () => session.WaitForFolderChangeAsync(RenewalInterval, TestContext.Current.CancellationToken));
        await ElapseOneWaitAsync(session, recoveredClient, clock);

        // Assert
        Assert.Equal(1, recoveredClient.ConnectCount);
        await folder.Received().OpenAsync(FolderAccess.ReadOnly, Arg.Any<CancellationToken>());
        await folder.DidNotReceive().OpenAsync(FolderAccess.ReadWrite, Arg.Any<CancellationToken>());
    }

    /// <summary>Shutdown ends the idle state and leaves the session disposable, rather than tearing the command down mid-flight.</summary>
    [Fact]
    public async Task WaitForFolderChangeAsync_Cancelled_EndsTheWaitAndLeavesTheSessionUsable()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var resilience = MailKitImapSessionTestContext.CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Idle };
        var folder = MailKitImapSessionTestContext.CreateSelectedFolder();
        await using var session = await OpenWatchingSessionAsync(resilience, client, folder, clock);
        using var shutdown = new CancellationTokenSource();

        // Act
        var waiting = session.WaitForFolderChangeAsync(RenewalInterval, shutdown.Token);
        await client.IdleEntered.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        await shutdown.CancelAsync();
        var outcome = await waiting.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxNotificationOutcome.WaitElapsed, outcome);
        Assert.Equal(0, client.DisposeCount);
    }

    /// <summary>The push path must have no retrieval of its own; a fetch here would mark a whole mailbox as read.</summary>
    [Fact]
    public async Task WaitForFolderChangeAsync_ServerReportsAChange_FetchesNothingAndSetsNoFlag()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var resilience = MailKitImapSessionTestContext.CreateSingleAttemptResilience();
        var folder = MailKitImapSessionTestContext.CreateSelectedFolder();
        var client = new FakeImapClient
        {
            Capabilities = ImapCapabilities.Idle,
            IdleBehavior = _ =>
            {
                folder.CountChanged += Raise.Event<EventHandler<EventArgs>>(folder, EventArgs.Empty);

                return Task.CompletedTask;
            },
        };
        await using var session = await OpenWatchingSessionAsync(resilience, client, folder, clock);

        // Act
        await session
            .WaitForFolderChangeAsync(RenewalInterval, TestContext.Current.CancellationToken)
            .WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        await folder.DidNotReceive().GetStreamAsync(Arg.Any<UniqueId>(), Arg.Any<CancellationToken>());
        await folder.DidNotReceive().FetchAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IFetchRequest>(), Arg.Any<CancellationToken>());
        await folder.DidNotReceive().SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>());
        await folder.DidNotReceive().StoreAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IStoreFlagsRequest>(), Arg.Any<CancellationToken>());
        await folder.DidNotReceive().OpenAsync(FolderAccess.ReadWrite, Arg.Any<CancellationToken>());
    }

    /// <summary>Opens a session against a server that advertises IDLE, failing the test loudly if it did not.</summary>
    private static async Task<IMailboxNotificationSession> OpenWatchingSessionAsync(
        OutboundResilienceTestHost resilience,
        FakeImapClient client,
        IMailFolder folder,
        FakeTimeProvider clock)
    {
        var result = await MailKitImapSessionTestContext.OpenNotificationSessionAsync(
            resilience,
            client,
            folder,
            clock);

        return result.Session
            ?? throw new InvalidOperationException("The scripted server was expected to advertise IDLE.");
    }

    /// <summary>Runs one wait through to its renewal deadline, which is the shape of every wait that observed nothing.</summary>
    private static async Task ElapseOneWaitAsync(
        IMailboxNotificationSession session,
        FakeImapClient client,
        FakeTimeProvider clock)
    {
        var idleCountBefore = client.IdleCount;
        var waiting = session.WaitForFolderChangeAsync(RenewalInterval, TestContext.Current.CancellationToken);

        while (client.IdleCount == idleCountBefore && !waiting.IsCompleted)
        {
            await Task.Yield();
        }

        clock.Advance(RenewalInterval);

        await waiting.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
    }

    private static void RaiseFolderEvent(IMailFolder folder, string reportedEvent)
    {
        if (reportedEvent == "MessageExpunged")
        {
            folder.MessageExpunged += Raise.Event<EventHandler<MessageEventArgs>>(folder, new MessageEventArgs(0));

            return;
        }

        if (reportedEvent == "MessagesVanished")
        {
            folder.MessagesVanished += Raise.Event<EventHandler<MessagesVanishedEventArgs>>(
                folder,
                new MessagesVanishedEventArgs([new UniqueId(10)], earlier: false));

            return;
        }

        folder.MessageFlagsChanged += Raise.Event<EventHandler<MessageFlagsChangedEventArgs>>(
            folder,
            new MessageFlagsChangedEventArgs(0, MessageFlags.Seen));
    }
}
