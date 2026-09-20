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
    [InlineData("Design\u202Ereview")]
    [InlineData("Design\u200Breview")]
    [InlineData("Design\u2028review")]
    [InlineData("Design\u2029review")]
    public void Create_ACharacterThatCarriesNoGlyph_IsRefused(string value)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => CalendarEventTitle.Create(value));
    }

    /// <summary>
    /// A formatting character outside the Basic Multilingual Plane is a surrogate pair whose halves categorize as
    /// <c>Surrogate</c> rather than as <c>Format</c>, so a title is judged as Unicode scalars: the language tag
    /// character here is exactly the invisible one a per-character test would keep.
    /// </summary>
    [Fact]
    public void Create_AFormattingCharacterOutsideTheBasicPlane_IsRefused()
    {
        // Arrange
        var tagged = "Design" + char.ConvertFromUtf32(0xE0001) + "review";

        // Act, Assert
        Assert.Throws<ArgumentException>(() => CalendarEventTitle.Create(tagged));
    }

    /// <summary>
    /// An unpaired surrogate is not a character at all, and enumerating scalars substitutes a printable replacement
    /// symbol for it — so it is refused before that, rather than reaching the database as text an encoder rejects.
    /// </summary>
    [Fact]
    public void Create_AnUnpairedSurrogate_IsRefused()
    {
        // Arrange
        var illFormed = "Design" + (char)0xD800 + "review";

        // Act, Assert
        Assert.Throws<ArgumentException>(() => CalendarEventTitle.Create(illFormed));
    }

    /// <summary>The two joiners shape neighbouring letters inside words people write, so refusing them would refuse those titles.</summary>
    [Theory]
    [InlineData("\u0644\u0627\u200C\u0642\u0627\u0621")]
    [InlineData("\u0915\u094D\u200D\u0937")]
    public void Create_AJoinerWrittenInsideAWord_IsAccepted(string value)
    {
        // Act
        var title = CalendarEventTitle.Create(value);

        // Assert
        Assert.Equal(value, title.Value);
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
