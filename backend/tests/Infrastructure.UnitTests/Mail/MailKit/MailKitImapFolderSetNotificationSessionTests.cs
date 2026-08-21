// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Infrastructure.Mail.MailKit;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit;
using MailKit.Net.Imap;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit;

/// <summary>Covers the subscription that watches several folders over one connection: capability, renewal, and which folder it names.</summary>
public sealed class MailKitImapFolderSetNotificationSessionTests
{
    /// <summary>Guards against a wait that never ends. No assertion depends on how long one actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    /// <summary>The renewal bound every test hands the session, so an elapsed wait is always the clock and never a real timer.</summary>
    private static readonly TimeSpan RenewalInterval = TimeSpan.FromMinutes(20);

    private static readonly MailFolderResolution ArchiveFolder = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("archive"),
        RemoteFolderPath.Create("Archive", '/'));

    private static readonly MailFolderResolution SentFolder = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("sent"),
        RemoteFolderPath.Create("Sent", '/'));

    /// <summary>Both capabilities together are what makes one connection able to report on a folder it has not selected.</summary>
    [Fact]
    public async Task OpenForFoldersAsync_ServerAdvertisesNotifyAndIdle_WatchesEveryFolderInPushMode()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Notify | ImapCapabilities.Idle };

        // Act
        var result = await OpenSubscriptionAsync(resilience, client, new FakeTimeProvider());

        // Assert
        Assert.Equal(MailSynchronizationMode.Push, result.EffectiveMode);
        Assert.NotNull(result.Session);

        await result.Session.DisposeAsync();
    }

    /// <summary>
    /// A server offering only one of the two cannot serve this session, and saying so is what lets the caller fall back
    /// to watching one folder per connection instead of leaving the account on its interval.
    /// </summary>
    [Theory]
    [InlineData(ImapCapabilities.Idle)]
    [InlineData(ImapCapabilities.Notify)]
    [InlineData(ImapCapabilities.None)]
    public async Task OpenForFoldersAsync_ServerLacksEitherCapability_ReportsPollingAndClosesTheConnection(
        ImapCapabilities advertisedCapabilities)
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = advertisedCapabilities };

        // Act
        var result = await OpenSubscriptionAsync(resilience, client, new FakeTimeProvider());

        // Assert
        Assert.Equal(MailSynchronizationMode.Polling, result.EffectiveMode);
        Assert.Null(result.Session);
        Assert.Equal(1, client.DisposeCount);
    }

    /// <summary>A folder other than the selected one reports its change as a moved message count, and it has to be named.</summary>
    [Fact]
    public async Task WaitForFolderChangeAsync_ServerReportsANonSelectedFolder_NamesThatFolder()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Notify | ImapCapabilities.Idle };
        var archiveFolder = CreateSelectedFolder();
        client.FoldersByPath[ArchiveFolder.RemotePath.Value] = archiveFolder;
        client.IdleBehavior = _ =>
        {
            archiveFolder.CountChanged += Raise.EventWith(archiveFolder, EventArgs.Empty);

            return Task.CompletedTask;
        };
        await using var session = await OpenWatchingSessionAsync(resilience, client, clock);

        // Act
        var outcome = await session
            .WaitForFolderChangeAsync(RenewalInterval, TestContext.Current.CancellationToken)
            .WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxNotificationOutcome.FolderChanged, outcome.Outcome);
        Assert.Equal(ArchiveFolder.Alias, outcome.ChangedFolder);
    }

    /// <summary>The selected folder is watched through the events it raises directly, which are not the ones the others use.</summary>
    [Fact]
    public async Task WaitForFolderChangeAsync_ServerReportsTheSelectedFolder_NamesTheFolderTheConnectionOpened()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Notify | ImapCapabilities.Idle };
        var selectedFolder = CreateSelectedFolder();
        client.Folder = selectedFolder;
        client.IdleBehavior = _ =>
        {
            selectedFolder.MessageFlagsChanged += Raise.EventWith(
                selectedFolder,
                new MessageFlagsChangedEventArgs(0, MessageFlags.Seen));

            return Task.CompletedTask;
        };
        await using var session = await OpenWatchingSessionAsync(resilience, client, clock);

        // Act
        var outcome = await session
            .WaitForFolderChangeAsync(RenewalInterval, TestContext.Current.CancellationToken)
            .WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(InboxFolder.Alias, outcome.ChangedFolder);
    }

    /// <summary>
    /// A removal reaches a connection with quick resynchronization enabled as a vanished report and never as an
    /// expunge, so a session watching only the older event would stop noticing deletions on the most capable servers.
    /// </summary>
    [Fact]
    public async Task WaitForFolderChangeAsync_ServerReportsVanishedMessages_ReportsTheFolderChanged()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient
        {
            Capabilities = ImapCapabilities.Notify | ImapCapabilities.Idle | ImapCapabilities.QuickResync,
        };
        var sentFolder = CreateSelectedFolder();
        client.FoldersByPath[SentFolder.RemotePath.Value] = sentFolder;
        client.IdleBehavior = _ =>
        {
            sentFolder.MessagesVanished += Raise.EventWith(
                sentFolder,
                new MessagesVanishedEventArgs([new UniqueId(10)], earlier: false));

            return Task.CompletedTask;
        };
        await using var session = await OpenWatchingSessionAsync(resilience, client, clock);

        // Act
        var outcome = await session
            .WaitForFolderChangeAsync(RenewalInterval, TestContext.Current.CancellationToken)
            .WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SentFolder.Alias, outcome.ChangedFolder);
    }

    /// <summary>Nothing reported is an ordinary return that leaves the session ready to be renewed.</summary>
    [Fact]
    public async Task WaitForFolderChangeAsync_NothingIsReported_HoldsUntilTheSuppliedWaitElapses()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Notify | ImapCapabilities.Idle };
        await using var session = await OpenWatchingSessionAsync(resilience, client, clock);

        // Act
        var waiting = session.WaitForFolderChangeAsync(RenewalInterval, TestContext.Current.CancellationToken);
        await client.IdleEntered.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        clock.Advance(RenewalInterval);
        var outcome = await waiting.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxNotificationOutcome.WaitElapsed, outcome.Outcome);
        Assert.Null(outcome.ChangedFolder);
    }

    /// <summary>
    /// A wait is where a dropped connection would be rebuilt, and a rebuilt connection holds no subscription, so the
    /// subscription belongs to the wait rather than to the session's opening.
    /// </summary>
    [Fact]
    public async Task WaitForFolderChangeAsync_CalledAgainAfterAnElapsedWait_SubscribesAgainOnTheSameConnection()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Notify | ImapCapabilities.Idle };
        var subscriptionCount = 0;
        await using var session = await OpenWatchingSessionAsync(
            resilience,
            client,
            clock,
            (_, _, _) =>
            {
                subscriptionCount++;

                return Task.CompletedTask;
            });

        // Act
        await ElapseOneWaitAsync(session, client, clock);
        await ElapseOneWaitAsync(session, client, clock);

        // Assert
        Assert.Equal(2, subscriptionCount);
        Assert.Equal(2, client.IdleCount);
        Assert.Equal(1, client.ConnectCount);
    }

    /// <summary>The subscription names every folder beside the selected one, which is what makes the bound mean anything.</summary>
    [Fact]
    public async Task WaitForFolderChangeAsync_SeveralFoldersWatched_SubscribesToEveryFolderBesideTheSelectedOne()
    {
        // Arrange
        var clock = new FakeTimeProvider();
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Notify | ImapCapabilities.Idle };
        var archiveFolder = CreateSelectedFolder();
        var sentFolder = CreateSelectedFolder();
        client.FoldersByPath[ArchiveFolder.RemotePath.Value] = archiveFolder;
        client.FoldersByPath[SentFolder.RemotePath.Value] = sentFolder;
        var subscribedFolders = new List<IReadOnlyList<IMailFolder>>();
        await using var session = await OpenWatchingSessionAsync(
            resilience,
            client,
            clock,
            (_, additionalFolders, _) =>
            {
                subscribedFolders.Add(additionalFolders);

                return Task.CompletedTask;
            });

        // Act
        await ElapseOneWaitAsync(session, client, clock);

        // Assert
        Assert.Equal([archiveFolder, sentFolder], Assert.Single(subscribedFolders));

        // The selected folder is reached through the selection the connection already made rather than requested
        // again, because a second request would hand back a folder object nothing is listening to.
        Assert.Equal(1, client.RequestedFolderPaths.Count(path => path == InboxFolder.RemotePath.Value));
    }

    /// <summary>Runs one wait to its renewal deadline, which is how a test advances a session without a real timer.</summary>
    private static async Task ElapseOneWaitAsync(
        IMailboxFolderSetNotificationSession session,
        FakeImapClient client,
        FakeTimeProvider clock)
    {
        var waiting = session.WaitForFolderChangeAsync(RenewalInterval, TestContext.Current.CancellationToken);
        await client.IdleEntered.Task.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
        clock.Advance(RenewalInterval);

        await waiting.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
    }

    private static async Task<IMailboxFolderSetNotificationSession> OpenWatchingSessionAsync(
        OutboundResilienceTestHost resilience,
        FakeImapClient client,
        FakeTimeProvider clock,
        ImapChangeSubscriptionCommand? requestFolderNotifications = null)
    {
        var result = await OpenSubscriptionAsync(resilience, client, clock, requestFolderNotifications);

        return result.Session ?? throw new InvalidOperationException("The scripted server declined the subscription.");
    }

    private static Task<MailboxFolderSetNotificationSessionResult> OpenSubscriptionAsync(
        OutboundResilienceTestHost resilience,
        FakeImapClient client,
        FakeTimeProvider clock,
        ImapChangeSubscriptionCommand? requestFolderNotifications = null)
    {
        client.Folder ??= CreateSelectedFolder();
        client.AuthenticationMechanisms.Add("PLAIN");

        return CreateNotificationSessionFactory(resilience, () => client.Client, clock, requestFolderNotifications)
            .OpenForFoldersAsync(
                PrimaryAccount,
                [InboxFolder, ArchiveFolder, SentFolder],
                TlsOnConnectWithPlainPolicy,
                CancellationToken.None);
    }
}
