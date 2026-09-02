// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.SensitiveContent.PersonalData;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.SensitiveContent.PersonalData;

/// <summary>Covers the translation between the analyzer's code-point offsets and .NET's UTF-16 offsets.</summary>
/// <remarks>
/// This is the one part of the adapter whose failure is invisible. A span shifted by the surrogate pairs preceding it still
/// describes a valid region, so redaction would replace characters nothing was found in and leave part of the value that was
/// found — and no bound anywhere downstream can tell the difference. Every case below is a text that would pass a naive
/// mapping and a text that would not.
/// </remarks>
public sealed class CodePointOffsetsTests
{
    [Fact]
    public void TryTranslate_TextOfBasicPlaneCharacters_LeavesOffsetsAlone()
    {
        // Arrange
        var offsets = CodePointOffsets.For("Card 4111111111111111 expires soon");

        // Act
        var translated = offsets.TryTranslate(5, 21, out var span);

        // Assert
        Assert.True(translated);
        Assert.Equal(5, span.Start);
        Assert.Equal(16, span.Length);
    }

    /// <summary>
    /// The emoji is one position to the analyzer and two to .NET, so the region after it sits two code units further along
    /// than the offsets say.
    /// </summary>
    [Fact]
    public void TryTranslate_SurrogatePairBeforeTheFinding_ShiftsTheSpanPastIt()
    {
        // Arrange
        const string text = "\U0001F4E7 4111111111111111";
        var offsets = CodePointOffsets.For(text);

        // Act
        var translated = offsets.TryTranslate(2, 18, out var span);

        // Assert
        Assert.True(translated);
        Assert.Equal(3, span.Start);
        Assert.Equal(16, span.Length);
        Assert.Equal("4111111111111111", text.Substring(span.Start, span.Length));
    }

    /// <summary>A finding that itself spans characters outside the basic plane covers twice as many code units as code points.</summary>
    [Fact]
    public void TryTranslate_FindingCoveringSurrogatePairs_CoversEveryCodeUnitOfThem()
    {
        // Arrange
        const string text = "id \U0001F600\U0001F601 end";
        var offsets = CodePointOffsets.For(text);

        // Act
        var translated = offsets.TryTranslate(3, 5, out var span);

        // Assert
        Assert.True(translated);
        Assert.Equal("\U0001F600\U0001F601", text.Substring(span.Start, span.Length));
    }

    /// <summary>An entity reaching the end of the text names the position just past the last code point.</summary>
    [Fact]
    public void TryTranslate_RegionEndingAtTheEndOfTheText_IsTranslated()
    {
        // Arrange
        const string text = "\U0001F4E7 PL60102010260000042270201111";
        var offsets = CodePointOffsets.For(text);

        // Act
        var translated = offsets.TryTranslate(2, 30, out var span);

        // Assert
        Assert.True(translated);
        Assert.Equal(text.Length, span.End);
    }

    [Theory]
    [InlineData(-1, 4)]
    [InlineData(0, 0)]
    [InlineData(4, 2)]
    [InlineData(0, 99)]
    public void TryTranslate_PairThatDescribesNoRegionOfTheText_IsRefused(int start, int end)
    {
        // Arrange
        var offsets = CodePointOffsets.For("short text");

        // Act
        var translated = offsets.TryTranslate(start, end, out var span);

        // Assert
        Assert.False(translated);
        Assert.False(span.IsSpecified);
    }

    /// <summary>A lone surrogate is one position on both sides, so it must not shift anything after it.</summary>
    [Fact]
    public void TryTranslate_UnpairedSurrogate_CountsAsOnePosition()
    {
        // Arrange
        var offsets = CodePointOffsets.For("\uD83D abc");

        // Act
        var translated = offsets.TryTranslate(2, 5, out var span);

        // Assert
        Assert.True(translated);
        Assert.Equal(2, span.Start);
        Assert.Equal(3, span.Length);
    }
}
