// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.Application.Notifications;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Notifications;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>
/// Covers the five routes a person reads, marks, and clears their own notification centre over. What separates them from the
/// mail routes is that nothing here reaches a mailbox: the page is drawn from what a producer already derived, a
/// notification another person holds answers as one that does not exist, and both writes are admitted under the grant a
/// signed-in person already holds.
/// </summary>
public sealed class ClientNotificationEndpointsTests
{
    private static readonly MailUserId User = SyntheticMailUser.Deployment;

    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 3, 8, 30, 0, TimeSpan.Zero);

    private static readonly Guid NotificationIdentifier = new("2f0b1c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d");

    private static readonly Guid SecondNotificationIdentifier = new("3a1c2d5e-6f70-4b8c-9dae-1f2a3b4c5d6e");

    [Fact]
    public async Task ReadPageAsync_APageOfNotifications_DescribesEachRowAndTheBoundaryTheNextPageContinuesFrom()
    {
        // Arrange
        var cursor = NotificationCursor.After(
            OccurredAt,
            NotificationId.Create(NotificationIdentifier),
            NotificationCursor.FingerprintOf(User));
        var notifications = Substitute.For<INotificationStore>();
        notifications.ReadPageAsync(User, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => [MailNotification(NotificationStatement.MailArrived(2))]);

        // Act
        var result = await ClientNotificationEndpoints.ReadPageAsync(
            pageSize: 1,
            cursor: null,
            SignedIn(notifications),
            TestContext.Current.CancellationToken);

        // Assert
        var page = Assert.IsType<Ok<ClientNotificationPageResponse>>(result.Result).Value!;
        var row = Assert.Single(page.Notifications);

        Assert.Equal(NotificationIdentifier, row.Id);
        Assert.Equal("Mail", row.Kind);
        Assert.Equal("Message", row.Target.Kind);
        Assert.Equal(OccurredAt, row.OccurredAt);
        Assert.False(row.Read);
        Assert.Equal(cursor.Encode(), page.NextCursor);

        // The condition and its numbers rather than a sentence, because what language the row is read in is the
        // client's to decide and this answer serves both of them.
        Assert.Equal("MailArrived", row.Statement?.Cause);
        Assert.Equal(2, row.Statement?.Counted);
        Assert.Null(row.Statement?.OutOf);
    }

    /// <summary>A row written before conditions were kept names none, and the answer says so rather than inventing one.</summary>
    [Fact]
    public async Task ReadPageAsync_ARowNamingNoCondition_DescribesItWithNoStatementAtAll()
    {
        // Arrange
        var notifications = Substitute.For<INotificationStore>();
        notifications.ReadPageAsync(User, null, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => [MailNotification(statement: null)]);

        // Act
        var result = await ClientNotificationEndpoints.ReadPageAsync(
            pageSize: 1,
            cursor: null,
            SignedIn(notifications),
            TestContext.Current.CancellationToken);

        // Assert
        var page = Assert.IsType<Ok<ClientNotificationPageResponse>>(result.Result).Value!;

        Assert.Null(Assert.Single(page.Notifications).Statement);
    }

    /// <summary>A boundary this deployment never issued names no page, and the newest one would be a panel silently jumping to the top.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorThisDeploymentDidNotIssue_RefusesWithoutEchoingIt()
    {
        // Arrange
        var notifications = Substitute.For<INotificationStore>();

        // Act
        var result = await ClientNotificationEndpoints.ReadPageAsync(
            pageSize: null,
            cursor: "not-a-cursor-this-deployment-issued",
            SignedIn(notifications),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.DoesNotContain(
            "not-a-cursor-this-deployment-issued",
            refusal.ProblemDetails.Detail!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadUnreadCountAsync_APersonWithUnreadNotifications_AnswersTheCountWithoutReadingAPage()
    {
        // Arrange
        var notifications = Substitute.For<INotificationStore>();
        notifications.CountUnreadAsync(User, Arg.Any<CancellationToken>()).Returns(4);

        // Act
        var result = await ClientNotificationEndpoints.ReadUnreadCountAsync(
            SignedIn(notifications),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(4, result.Value!.UnreadCount);
        await notifications.DidNotReceive().ReadPageAsync(
            Arg.Any<MailUserId>(),
            Arg.Any<NotificationCursor?>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The answer carries the state and the badge together, so a client redraws both without fetching the page again.</summary>
    [Fact]
    public async Task SetReadStateAsync_ANotificationOfTheirOwn_AnswersTheNewStateAndWhatIsLeftUnread()
    {
        // Arrange
        var notifications = Substitute.For<INotificationStore>();
        notifications.SetReadAsync(
                User,
                NotificationId.Create(NotificationIdentifier),
                true,
                Arg.Any<CancellationToken>())
            .Returns(NotificationReadOutcome.Applied);
        notifications.CountUnreadAsync(User, Arg.Any<CancellationToken>()).Returns(2);

        // Act
        var result = await ClientNotificationEndpoints.SetReadStateAsync(
            NotificationIdentifier,
            new ClientNotificationReadStateRequest(true),
            SignedIn(notifications),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<ClientNotificationReadStateResponse>>(result.Result).Value!;

        Assert.Equal(NotificationIdentifier, answered.Id);
        Assert.True(answered.Read);
        Assert.Equal(2, answered.UnreadCount);
    }

    /// <summary>The refusal is the same one an absent notification gets, so nothing here reports whose notifications exist.</summary>
    [Fact]
    public async Task SetReadStateAsync_ANotificationSomebodyElseHolds_AnswersAsOneThatDoesNotExist()
    {
        // Arrange
        var notifications = Substitute.For<INotificationStore>();
        notifications.SetReadAsync(
                Arg.Any<MailUserId>(),
                Arg.Any<NotificationId>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .Returns(NotificationReadOutcome.NotFound);

        // Act
        var result = await ClientNotificationEndpoints.SetReadStateAsync(
            NotificationIdentifier,
            new ClientNotificationReadStateRequest(true),
            SignedIn(notifications),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>An identifier addressing nothing is answered without asking the store, because no notification is ever stored under it.</summary>
    [Fact]
    public async Task SetReadStateAsync_TheEmptyIdentifier_AnswersAsOneThatDoesNotExistWithoutReachingTheStore()
    {
        // Arrange
        var notifications = Substitute.For<INotificationStore>();

        // Act
        var result = await ClientNotificationEndpoints.SetReadStateAsync(
            Guid.Empty,
            new ClientNotificationReadStateRequest(false),
            SignedIn(notifications),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
        await notifications.DidNotReceive().SetReadAsync(
            Arg.Any<MailUserId>(),
            Arg.Any<NotificationId>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The deduplication rule holds one unread notification per condition, so the caller is told why the row stays read rather than being told it moved.</summary>
    [Fact]
    public async Task SetReadStateAsync_AConditionThatAlreadyStandsUnread_ReportsTheConflict()
    {
        // Arrange
        var notifications = Substitute.For<INotificationStore>();
        notifications.SetReadAsync(
                Arg.Any<MailUserId>(),
                Arg.Any<NotificationId>(),
                false,
                Arg.Any<CancellationToken>())
            .Returns(NotificationReadOutcome.ConditionAlreadyStanding);

        // Act
        var result = await ClientNotificationEndpoints.SetReadStateAsync(
            NotificationIdentifier,
            new ClientNotificationReadStateRequest(false),
            SignedIn(notifications),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status409Conflict, refusal.StatusCode);
    }

    [Fact]
    public async Task MarkAllReadAsync_APersonWithUnreadNotifications_AnswersHowManyMovedAndLeavesNoneUnread()
    {
        // Arrange
        var notifications = Substitute.For<INotificationStore>();
        notifications.MarkAllReadAsync(User, Arg.Any<CancellationToken>()).Returns(3);

        // Act
        var result = await ClientNotificationEndpoints.MarkAllReadAsync(
            SignedIn(notifications),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, result.Value!.MarkedRead);
        Assert.Equal(0, result.Value.UnreadCount);
    }

    [Fact]
    public async Task EraseAsync_NotificationsThePersonHolds_AnswersHowManyWentAndTheCountThatLeaves()
    {
        // Arrange
        var notifications = Substitute.For<INotificationStore>();
        notifications.EraseAsync(
                User,
                Arg.Any<IReadOnlyCollection<NotificationId>>(),
                Arg.Any<CancellationToken>())
            .Returns(2);
        notifications.CountUnreadAsync(User, Arg.Any<CancellationToken>()).Returns(4);

        // Act
        var result = await ClientNotificationEndpoints.EraseAsync(
            new ClientNotificationDeletionRequest([NotificationIdentifier, SecondNotificationIdentifier]),
            SignedIn(notifications),
            TestContext.Current.CancellationToken);

        // Assert
        var erased = Assert.IsType<Ok<ClientDeletedNotificationsResponse>>(result.Result);

        Assert.Equal(2, erased.Value!.Deleted);
        Assert.Equal(4, erased.Value.UnreadCount);
    }

    /// <summary>The one act here that cannot be taken back is refused above the ceiling rather than served in part, unlike the page size two routes above.</summary>
    [Fact]
    public async Task EraseAsync_MoreIdentifiersThanOnePageCouldHold_IsRefusedRatherThanTruncated()
    {
        // Arrange
        var notifications = Substitute.For<INotificationStore>();

        // Act
        var result = await ClientNotificationEndpoints.EraseAsync(
            new ClientNotificationDeletionRequest(
                [.. Enumerable.Range(0, OwnNotifications.MaximumErasedAtOnce + 1).Select(_ => Guid.NewGuid())]),
            SignedIn(notifications),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        await notifications.DidNotReceive().EraseAsync(
            Arg.Any<MailUserId>(),
            Arg.Any<IReadOnlyCollection<NotificationId>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An identifier addressing nothing is dropped rather than failing the request, because a caller that sent it named nothing.</summary>
    [Fact]
    public async Task EraseAsync_TheEmptyIdentifier_NamesNothingRatherThanFailingTheRequest()
    {
        // Arrange
        var notifications = Substitute.For<INotificationStore>();
        IReadOnlyCollection<NotificationId>? named = null;
        notifications.EraseAsync(User, Arg.Any<IReadOnlyCollection<NotificationId>>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                named = call.Arg<IReadOnlyCollection<NotificationId>>();

                return 1;
            });

        // Act
        var result = await ClientNotificationEndpoints.EraseAsync(
            new ClientNotificationDeletionRequest([Guid.Empty, NotificationIdentifier]),
            SignedIn(notifications),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<Ok<ClientDeletedNotificationsResponse>>(result.Result);
        Assert.Equal([NotificationId.Create(NotificationIdentifier)], named);
    }

    /// <summary>A body naming no notification is an erasure of nothing rather than a refusal, which is what an absent list means.</summary>
    [Fact]
    public async Task EraseAsync_ABodyNamingNoNotification_ErasesNothingAndStillAnswersTheCount()
    {
        // Arrange
        var notifications = Substitute.For<INotificationStore>();
        notifications.CountUnreadAsync(User, Arg.Any<CancellationToken>()).Returns(7);

        // Act
        var result = await ClientNotificationEndpoints.EraseAsync(
            new ClientNotificationDeletionRequest(NotificationIds: null),
            SignedIn(notifications),
            TestContext.Current.CancellationToken);

        // Assert
        var erased = Assert.IsType<Ok<ClientDeletedNotificationsResponse>>(result.Result);

        Assert.Equal(0, erased.Value!.Deleted);
        Assert.Equal(7, erased.Value.UnreadCount);
    }

    /// <summary>The strict binding, which is what keeps a mistyped request from being read as the opposite of what it stated.</summary>
    [Fact]
    public void Deserialize_ABodyCarryingAKeyNothingBinds_IsRefused()
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientNotificationReadStateRequest>(
            """{"read":true,"dismissed":true}""",
            WebFormat));
    }

    /// <summary>The same strict binding on the act that cannot be taken back, where a key nothing binds is the difference between a refusal and erasing a set nobody named.</summary>
    [Fact]
    public void Deserialize_ADeletionCarryingAKeyNothingBinds_IsRefused()
    {
        // Assert
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ClientNotificationDeletionRequest>(
            """{"notificationIds":[],"all":true}""",
            WebFormat));
    }

    /// <summary>How the transport reads a body, so the binding this asserts is the one a request actually meets.</summary>
    private static JsonSerializerOptions WebFormat { get; } = new(JsonSerializerDefaults.Web);

    private static OwnNotifications SignedIn(INotificationStore store) => new(
        AccessAuthorizations.ForUserGranted(User, MailFathomPermission.MailRead),
        store);

    private static Notification MailNotification(NotificationStatement? statement) => Notification.Compose(
        NotificationId.Create(NotificationIdentifier),
        User,
        NotificationKind.Mail,
        title: "New mail arrived",
        body: "Two messages arrived in your inbox.",
        statement,
        source: "work",
        NotificationTarget.ToMessage(StoredEmailId.Create(new Guid("8a1b2c3d-4e5f-4061-8273-849506a7b8c9"))),
        NotificationDeduplicationKey.Create("mail-arrived"),
        OccurredAt);
}
