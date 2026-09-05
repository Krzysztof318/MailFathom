// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Notifications;
using MailFathom.Application.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Notifications;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Notifications;

/// <summary>Covers what a synchronization run tells its owner, and what it deliberately says nothing about.</summary>
public sealed class SynchronizationNotificationsTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private static readonly MailAccountIdentity SecondAccount =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("personal"));

    private static readonly DateTimeOffset RunInstant = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A run that commits forty messages is one arrival to somebody who was away, not forty.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_ManyMessagesInOneRun_RecordsOneNotificationCarryingTheCount()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        await notifications.ReportArrivedMailAsync(Account, 40, TestContext.Current.CancellationToken);

        // Assert
        var notification = Assert.Single(store.Recorded);
        Assert.Equal(NotificationKind.Mail, notification.Kind);
        Assert.Equal("40 new messages arrived.", notification.Body);
        Assert.Equal(NotificationScreen.Mail, notification.Target.Screen);
        Assert.Equal(RunInstant, notification.OccurredAt);
        Assert.False(notification.IsRead);
    }

    /// <summary>The count is the run's own, so a single message is described as one rather than as a plural.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_OneMessage_RecordsThatOneMessageArrived()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        await notifications.ReportArrivedMailAsync(Account, 1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("1 new message arrived.", Assert.Single(store.Recorded).Body);
    }

    /// <summary>An empty run is the ordinary case and is not an event anybody was away from the screen for.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_RunThatCommittedNothing_RecordsNothing()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        var recorded = await notifications.ReportArrivedMailAsync(Account, 0, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(recorded);
        Assert.Empty(store.Recorded);
    }

    /// <summary>Nothing composed here reads mail, so a notification carries no address, subject, or body fragment.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_AnyRun_NamesOnlyMailFathomsOwnIdentifiers()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        await notifications.ReportArrivedMailAsync(Account, 3, TestContext.Current.CancellationToken);

        // Assert
        var notification = Assert.Single(store.Recorded);
        Assert.Equal(Account.Id.Value, notification.Source);
        Assert.Equal(NotificationTargetKind.Screen, notification.Target.Kind);
        Assert.Null(notification.Target.Message);
    }

    /// <summary>A refused credential is refused again on every run, and a person who has not read the first statement gains nothing from a second.</summary>
    [Fact]
    public async Task ReportRefusedCredentialAsync_ConditionRepeatedWhileUnread_RecordsItOnce()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        var first = await notifications.ReportRefusedCredentialAsync(Account, TestContext.Current.CancellationToken);
        var second = await notifications.ReportRefusedCredentialAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(first);
        Assert.False(second);
        Assert.Single(store.Recorded);
    }

    /// <summary>The condition is one account's, so a second account being refused is its own statement rather than a repeat.</summary>
    [Fact]
    public async Task ReportRefusedCredentialAsync_SecondAccountRefused_RecordsItsOwnStatement()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        await notifications.ReportRefusedCredentialAsync(Account, TestContext.Current.CancellationToken);
        await notifications.ReportRefusedCredentialAsync(SecondAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, store.Recorded.Count);
        Assert.Equal(
            [Account.Id.Value, SecondAccount.Id.Value],
            store.Recorded.Select(notification => notification.Source));
    }

    /// <summary>A refused credential is the person's to repair, so the statement leads to where they repair it.</summary>
    [Fact]
    public async Task ReportRefusedCredentialAsync_RefusedAccount_LeadsToTheSettingsScreen()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        await notifications.ReportRefusedCredentialAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var notification = Assert.Single(store.Recorded);
        Assert.Equal(NotificationKind.System, notification.Kind);
        Assert.Equal(NotificationScreen.Settings, notification.Target.Screen);
    }

    /// <summary>An incomplete run and a refused credential are different conditions, so one never suppresses the other.</summary>
    [Fact]
    public async Task ReportIncompleteRunAsync_AccountAlreadyReportedAsRefused_RecordsItsOwnCondition()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        await notifications.ReportRefusedCredentialAsync(Account, TestContext.Current.CancellationToken);
        await notifications.ReportIncompleteRunAsync(Account, 2, 5, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, store.Recorded.Count);
        Assert.Contains(store.Recorded, notification => notification.Body.Contains(
            "2 of 5 folders",
            StringComparison.Ordinal));
    }

    /// <summary>A run that finished everything it scheduled is not news.</summary>
    [Fact]
    public async Task ReportIncompleteRunAsync_RunThatFinishedEveryFolder_RecordsNothing()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        var recorded = await notifications.ReportIncompleteRunAsync(
            Account,
            failedFolderCount: 0,
            scheduledFolderCount: 5,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(recorded);
        Assert.Empty(store.Recorded);
    }

    /// <summary>An incomplete run is something MailFathom keeps working on, so there is nothing for the person to open.</summary>
    [Fact]
    public async Task ReportIncompleteRunAsync_IncompleteRun_LeadsNowhere()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        await notifications.ReportIncompleteRunAsync(Account, 2, 5, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(NotificationTargetKind.Nothing, Assert.Single(store.Recorded).Target.Kind);
    }

    /// <summary>A negative count is a caller that miscounted rather than a run with nothing to say.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_NegativeCount_IsRefused()
    {
        // Arrange
        var notifications = CreateNotifications(new InMemoryNotificationStore());

        // Act
        var refusal = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => notifications.ReportArrivedMailAsync(Account, -1, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("newMessageCount", refusal.ParamName);
    }

    /// <summary>
    /// Nothing bounds a configured account identifier, and both places one reaches are bounded, so an outsized one is
    /// still reported rather than silently disabling every notification that account would ever produce.
    /// </summary>
    [Fact]
    public async Task ReportRefusedCredentialAsync_AnAccountIdentifierPastEveryBound_IsStillRecorded()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);
        var outsized = MailAccountIdentity.Create(
            SyntheticMailOwner.Deployment,
            MailAccountId.Create(new string('w', 400)));

        // Act
        await notifications.ReportRefusedCredentialAsync(outsized, TestContext.Current.CancellationToken);

        // Assert
        var notification = Assert.Single(store.Recorded);
        Assert.Null(notification.Source);
        Assert.StartsWith("credential-refused:", notification.DeduplicationKey.Value, StringComparison.Ordinal);
        Assert.True(notification.DeduplicationKey.Value.Length <= NotificationDeduplicationKey.MaximumLength);
    }

    /// <summary>A row written while somebody has the client open is said out loud, with what the bell has to draw.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_WithAClientListening_SignalsTheRaisedNotificationAndTheUnreadCount()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var channel = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider(RunInstant);
        await using var signals = new ClientSignals([channel], clock);
        var notifications = new SynchronizationNotifications(store, signals, clock);

        // Act
        await notifications.ReportArrivedMailAsync(Account, 4, TestContext.Current.CancellationToken);

        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        var signal = Assert.Single(channel.Published);
        Assert.Equal(ClientSignalKind.NotificationRaised, signal.Kind);
        Assert.Equal(SyntheticMailOwner.Deployment, signal.Owner);
        Assert.Equal(NotificationKind.Mail, signal.NotificationKind);
        Assert.Equal(1, signal.Count);
        Assert.Equal(Assert.Single(store.Recorded).Body, signal.SecondLine);
    }

    /// <summary>A notification the store recognized as one already raised says nothing to a client either.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_ARowTheStoreDeduplicated_SignalsNothingASecondTime()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var channel = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider(RunInstant);
        await using var signals = new ClientSignals([channel], clock);
        var notifications = new SynchronizationNotifications(store, signals, clock);

        // Act
        await notifications.ReportArrivedMailAsync(Account, 4, TestContext.Current.CancellationToken);
        await notifications.ReportArrivedMailAsync(Account, 4, TestContext.Current.CancellationToken);

        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        Assert.Single(store.Recorded);
        Assert.Single(channel.Published);
    }

    private static SynchronizationNotifications CreateNotifications(INotificationStore store) =>
        new(store, ClientSignalPublishers.ReachingNobody, new FakeTimeProvider(RunInstant));
}
