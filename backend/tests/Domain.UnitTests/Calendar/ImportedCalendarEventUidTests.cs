// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Calendar;
using Xunit;

namespace MailFathom.Domain.UnitTests.Calendar;

/// <summary>Covers what an imported identifier admits out of a file, and the equality a repeated import is recognized by.</summary>
public sealed class ImportedCalendarEventUidTests
{
    [Fact]
    public void Create_SurroundingWhitespace_IsTrimmedAndTheIdentifierIsKeptAsWritten()
    {
        // Act
        var uid = ImportedCalendarEventUid.Create(" 19960401T080045Z-4000F192713@example.test ");

        // Assert
        Assert.Equal("19960401T080045Z-4000F192713@example.test", uid.Value);
    }

    /// <summary>
    /// RFC 5545 makes the value opaque and its only property equality, so two spellings are two identifiers to
    /// everybody producing them: folding them together would recognize as a repeat an entry the file says is another.
    /// </summary>
    [Fact]
    public void Equality_TwoSpellingsDifferingInCase_AreTwoIdentifiers()
    {
        // Act
        var lower = ImportedCalendarEventUid.Create("entry@example.test");
        var upper = ImportedCalendarEventUid.Create("ENTRY@example.test");

        // Assert
        Assert.NotEqual(lower, upper);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_NothingToRead_IsRefused(string? value)
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => ImportedCalendarEventUid.Create(value));

        // Assert
        Assert.Equal("value", refusal.ParamName);
    }

    [Fact]
    public void Create_LongerThanTheBound_IsRefused()
    {
        // Arrange
        var overlong = new string('a', ImportedCalendarEventUid.MaximumLength + 1);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ImportedCalendarEventUid.Create(overlong));
    }

    /// <summary>The value is answered back in the report naming what an import skipped, so it carries no such character.</summary>
    [Theory]
    [InlineData("entry\n@example.test")]
    [InlineData("entry\u202E@example.test")]
    [InlineData("entry\u2028@example.test")]
    public void Create_ACharacterThatCarriesNoGlyph_IsRefused(string value)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => ImportedCalendarEventUid.Create(value));
    }

    /// <summary>
    /// The file is untrusted, so the two shapes a per-character test would let through are refused here: a formatting
    /// character outside the Basic Multilingual Plane, whose surrogate halves categorize as neither control nor
    /// format, and an unpaired surrogate, which is not a character at all.
    /// </summary>
    [Fact]
    public void Create_TextOnlyAScalarWalkRefuses_IsRefused()
    {
        // Arrange
        var tagged = "entry" + char.ConvertFromUtf32(0xE0001) + "@example.test";
        var illFormed = "entry" + (char)0xDC00 + "@example.test";

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ImportedCalendarEventUid.Create(tagged));
        Assert.Throws<ArgumentException>(() => ImportedCalendarEventUid.Create(illFormed));
    }

    [Fact]
    public void TryCreate_SomethingThatIsNotAnIdentifier_AnswersWithTheUnspecifiedValue()
    {
        // Act
        var read = ImportedCalendarEventUid.TryCreate(value: null, out var uid);

        // Assert
        Assert.False(read);
        Assert.False(uid.IsSpecified);
    }
}
