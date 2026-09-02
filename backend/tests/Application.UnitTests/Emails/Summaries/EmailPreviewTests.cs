// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Summaries;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Summaries;

/// <summary>Covers the bound and the reflow a list row's preview is produced under.</summary>
public sealed class EmailPreviewTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n\t ")]
    public void Bounded_TextThatCarriesNoWords_IsAbsentRatherThanEmpty(string? text)
    {
        // Act
        var preview = EmailPreview.Bounded(text);

        // Assert
        Assert.Null(preview);
    }

    /// <summary>A wrapped quotation would otherwise arrive as a preview made of line breaks.</summary>
    [Fact]
    public void Bounded_TextBrokenAcrossLines_CollapsesEveryRunOfWhitespaceToOneSpace()
    {
        // Act
        var preview = EmailPreview.Bounded("  the release\r\n\r\nis   out\ttoday  ");

        // Assert
        Assert.Equal("the release is out today", preview);
    }

    /// <summary>The bound is what keeps a page of rows from being a page of bodies, so it holds whatever storage answered with.</summary>
    [Fact]
    public void Bounded_TextLongerThanTheBound_IsCutToIt()
    {
        // Arrange
        var body = new string('a', EmailPreview.MaximumCharacters + 50);

        // Act
        var preview = EmailPreview.Bounded(body);

        // Assert
        Assert.Equal(EmailPreview.MaximumCharacters, preview?.Length);
    }

    /// <summary>Collapsing shortens a preview and never lengthens one, which is what makes the bound a ceiling rather than a size.</summary>
    /// <remarks>
    /// The arrangement is longer than the bound before its whitespace is collapsed and shorter than it afterwards, so a
    /// preview that arrived cut would fail here while the test above still passed.
    /// </remarks>
    [Fact]
    public void Bounded_TextThatOnlyCollapsingBringsInsideTheBound_IsNotCutAtAll()
    {
        // Arrange
        const int wordCount = 40;
        var words = Enumerable.Repeat("word", wordCount).ToArray();
        var body = string.Join("  \r\n  ", words);

        // Act
        var preview = EmailPreview.Bounded(body);

        // Assert
        Assert.True(body.Length > EmailPreview.MaximumCharacters);
        Assert.Equal(string.Join(' ', words), preview);
    }

    /// <summary>
    /// The query cuts in codepoints, so the text this receives can hold more UTF-16 characters than the bound and the
    /// bound can fall between the two halves of one of them.
    /// </summary>
    [Fact]
    public void Bounded_TextWhoseBoundFallsInsideACharacter_CutsBeforeItRatherThanThroughIt()
    {
        // Arrange
        var opening = new string('a', EmailPreview.MaximumCharacters - 1);
        var body = $"{opening}\U0001F642tail";

        // Act
        var preview = EmailPreview.Bounded(body);

        // Assert
        Assert.Equal(opening, preview);
    }
}
