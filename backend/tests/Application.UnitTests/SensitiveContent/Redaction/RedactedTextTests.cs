// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent.Redaction;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent.Redaction;

/// <summary>Covers where an offset recorded against the analyzed text ends up once the placeholders are in place.</summary>
public sealed class RedactedTextTests
{
    /// <summary>With nothing replaced the redaction is the identity, so an offset is its own answer.</summary>
    [Fact]
    public void MapOffset_ARedactionThatReplacedNothing_AnswersWithTheOffsetItself()
    {
        // Arrange
        var redacted = RedactedText.Create("page one page two", [], omittedCharacterCount: 0);

        // Act, Assert
        Assert.Equal(9, redacted.MapOffset(9));
    }

    /// <summary>Everything before the first placeholder sits where it always did.</summary>
    [Fact]
    public void MapOffset_AnOffsetBeforeEveryPlaceholder_IsLeftWhereItWas()
    {
        // Arrange
        var redacted = RedactedText.Create(
            "page one [redacted:email]",
            [],
            omittedCharacterCount: 0,
            [new RedactedPlacement(9, 6, 16)]);

        // Act, Assert
        Assert.Equal(5, redacted.MapOffset(5));
    }

    /// <summary>
    /// An offset after a substitution has moved by the net length every earlier placeholder added or removed, which is
    /// the whole reason this exists: a comparison of the two lengths cannot recover it.
    /// </summary>
    [Theory]
    [InlineData(6, 16)]
    [InlineData(20, 16)]
    public void MapOffset_AnOffsetAfterASubstitution_MovesByTheNetLengthItApplied(int replaced, int placeholderLength)
    {
        // Arrange
        var redacted = RedactedText.Create(
            new string('x', placeholderLength) + " tail",
            [],
            omittedCharacterCount: 0,
            [new RedactedPlacement(0, replaced, placeholderLength)]);

        // Act, Assert
        Assert.Equal(placeholderLength + 1, redacted.MapOffset(replaced + 1));
    }

    /// <summary>
    /// Placements compound, so an offset past two substitutions of different lengths moves by both together rather
    /// than by the nearest one.
    /// </summary>
    [Fact]
    public void MapOffset_AnOffsetPastSeveralSubstitutions_MovesByEveryNetLengthTogether()
    {
        // Arrange
        var redacted = RedactedText.Create(
            "ab[redacted:CloudKey]ghij[redacted:PersonName]qrstuvwxyz",
            [],
            omittedCharacterCount: 0,
            [new RedactedPlacement(2, 4, 19), new RedactedPlacement(10, 6, 21)]);

        // Act
        var mapped = redacted.MapOffset(16);

        // Assert
        Assert.Equal(46, mapped);
        Assert.Equal('q', redacted.Text[mapped!.Value]);
    }

    /// <summary>The characters an offset pointed at are gone, so it answers with the placeholder standing in for them.</summary>
    [Fact]
    public void MapOffset_AnOffsetInsideAReplacedRegion_AnswersWithThePlaceholderThatStandsThere()
    {
        // Arrange
        var redacted = RedactedText.Create(
            "page one [redacted:email]",
            [],
            omittedCharacterCount: 0,
            [new RedactedPlacement(9, 6, 16)]);

        // Act, Assert
        Assert.Equal(9, redacted.MapOffset(12));
    }

    /// <summary>Text the ceiling dropped is not in the result at all, so an offset into it resolves to no place.</summary>
    [Fact]
    public void MapOffset_AnOffsetPastWhatTheCeilingAdmitted_AnswersWithNothing()
    {
        // Arrange
        var redacted = RedactedText.Create("page one", [], omittedCharacterCount: 9);

        // Act, Assert
        Assert.Null(redacted.MapOffset(9));
    }

    /// <summary>An offset is a position in a text, and there is no such position before its start.</summary>
    [Fact]
    public void MapOffset_ANegativeOffset_IsRefused()
    {
        // Arrange
        var redacted = RedactedText.Create("page one", [], omittedCharacterCount: 0);

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => redacted.MapOffset(-1));
    }
}
