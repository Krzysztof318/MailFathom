// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Notifications;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Notifications;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Notifications;

/// <summary>
/// Covers the use case a person reads and marks their own notification centre through. What it has to hold is that the
/// owner acted on is the one the credential authenticated rather than one a caller could name, that a page is bounded
/// and clamped rather than refused, that the walk continues from a boundary this deployment issued to this caller, and
/// that a notification somebody else holds answers exactly as one that does not exist.
/// </summary>
public sealed class OwnNotificationsTests
{
    private static readonly MailOwnerId Owner = SyntheticMailOwner.Deployment;

    private static readonly MailOwnerId SomebodyElse = MailOwnerId.Create(
        new Guid("6d0b6a1c-6f5e-4a7e-9a1a-8d2a3f4b5c60"));

    private static readonly DateTimeOffset FirstInstant = new(2026, 9, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReadPageAsync_APersonWithNotifications_AnswersTheirOwnNewestFirst()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        await RecordAsync(store, Owner, occurredAtOffsetMinutes: 0, "oldest");
        await RecordAsync(store, Owner, occurredAtOffsetMinutes: 10, "newest");
        await RecordAsync(store, SomebodyElse, occurredAtOffsetMinutes: 5, "somebody-else");

        var notifications = SignedIn(store);

        // Act
        var page = await notifications.ReadPageAsync(null, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["newest", "oldest"],
            page!.Notifications.Select(notification => notification.DeduplicationKey.Value));
    }

    /// <summary>A panel asks for as much as it can draw, so the useful answer to a thousand is the most this deployment serves.</summary>
    [Theory]
    [InlineData(null, OwnNotifications.DefaultPageSize)]
    [InlineData(0, OwnNotifications.DefaultPageSize)]
    [InlineData(-5, OwnNotifications.DefaultPageSize)]
    [InlineData(1000, OwnNotifications.MaximumPageSize)]
    [InlineData(7, 7)]
    public async Task ReadPageAsync_APageSizeAskedFor_IsClampedToWhatThisDeploymentServes(int? asked, int served)
    {
        // Arrange
        var store = new RecordingNotificationStore();
        var notifications = SignedIn(store);

        // Act
        await notifications.ReadPageAsync(asked, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(served, store.LastLimit);
    }

    /// <summary>The boundary continues the walk rather than shifting a window, so a page raised into cannot repeat or skip a row.</summary>
    [Fact]
    public async Task ReadPageAsync_TheCursorAPreviousPageReturned_ContinuesBeyondIt()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        await RecordAsync(store, Owner, occurredAtOffsetMinutes: 0, "oldest");
        await RecordAsync(store, Owner, occurredAtOffsetMinutes: 10, "newest");

        var notifications = SignedIn(store);
        var first = await notifications.ReadPageAsync(1, null, TestContext.Current.CancellationToken);

        // Act
        var second = await notifications.ReadPageAsync(
            1,
            first!.NextCursor!.Value.Encode(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("oldest", Assert.Single(second!.Notifications).DeduplicationKey.Value);
    }

    /// <summary>A short page is the end of the centre, so a caller stops on the absent cursor rather than on a length comparison.</summary>
    [Fact]
    public async Task ReadPageAsync_APageTheCentreCouldNotFill_CarriesNoCursor()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        await RecordAsync(store, Owner, occurredAtOffsetMinutes: 0, "only");

        var notifications = SignedIn(store);

        // Act
        var page = await notifications.ReadPageAsync(10, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(page!.NextCursor);
    }

    /// <summary>A boundary this deployment never issued names no page, and answering it with the newest one would be a panel silently jumping to the top.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorThisDeploymentDidNotIssue_IsRefusedRatherThanIgnored()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        await RecordAsync(store, Owner, occurredAtOffsetMinutes: 0, "only");

        // Act
        var page = await SignedIn(store).ReadPageAsync(
            10,
            "not-a-cursor-this-deployment-issued",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(page);
    }

    /// <summary>A query string is composed by a page rather than typed, so a screen with nothing to continue from sends an empty value rather than none.</summary>
    [Fact]
    public async Task ReadPageAsync_AnEmptyCursor_IsReadAsTheNewestPageRatherThanRefused()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        await RecordAsync(store, Owner, occurredAtOffsetMinutes: 0, "only");

        // Act
        var page = await SignedIn(store).ReadPageAsync(10, "  ", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("only", Assert.Single(page!.Notifications).DeduplicationKey.Value);
    }

    /// <summary>The fingerprint is the owner, so a cursor issued to somebody else names no boundary in this caller's own walk.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorIssuedForAnotherOwner_IsRefused()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var elsewhere = NotificationCursor.After(
            FirstInstant,
            NotificationId.Create(Guid.CreateVersion7(FirstInstant)),
            NotificationCursor.FingerprintOf(SomebodyElse));

        // Act
        var page = await SignedIn(store).ReadPageAsync(
            10,
            elsewhere.Encode(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(page);
    }

    /// <summary>The badge is what a client asks for most, and it counts the caller's own unread notifications and nobody else's.</summary>
    [Fact]
    public async Task CountUnreadAsync_ADeploymentServingSeveralPeople_CountsTheCallersOwnUnreadOnly()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        await RecordAsync(store, Owner, occurredAtOffsetMinutes: 0, "mine");
        await RecordAsync(store, SomebodyElse, occurredAtOffsetMinutes: 0, "theirs");
        await RecordAsync(store, SomebodyElse, occurredAtOffsetMinutes: 5, "theirs-too");

        // Act
        var unreadCount = await SignedIn(store).CountUnreadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, unreadCount);
    }

    [Fact]
    public async Task SetReadAsync_ANotificationOfTheirOwn_MarksItReadAndUnreadAgain()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notification = await RecordAsync(store, Owner, occurredAtOffsetMinutes: 0, "mine");
        var notifications = SignedIn(store);

        // Act
        var read = await notifications.SetReadAsync(notification.Id, true, TestContext.Current.CancellationToken);
        var readCount = await notifications.CountUnreadAsync(TestContext.Current.CancellationToken);
        var unread = await notifications.SetReadAsync(notification.Id, false, TestContext.Current.CancellationToken);
        var unreadCount = await notifications.CountUnreadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(NotificationReadOutcome.Applied, read);
        Assert.Equal(0, readCount);
        Assert.Equal(NotificationReadOutcome.Applied, unread);
        Assert.Equal(1, unreadCount);
    }

    /// <summary>The refusal is the same one an absent notification gets, so nothing here reports whose notifications exist.</summary>
    [Fact]
    public async Task SetReadAsync_ANotificationSomebodyElseHolds_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var theirs = await RecordAsync(store, SomebodyElse, occurredAtOffsetMinutes: 0, "theirs");
        var notifications = SignedIn(store);

        // Act
        var somebodyElses = await notifications.SetReadAsync(theirs.Id, true, TestContext.Current.CancellationToken);
        var nobodys = await notifications.SetReadAsync(
            NotificationId.Create(Guid.CreateVersion7(FirstInstant)),
            true,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(NotificationReadOutcome.NotFound, somebodyElses);
        Assert.Equal(NotificationReadOutcome.NotFound, nobodys);
        Assert.False(store.Recorded.Single(candidate => candidate.Id == theirs.Id).IsRead);
    }

    /// <summary>The deduplication rule holds one unread notification per condition, so a condition said again after this one was read stands in its place.</summary>
    [Fact]
    public async Task SetReadAsync_AConditionThatStandsUnreadAgain_RefusesToMarkTheOlderOneUnread()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var older = await RecordAsync(store, Owner, occurredAtOffsetMinutes: 0, "credential-refused");
        var notifications = SignedIn(store);

        await notifications.SetReadAsync(older.Id, true, TestContext.Current.CancellationToken);
        await RecordAsync(store, Owner, occurredAtOffsetMinutes: 10, "credential-refused");

        // Act
        var outcome = await notifications.SetReadAsync(older.Id, false, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(NotificationReadOutcome.ConditionAlreadyStanding, outcome);
        Assert.True(store.Recorded.Single(candidate => candidate.Id == older.Id).IsRead);
    }

    [Fact]
    public async Task MarkAllReadAsync_ADeploymentServingSeveralPeople_MarksTheCallersOwnAndLeavesTheRest()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        await RecordAsync(store, Owner, occurredAtOffsetMinutes: 0, "mine");
        await RecordAsync(store, Owner, occurredAtOffsetMinutes: 5, "mine-too");
        var theirs = await RecordAsync(store, SomebodyElse, occurredAtOffsetMinutes: 0, "theirs");

        var notifications = SignedIn(store);

        // Act
        var markedCount = await notifications.MarkAllReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, markedCount);
        Assert.Equal(0, await notifications.CountUnreadAsync(TestContext.Current.CancellationToken));
        Assert.False(store.Recorded.Single(candidate => candidate.Id == theirs.Id).IsRead);
    }

    /// <summary>The centre is a person's own working state, so a caller holding no reading grant reaches none of it.</summary>
    [Fact]
    public async Task ReadPageAsync_ACallerWithoutTheReadingGrant_IsRefused()
    {
        // Arrange
        var notifications = new OwnNotifications(
            AccessAuthorizations.ForOwnerGranted(Owner, MailFathomPermission.MailSend),
            new InMemoryNotificationStore());

        // Act and assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => notifications.ReadPageAsync(null, null, TestContext.Current.CancellationToken));
    }

    private static OwnNotifications SignedIn(INotificationStore store) => new(
        AccessAuthorizations.ForOwnerGranted(Owner, MailFathomPermission.MailRead),
        store);

    private static async Task<Notification> RecordAsync(
        InMemoryNotificationStore store,
        MailOwnerId owner,
        int occurredAtOffsetMinutes,
        string deduplicationKey)
    {
        var occurredAt = FirstInstant + TimeSpan.FromMinutes(occurredAtOffsetMinutes);
        var notification = Notification.Compose(
            NotificationId.Create(Guid.CreateVersion7(occurredAt)),
            owner,
            NotificationKind.System,
            title: "Something happened",
            body: "Something happened that nobody was at the screen for.",
            source: "work",
            NotificationTarget.Nothing,
            NotificationDeduplicationKey.Create(deduplicationKey),
            occurredAt);

        await store.RecordAsync(notification, TestContext.Current.CancellationToken);

        return notification;
    }

    /// <summary>Reports the limit the use case asked a store for, which is what the clamp is observable through.</summary>
    private sealed class RecordingNotificationStore : INotificationStore
    {
        public int LastLimit { get; private set; }

        public Task<bool> RecordAsync(Notification notification, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<IReadOnlyList<Notification>> ReadPageAsync(
            MailOwnerId owner,
            NotificationCursor? after,
            int limit,
            CancellationToken cancellationToken)
        {
            this.LastLimit = limit;

            return Task.FromResult<IReadOnlyList<Notification>>([]);
        }

        public Task<int> CountUnreadAsync(MailOwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<NotificationReadOutcome> SetReadAsync(
            MailOwnerId owner,
            NotificationId notification,
            bool isRead,
            CancellationToken cancellationToken) =>
            Task.FromResult(NotificationReadOutcome.Applied);

        public Task<int> MarkAllReadAsync(MailOwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<int> EraseOccurredBeforeAsync(
            MailOwnerId owner,
            DateTimeOffset occurredBefore,
            int limit,
            CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }
}
