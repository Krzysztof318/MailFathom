// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Notifications;
using Xunit;

namespace MailFathom.Domain.UnitTests.Notifications;

/// <summary>Covers what a notification refuses to be composed as, and the three shapes its target takes.</summary>
public sealed class NotificationTests
{
    private static readonly MailOwnerId Owner = MailOwnerId.Create(Guid.NewGuid());

    private static readonly DateTimeOffset OccurredAt = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A row written under the unspecified identity would belong to nobody, so it is refused at composition.</summary>
    [Fact]
    public void Compose_UnspecifiedOwner_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => Compose(owner: default(MailOwnerId)));

        // Assert
        Assert.Equal("owner", refusal.ParamName);
    }

    /// <summary>A kind outside the declared set would be stored as text nothing can draw a row from.</summary>
    [Fact]
    public void Compose_UndeclaredKind_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => Compose(kind: (NotificationKind)99));

        // Assert
        Assert.Equal("kind", refusal.ParamName);
    }

    /// <summary>Both value types are structs, so the one value their private constructors cannot reach is refused here.</summary>
    [Theory]
    [InlineData("id")]
    [InlineData("deduplicationKey")]
    public void Compose_StructDefaultInPlaceOfAValidatedValue_IsRefused(string parameterName)
    {
        // Arrange
        var id = parameterName == "id"
            ? default
            : NotificationId.Create(Guid.CreateVersion7(OccurredAt));
        var deduplicationKey = parameterName == "deduplicationKey"
            ? default
            : NotificationDeduplicationKey.Create("something-happened:work");

        // Act
        var refusal = Assert.Throws<ArgumentException>(() => Notification.Compose(
            id,
            Owner,
            NotificationKind.System,
            title: "Something happened",
            body: "Something happened that nobody was at the screen for.",
            source: "work",
            NotificationTarget.Nothing,
            deduplicationKey,
            OccurredAt));

        // Assert
        Assert.Equal(parameterName, refusal.ParamName);
    }

    /// <summary>The bounds are the column's, so text past one is refused here rather than by the database.</summary>
    [Fact]
    public void Compose_TitleLongerThanTheBound_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => Compose(title: new string('a', Notification.MaximumTitleLength + 1)));

        // Assert
        Assert.Equal("title", refusal.ParamName);
    }

    /// <summary>A notification is unread when it is composed, which is what makes the deduplication rule mean anything.</summary>
    [Fact]
    public void Compose_AnyNotification_IsUnread()
    {
        // Act
        var notification = Compose();

        // Assert
        Assert.False(notification.IsRead);
    }

    /// <summary>The kind is the whole source line where nothing narrows it further, which is what absence means here.</summary>
    [Fact]
    public void Compose_NoSource_LeavesTheKindAsTheWholeSourceLine()
    {
        // Act
        var notification = Compose(source: null);

        // Assert
        Assert.Null(notification.Source);
    }

    /// <summary>A message target is the one shape that ties a notification to mail, and it names nothing else.</summary>
    [Fact]
    public void ToMessage_AnyMessage_NamesTheMessageAndNoScreen()
    {
        // Arrange
        var message = StoredEmailId.Create(Guid.NewGuid());

        // Act
        var target = NotificationTarget.ToMessage(message);

        // Assert
        Assert.Equal(NotificationTargetKind.Message, target.Kind);
        Assert.Equal(message, target.Message);
        Assert.Null(target.Screen);
    }

    /// <summary>A screen the client may not have is a promise nothing can keep, so an undeclared one is refused.</summary>
    [Fact]
    public void ToScreen_UndeclaredScreen_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => NotificationTarget.ToScreen((NotificationScreen)99));

        // Assert
        Assert.Equal("screen", refusal.ParamName);
    }

    /// <summary>A statement with nowhere to go names neither of the other two shapes.</summary>
    [Fact]
    public void Nothing_TheEmptyTarget_NamesNeitherAMessageNorAScreen()
    {
        // Assert
        Assert.Equal(NotificationTargetKind.Nothing, NotificationTarget.Nothing.Kind);
        Assert.Null(NotificationTarget.Nothing.Message);
        Assert.Null(NotificationTarget.Nothing.Screen);
    }

    private static Notification Compose(
        MailOwnerId? owner = null,
        NotificationKind kind = NotificationKind.System,
        string title = "Something happened",
        string? source = "work") =>
        Notification.Compose(
            NotificationId.Create(Guid.CreateVersion7(OccurredAt)),
            owner ?? Owner,
            kind,
            title,
            body: "Something happened that nobody was at the screen for.",
            source,
            NotificationTarget.Nothing,
            NotificationDeduplicationKey.Create("something-happened:work"),
            OccurredAt);
    /// <summary>The read state is the one thing a store says and a producer never does, which is why restoring takes it and composing does not.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Restore_ARowThisDeploymentAlreadyKept_CarriesTheReadStateItWasStoredUnder(bool isRead)
    {
        // Act
        var notification = Notification.Restore(
            NotificationId.Create(Guid.CreateVersion7()),
            Owner,
            NotificationKind.System,
            title: "Something happened",
            body: "Something happened that nobody was at the screen for.",
            source: null,
            NotificationTarget.Nothing,
            NotificationDeduplicationKey.Create("something-happened"),
            new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero),
            isRead);

        // Assert
        Assert.Equal(isRead, notification.IsRead);
    }

    /// <summary>A row read back is input from outside this process however it got there, so restoring validates what composing validates.</summary>
    [Fact]
    public void Restore_ARowWhoseOwnerNamesNobody_IsRefusedExactlyAsComposingOneIs()
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() => Notification.Restore(
            NotificationId.Create(Guid.CreateVersion7()),
            default,
            NotificationKind.System,
            title: "Something happened",
            body: "Something happened that nobody was at the screen for.",
            source: null,
            NotificationTarget.Nothing,
            NotificationDeduplicationKey.Create("something-happened"),
            new DateTimeOffset(2026, 9, 3, 8, 0, 0, TimeSpan.Zero),
            isRead: false));
    }

}
