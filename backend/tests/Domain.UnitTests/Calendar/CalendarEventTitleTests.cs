// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Calendar;
using Xunit;

namespace MailFathom.Domain.UnitTests.Calendar;

/// <summary>Covers what a title is trimmed to and which characters it refuses, whoever supplied it.</summary>
public sealed class CalendarEventTitleTests
{
    [Fact]
    public void Create_SurroundingWhitespace_IsTrimmedAndTheWordsAreKept()
    {
        // Act
        var title = CalendarEventTitle.Create("  Design review  ");

        // Assert
        Assert.Equal("Design review", title.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NothingToRead_IsRefused(string? value)
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => CalendarEventTitle.Create(value));

        // Assert
        Assert.Equal("value", refusal.ParamName);
    }

    [Fact]
    public void Create_LongerThanTheBound_IsRefused()
    {
        // Arrange
        var overlong = new string('a', CalendarEventTitle.MaximumLength + 1);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => CalendarEventTitle.Create(overlong));
    }

    /// <summary>
    /// A title is drawn in a list beside the other events and read back in an answer, so a line break would end the
    /// row it is on and a bidirectional override would render the rest of it as text the record does not contain.
    /// </summary>
    [Theory]
    [InlineData("Design\nreview")]
    [InlineData("Design\treview")]
    [InlineData("Design‮review")]
    [InlineData("Design​review")]
    public void Create_ACharacterThatCarriesNoGlyph_IsRefused(string value)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => CalendarEventTitle.Create(value));
    }

    [Fact]
    public void TryCreate_SomethingThatIsNotATitle_AnswersWithTheUnspecifiedValue()
    {
        // Act
        var read = CalendarEventTitle.TryCreate("  ", out var title);

        // Assert
        Assert.False(read);
        Assert.False(title.IsSpecified);
    }
}
