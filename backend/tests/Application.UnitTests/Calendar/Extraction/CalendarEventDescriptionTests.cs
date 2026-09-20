// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Calendar.Extraction;
using Xunit;

namespace MailFathom.Application.UnitTests.Calendar.Extraction;

/// <summary>Covers what counts as a description, and what the instant beside it keeps.</summary>
public sealed class CalendarEventDescriptionTests
{
    private static readonly DateTimeOffset WrittenAt = new(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2));

    [Fact]
    public void TryCreate_ASentence_KeepsItTrimmedBesideTheInstantItWasTypedAt()
    {
        // Act
        var read = CalendarEventDescription.TryCreate("  lunch tomorrow at one  ", WrittenAt, out var description);

        // Assert
        Assert.True(read);
        Assert.NotNull(description);
        Assert.Equal("lunch tomorrow at one", description.Text);
        Assert.Equal(WrittenAt, description.WrittenAt);
    }

    /// <summary>The offset travels with the instant, because it is what turns an hour into the person's own hour.</summary>
    [Fact]
    public void TryCreate_AnInstantCarryingAnOffset_KeepsTheOffsetRatherThanNormalisingIt()
    {
        // Act
        CalendarEventDescription.TryCreate("lunch tomorrow", WrittenAt, out var description);

        // Assert
        Assert.NotNull(description);
        Assert.Equal(TimeSpan.FromHours(2), description.WrittenAt.Offset);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryCreate_NothingToRead_IsRefused(string? text)
    {
        // Act
        var read = CalendarEventDescription.TryCreate(text, WrittenAt, out var description);

        // Assert
        Assert.False(read);
        Assert.Null(description);
    }

    /// <summary>The field asks for a sentence, and what reads mail is the pass under the account's own posture.</summary>
    [Fact]
    public void TryCreate_TextLongerThanASentence_IsRefused()
    {
        // Arrange
        var tooLong = new string('a', CalendarEventDescription.MaximumTextLength + 1);

        // Act & Assert
        Assert.False(CalendarEventDescription.TryCreate(tooLong, WrittenAt, out _));
    }

    [Fact]
    public void TryCreate_TextExactlyAtTheBound_IsADescription()
    {
        // Arrange
        var atTheBound = new string('a', CalendarEventDescription.MaximumTextLength);

        // Act & Assert
        Assert.True(CalendarEventDescription.TryCreate(atTheBound, WrittenAt, out _));
    }
}
