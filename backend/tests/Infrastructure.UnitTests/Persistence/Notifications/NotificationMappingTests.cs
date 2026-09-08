// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Notifications;
using MailFathom.Infrastructure.Persistence.Notifications;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Notifications;

/// <summary>Covers the row a notification is written as, and the three shapes its target flattens into.</summary>
public sealed class NotificationMappingTests
{
    private static readonly MailUserId User = MailUserId.Create(Guid.NewGuid());

    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Everything the notification stated reaches the row, because nothing else re-derives any of it.</summary>
    [Fact]
    public void ToEntity_ANotificationLeadingNowhere_KeepsEveryPartOfIt()
    {
        // Arrange
        var notification = Compose(NotificationTarget.Nothing);

        // Act
        var entity = NotificationMapping.ToEntity(notification);

        // Assert
        Assert.Equal(notification.Id.Value, entity.Id);
        Assert.Equal(User.Value, entity.UserId);
        Assert.Equal(NotificationKind.System, entity.Kind);
        Assert.Equal("Something happened", entity.Title);
        Assert.Equal("Something happened that nobody was at the screen for.", entity.Body);
        Assert.Equal("work", entity.Source);
        Assert.Equal("something-happened:work", entity.DeduplicationKey);
        Assert.Equal(OccurredAt, entity.OccurredAt);
        Assert.False(entity.IsRead);
    }

    /// <summary>A statement with nowhere to go carries neither of the two columns a target would fill.</summary>
    [Fact]
    public void ToEntity_ANotificationLeadingNowhere_FillsNeitherTargetColumn()
    {
        // Act
        var entity = NotificationMapping.ToEntity(Compose(NotificationTarget.Nothing));

        // Assert
        Assert.Equal(NotificationTargetKind.Nothing, entity.TargetKind);
        Assert.Null(entity.TargetStoredEmailId);
        Assert.Null(entity.TargetScreen);
    }

    /// <summary>The message column is the foreign key the erasure cascade runs along, so it carries the identifier itself.</summary>
    [Fact]
    public void ToEntity_ANotificationLeadingToAMessage_FillsTheMessageColumnAlone()
    {
        // Arrange
        var message = StoredEmailId.Create(Guid.NewGuid());

        // Act
        var entity = NotificationMapping.ToEntity(Compose(NotificationTarget.ToMessage(message)));

        // Assert
        Assert.Equal(NotificationTargetKind.Message, entity.TargetKind);
        Assert.Equal(message.Value, entity.TargetStoredEmailId);
        Assert.Null(entity.TargetScreen);
    }

    /// <summary>A screen is not a row anything joins on, so it fills the other column and leaves the key empty.</summary>
    [Fact]
    public void ToEntity_ANotificationLeadingToAScreen_FillsTheScreenColumnAlone()
    {
        // Act
        var entity = NotificationMapping.ToEntity(
            Compose(NotificationTarget.ToScreen(NotificationScreen.Settings)));

        // Assert
        Assert.Equal(NotificationTargetKind.Screen, entity.TargetKind);
        Assert.Null(entity.TargetStoredEmailId);
        Assert.Equal(NotificationScreen.Settings, entity.TargetScreen);
    }

    /// <summary>The kind is the whole source line where nothing narrows it, and the column says so by holding nothing.</summary>
    [Fact]
    public void ToEntity_ANotificationWithoutASource_LeavesTheSourceColumnEmpty()
    {
        // Act
        var entity = NotificationMapping.ToEntity(Compose(NotificationTarget.Nothing, source: null));

        // Assert
        Assert.Null(entity.Source);
    }

    /// <summary>A row read back is the notification it was written from, the read state included — which is the one field a store says and a producer never does.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToNotification_ARowThisDeploymentWrote_IsTheNotificationItWasWrittenFrom(bool isRead)
    {
        // Arrange
        var notification = Compose(NotificationTarget.Nothing);
        var entity = NotificationMapping.ToEntity(notification);
        entity.IsRead = isRead;

        // Act
        var restored = NotificationMapping.ToNotification(entity);

        // Assert
        Assert.Equal(notification.Id, restored.Id);
        Assert.Equal(notification.User, restored.User);
        Assert.Equal(notification.Kind, restored.Kind);
        Assert.Equal(notification.Title, restored.Title);
        Assert.Equal(notification.Body, restored.Body);
        Assert.Equal(notification.Source, restored.Source);
        Assert.Equal(notification.DeduplicationKey, restored.DeduplicationKey);
        Assert.Equal(notification.OccurredAt, restored.OccurredAt);
        Assert.Equal(isRead, restored.IsRead);
    }

    /// <summary>The three columns a target flattened into are read back as the one shape they came from.</summary>
    [Fact]
    public void ToNotification_ARowLeadingToAMessage_ReadsTheMessageBackAsItsTarget()
    {
        // Arrange
        var message = StoredEmailId.Create(Guid.NewGuid());
        var entity = NotificationMapping.ToEntity(Compose(NotificationTarget.ToMessage(message)));

        // Act
        var target = NotificationMapping.ToNotification(entity).Target;

        // Assert
        Assert.Equal(NotificationTargetKind.Message, target.Kind);
        Assert.Equal(message, target.Message);
        Assert.Null(target.Screen);
    }

    [Fact]
    public void ToNotification_ARowLeadingToAScreen_ReadsTheScreenBackAsItsTarget()
    {
        // Arrange
        var entity = NotificationMapping.ToEntity(
            Compose(NotificationTarget.ToScreen(NotificationScreen.Settings)));

        // Act
        var target = NotificationMapping.ToNotification(entity).Target;

        // Assert
        Assert.Equal(NotificationTargetKind.Screen, target.Kind);
        Assert.Null(target.Message);
        Assert.Equal(NotificationScreen.Settings, target.Screen);
    }

    /// <summary>A row naming a shape without the column that shape is carried in is refused rather than read as a target leading nowhere.</summary>
    [Fact]
    public void ToNotification_ARowNamingAShapeWithoutItsColumn_IsRefused()
    {
        // Arrange
        var entity = NotificationMapping.ToEntity(Compose(NotificationTarget.ToMessage(StoredEmailId.Create(Guid.NewGuid()))));
        entity.TargetStoredEmailId = null;

        // Act and assert
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationMapping.ToNotification(entity));
    }

    /// <summary>The screen column is refused on the same terms, because either target shape read back without its column would lead nowhere.</summary>
    [Fact]
    public void ToNotification_ARowNamingAScreenShapeWithoutItsColumn_IsRefused()
    {
        // Arrange
        var entity = NotificationMapping.ToEntity(Compose(NotificationTarget.ToScreen(NotificationScreen.Settings)));
        entity.TargetScreen = null;

        // Act and assert
        Assert.Throws<ArgumentOutOfRangeException>(() => NotificationMapping.ToNotification(entity));
    }

    /// <summary>The condition is what a client draws the row from, so it survives the round trip with its numbers.</summary>
    /// <remarks>
    /// Read back through the mapping rather than off the entity, because what a stored row costs is the columns being
    /// flattened out of one value and read into it again — a cause kept beside a count that was dropped would draw a
    /// sentence with a hole in it.
    /// </remarks>
    [Fact]
    public void ToNotification_ARowNamingACondition_KeepsThatConditionAndItsCounts()
    {
        // Arrange
        var notification = Compose(
            NotificationTarget.Nothing,
            statement: NotificationStatement.SynchronizationIncomplete(2, 5));

        // Act
        var restored = NotificationMapping.ToNotification(NotificationMapping.ToEntity(notification));

        // Assert
        Assert.Equal(notification.Statement, restored.Statement);
    }

    /// <summary>A row written before conditions were kept names none, and is drawn from what it does carry.</summary>
    [Fact]
    public void ToNotification_ARowWrittenBeforeConditionsWereKept_NamesNoneAndKeepsItsTwoLines()
    {
        // Arrange
        var entity = NotificationMapping.ToEntity(Compose(NotificationTarget.Nothing));

        // Act
        var restored = NotificationMapping.ToNotification(entity);

        // Assert
        Assert.Null(entity.Cause);
        Assert.Null(restored.Statement);
        Assert.Equal("Something happened", restored.Title);
    }

    private static Notification Compose(
        NotificationTarget target,
        string? source = "work",
        NotificationStatement? statement = null) =>
        Notification.Compose(
            NotificationId.Create(Guid.CreateVersion7(OccurredAt)),
            User,
            NotificationKind.System,
            title: "Something happened",
            body: "Something happened that nobody was at the screen for.",
            statement,
            source,
            target,
            NotificationDeduplicationKey.Create("something-happened:work"),
            OccurredAt);
}
