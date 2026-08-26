// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
    [Fact]
    public void Bounded_TextAtTheBoundCarryingWhitespace_StaysInsideIt()
    {
        // Arrange
        var body = string.Join("\n", Enumerable.Repeat("word", EmailPreview.MaximumCharacters / 2));

        // Act
        var preview = EmailPreview.Bounded(body);

        // Assert
        Assert.NotNull(preview);
        Assert.True(preview.Length <= EmailPreview.MaximumCharacters);
    }
}
