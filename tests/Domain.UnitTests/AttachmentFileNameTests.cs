// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests;

public sealed class AttachmentFileNameTests
{
    /// <summary>A name a sender simply wrote must survive untouched and must not be reported as repaired.</summary>
    [Theory]
    [InlineData("invoice.pdf")]
    [InlineData(".gitignore")]
    [InlineData("faktura wrzesień 2026.pdf")]
    [InlineData("報告書.xlsx")]
    public void TryNormalize_OrdinaryName_KeepsItAndReportsNoRepair(string decodedFileName)
    {
        // Act
        var normalized = AttachmentFileName.TryNormalize(decodedFileName, out var fileName);

        // Assert
        Assert.True(normalized);
        Assert.Equal(decodedFileName, fileName.Value);
        Assert.False(fileName.WasNormalized);
    }

    /// <summary>A name may never be a location, whichever platform's separators the sender used.</summary>
    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\windows\\system32\\cmd.exe", "cmd.exe")]
    [InlineData("/absolute/path/report.pdf", "report.pdf")]
    [InlineData("C:\\Users\\anna\\report.pdf", "report.pdf")]
    public void TryNormalize_NameCarryingPathStructure_KeepsOnlyTheNameAtItsEnd(
        string decodedFileName,
        string expectedFileName)
    {
        // Act
        var normalized = AttachmentFileName.TryNormalize(decodedFileName, out var fileName);

        // Assert
        Assert.True(normalized);
        Assert.Equal(expectedFileName, fileName.Value);
        Assert.True(fileName.WasNormalized);
    }

    /// <summary>Control characters would let a file name break the line whatever writes it is writing.</summary>
    [Fact]
    public void TryNormalize_NameCarryingControlCharacters_RemovesThem()
    {
        // Act
        var normalized = AttachmentFileName.TryNormalize("inv\r\noice\t.pdf", out var fileName);

        // Assert
        Assert.True(normalized);
        Assert.Equal("invoice.pdf", fileName.Value);
        Assert.True(fileName.WasNormalized);
    }

    /// <summary>A bidirectional override makes a name render as something other than what it is, so it is removed rather than kept.</summary>
    [Fact]
    public void TryNormalize_NameCarryingBidirectionalOverride_RemovesIt()
    {
        // Arrange
        const string disguisedExecutable = "invoice\u202Egnp.exe";

        // Act
        var normalized = AttachmentFileName.TryNormalize(disguisedExecutable, out var fileName);

        // Assert
        Assert.True(normalized);
        Assert.Equal("invoicegnp.exe", fileName.Value);
        Assert.True(fileName.WasNormalized);
    }

    /// <summary>A formatting character outside the Basic Multilingual Plane is as invisible as U+202E and is removed too.</summary>
    [Fact]
    public void TryNormalize_NameCarryingSupplementaryPlaneFormattingCharacter_RemovesIt()
    {
        // Arrange
        const string taggedName = "invoice\U000E0001\U000E0074.pdf";

        // Act
        var normalized = AttachmentFileName.TryNormalize(taggedName, out var fileName);

        // Assert
        Assert.True(normalized);
        Assert.Equal("invoice.pdf", fileName.Value);
        Assert.True(fileName.WasNormalized);
    }

    /// <summary>An unbounded name is bounded, because nothing downstream should have to guess how long one can be.</summary>
    [Fact]
    public void TryNormalize_OverLongName_BoundsItsLength()
    {
        // Arrange
        var overLongFileName = new string('a', AttachmentFileName.MaxLength + 50) + ".pdf";

        // Act
        var normalized = AttachmentFileName.TryNormalize(overLongFileName, out var fileName);

        // Assert
        Assert.True(normalized);
        Assert.Equal(AttachmentFileName.MaxLength, fileName.Value.Length);
        Assert.True(fileName.WasNormalized);
    }

    /// <summary>The cut may never land inside a character, or the name stops being a string every consumer can carry.</summary>
    [Fact]
    public void TryNormalize_OverLongNameEndingInASurrogatePair_CutsBetweenCharactersRatherThanInsideOne()
    {
        // Arrange: the emoji straddles the bound, so a cut by UTF-16 code units would keep its high surrogate alone.
        var overLongFileName = new string('a', AttachmentFileName.MaxLength - 1) + "\U0001F4C4report.pdf";

        // Act
        var normalized = AttachmentFileName.TryNormalize(overLongFileName, out var fileName);

        // Assert
        Assert.True(normalized);
        Assert.Equal(AttachmentFileName.MaxLength - 1, fileName.Value.Length);
        Assert.DoesNotContain(fileName.Value, char.IsSurrogate);
    }

    /// <summary>A combining sequence is one character to a reader, so the bound keeps it whole too.</summary>
    [Fact]
    public void TryNormalize_OverLongNameEndingInACombiningSequence_KeepsTheSequenceWhole()
    {
        // Arrange: "e" plus a combining acute accent renders as one character and must not be split across the bound.
        var overLongFileName = new string('a', AttachmentFileName.MaxLength - 1) + "éreport.pdf";

        // Act
        AttachmentFileName.TryNormalize(overLongFileName, out var fileName);

        // Assert
        Assert.Equal(AttachmentFileName.MaxLength - 1, fileName.Value.Length);
        Assert.EndsWith("a", fileName.Value, StringComparison.Ordinal);
    }

    /// <summary>A part left with nothing usable is unnamed rather than given a name nobody wrote.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("..")]
    [InlineData("../")]
    [InlineData("\u202E\u200B")]
    public void TryNormalize_NameWithNothingUsableLeft_ReportsNoName(string? decodedFileName)
    {
        // Act
        var normalized = AttachmentFileName.TryNormalize(decodedFileName, out var fileName);

        // Assert
        Assert.False(normalized);
        Assert.Equal(default, fileName);
    }
}
