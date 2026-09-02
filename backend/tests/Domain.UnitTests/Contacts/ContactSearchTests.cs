// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using Xunit;

namespace MailFathom.Domain.UnitTests.Contacts;

/// <summary>Covers the text a caller looks somebody up by, and the form the book compares it in.</summary>
public sealed class ContactSearchTests
{
    /// <summary>One value is matched against a name's comparison form and an address's, so it is derived the same way both are.</summary>
    [Theory]
    [InlineData("Kowalska", "KOWALSKA")]
    [InlineData("  anna@example.test  ", "ANNA@EXAMPLE.TEST")]
    [InlineData("Anna Kowalska", "ANNA KOWALSKA")]
    public void Create_TextACallerWrote_CarriesTheComparisonFormTheBookMatchesOn(string value, string expectedForm)
    {
        // Act
        var search = ContactSearch.Create(value);

        // Assert
        Assert.Equal(value.Trim(), search.Text);
        Assert.Equal(expectedForm, search.ComparisonForm);
    }

    /// <summary>Reading blank text as the whole book would turn a mistyped lookup into a walk of everybody this deployment holds.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankText_IsRefused(string value) =>
        Assert.Throws<ArgumentException>(() => ContactSearch.Create(value));

    /// <summary>Text longer than the longest value the book holds can match nothing, so it is refused rather than run.</summary>
    [Fact]
    public void Create_TextLongerThanAnythingTheBookHolds_IsRefused() =>
        Assert.Throws<ArgumentException>(() =>
            ContactSearch.Create(new string('a', ContactSearch.MaximumLength + 1)));

    /// <summary>The text at the bound is one the book could genuinely be holding, so it is accepted.</summary>
    [Fact]
    public void Create_TextAtTheBound_IsAccepted()
    {
        // Act
        var search = ContactSearch.Create(new string('a', ContactSearch.MaximumLength));

        // Assert
        Assert.Equal(ContactSearch.MaximumLength, search.Text.Length);
    }

    /// <summary>
    /// The text reaches a database predicate and travels back in nothing, but it is judged by the same rule a name is:
    /// a control character, a line break, a bidirectional override, and text that is not well-formed at all.
    /// </summary>
    [Theory]
    [InlineData("Anna\u0001Kowalska")]
    [InlineData("Anna\nKowalska")]
    [InlineData("Anna\u202EKowalska")]
    public void Create_TextCarryingACharacterThatRendersAsNothing_IsRefused(string value) =>
        Assert.Throws<ArgumentException>(() => ContactSearch.Create(value));

    /// <summary>An unpaired surrogate is no character at all, and the scalar walk substitutes a printable one for it.</summary>
    /// <remarks>
    /// Built here rather than written as theory data, because the serializer that carries theory data replaces the
    /// unpaired half with a replacement character on the way in \u2014 so the case would arrive well-formed and prove nothing.
    /// </remarks>
    [Fact]
    public void Create_TextCarryingAnUnpairedSurrogate_IsRefused()
    {
        // Arrange
        var value = string.Concat("Anna", "\ud800", "Kowalska");

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactSearch.Create(value));
    }

    /// <summary>The two joiners are written inside ordinary words, so refusing them would refuse names people have.</summary>
    [Fact]
    public void Create_TextCarryingAJoiner_IsAccepted()
    {
        // Act
        var search = ContactSearch.Create("Anna\u200CKowalska");

        // Assert
        Assert.Equal("ANNA\u200CKOWALSKA", search.ComparisonForm);
    }

    /// <summary>Nothing here is a pattern, so a wildcard a caller wrote is text the book looks for rather than a wider search.</summary>
    [Fact]
    public void Create_TextCarryingAWildcardCharacter_KeepsItAsText()
    {
        // Act
        var search = ContactSearch.Create("%anna_");

        // Assert
        Assert.Equal("%ANNA_", search.ComparisonForm);
    }
}
