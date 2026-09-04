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
    private static readonly MailOwnerId Owner = MailOwnerId.Create(Guid.NewGuid());

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
        Assert.Equal(Owner.Value, entity.OwnerId);
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

    private static Notification Compose(NotificationTarget target, string? source = "work") =>
        Notification.Compose(
            NotificationId.Create(Guid.CreateVersion7(OccurredAt)),
            Owner,
            NotificationKind.System,
            title: "Something happened",
            body: "Something happened that nobody was at the screen for.",
            source,
            target,
            NotificationDeduplicationKey.Create("something-happened:work"),
            OccurredAt);
}
