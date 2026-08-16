// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using Xunit;

namespace MailFathom.Domain.UnitTests.Contacts;

public sealed class ContactDisplayNameTests
{
    /// <summary>The owner's casing is what a reader is shown, and the sort key is what a listing is ordered by.</summary>
    [Fact]
    public void Create_NameWrittenByAnOwner_KeepsTheCasingAndDerivesTheComparisonForm()
    {
        // Arrange
        const string written = "  Anna Kowalska  ";

        // Act
        var displayName = ContactDisplayName.Create(written);

        // Assert
        Assert.Equal("Anna Kowalska", displayName.Value);
        Assert.Equal("ANNA KOWALSKA", displayName.SortKey);
        Assert.Equal("Anna Kowalska", displayName.ToString());
    }

    /// <summary>Two spellings of one name order together, which is the whole reason the key exists.</summary>
    [Fact]
    public void Create_NamesDifferingOnlyInCase_ProduceOneSortKey()
    {
        // Act
        var written = ContactDisplayName.Create("anna kowalska");
        var shouted = ContactDisplayName.Create("ANNA KOWALSKA");

        // Assert
        Assert.Equal(written.SortKey, shouted.SortKey);
    }

    /// <summary>Blank text names nobody, so it is refused rather than stored as a contact with no name.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_BlankName_IsRefused(string written)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactDisplayName.Create(written));
    }

    /// <summary>A name may not carry a line break, because it is published into listings of the other contacts.</summary>
    [Theory]
    [InlineData("Anna\nKowalska")]
    [InlineData("Anna\rKowalska")]
    [InlineData("Anna\u0007Kowalska")]
    public void Create_NameCarryingAControlCharacter_IsRefused(string written)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => ContactDisplayName.Create(written));
    }

    /// <summary>The bound is on the trimmed value, so surrounding whitespace never decides whether a name fits.</summary>
    [Fact]
    public void Create_NameAtTheBound_IsAcceptedAndOneCharacterLongerIsRefused()
    {
        // Arrange
        var atBound = new string('a', ContactDisplayName.MaximumLength);
        var overBound = new string('a', ContactDisplayName.MaximumLength + 1);

        // Act
        var accepted = ContactDisplayName.Create($"  {atBound}  ");

        // Assert
        Assert.Equal(atBound, accepted.Value);
        Assert.Throws<ArgumentException>(() => ContactDisplayName.Create(overBound));
    }
}
