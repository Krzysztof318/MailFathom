// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Notifications;
using Xunit;

namespace MailFathom.Domain.UnitTests.Notifications;

/// <summary>Covers how a key stays inside its bound when the half nobody bounds is longer than the whole.</summary>
public sealed class NotificationDeduplicationKeyTests
{
    /// <summary>The ordinary key is the readable one, because that is what a person debugging a record reads.</summary>
    [Fact]
    public void For_ASubjectThatFits_KeepsTheConditionAndTheSubjectAsWritten()
    {
        // Act
        var key = NotificationDeduplicationKey.For("credential-refused", "work");

        // Assert
        Assert.Equal("credential-refused:work", key.Value);
    }

    /// <summary>An account identifier is the operator's own text and nothing bounds it, so the key bounds it here.</summary>
    [Fact]
    public void For_ASubjectPastTheBound_StaysWithinTheBound()
    {
        // Act
        var key = NotificationDeduplicationKey.For("credential-refused", new string('w', 400));

        // Assert
        Assert.True(key.Value.Length <= NotificationDeduplicationKey.MaximumLength);
        Assert.StartsWith("credential-refused:", key.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two outsized subjects sharing a prefix are reduced apart rather than together: truncation would collapse two
    /// accounts into one condition, and each would then silence the other's statements.
    /// </summary>
    [Fact]
    public void For_TwoOutsizedSubjectsSharingAPrefix_ProducesTwoKeys()
    {
        // Arrange
        var prefix = new string('w', 400);

        // Act
        var first = NotificationDeduplicationKey.For("credential-refused", prefix + "a");
        var second = NotificationDeduplicationKey.For("credential-refused", prefix + "b");

        // Assert
        Assert.NotEqual(first, second);
    }

    /// <summary>The reduction is of the subject itself, so one account's condition is named the same way every run.</summary>
    [Fact]
    public void For_TheSameOutsizedSubjectTwice_ProducesTheSameKey()
    {
        // Arrange
        var subject = new string('w', 400);

        // Act
        var first = NotificationDeduplicationKey.For("credential-refused", subject);
        var second = NotificationDeduplicationKey.For("credential-refused", subject);

        // Assert
        Assert.Equal(first, second);
    }

    /// <summary>A condition long enough to leave no room for a reduced subject is MailFathom's own defect, not an operator's.</summary>
    [Fact]
    public void For_AConditionWithNoRoomForAReducedSubject_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => NotificationDeduplicationKey.For(new string('c', 250), "work"));

        // Assert
        Assert.Equal("condition", refusal.ParamName);
    }

    /// <summary>Neither half names a condition on its own, so a blank one is refused rather than composed around.</summary>
    [Theory]
    [InlineData("", "work")]
    [InlineData("credential-refused", "  ")]
    public void For_ABlankHalf_IsRefused(string condition, string subject)
    {
        // Act and assert
        Assert.Throws<ArgumentException>(() => NotificationDeduplicationKey.For(condition, subject));
    }
}
