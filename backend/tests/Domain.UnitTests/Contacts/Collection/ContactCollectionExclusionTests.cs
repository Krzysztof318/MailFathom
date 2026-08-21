// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts.Collection;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.Contacts.Collection;

/// <summary>Covers the owner's own half of collection's bounds: what one deployment says it never records.</summary>
public sealed class ContactCollectionExclusionTests
{
    /// <summary>A domain entry is the shape for correspondence that all arrives from one place.</summary>
    [Theory]
    [InlineData("anna@example.test", true)]
    [InlineData("Anna@EXAMPLE.test", true)]
    [InlineData("anna@other.test", false)]
    [InlineData("anna@mail.example.test", false)]
    public void Excludes_ADomainEntry_MatchesThatDomainAlone(string address, bool expected)
    {
        // Arrange
        Assert.True(ContactCollectionExclusion.TryCreateForDomain(
            "example.test",
            includeSubdomains: false,
            out var exclusion));

        // Act & Assert
        Assert.Equal(expected, exclusion.Excludes(AddressOf(address)));
    }

    /// <summary>Reaching under a domain is opt-in, because an organization whose automation lives beneath its own name needs one entry.</summary>
    [Theory]
    [InlineData("anna@example.test", true)]
    [InlineData("bot@mail.example.test", true)]
    [InlineData("anna@notexample.test", false)]
    public void Excludes_ADomainEntryReachingUnderItself_MatchesTheNamesBeneathIt(string address, bool expected)
    {
        // Arrange
        Assert.True(ContactCollectionExclusion.TryCreateForDomain(
            "example.test",
            includeSubdomains: true,
            out var exclusion));

        // Act & Assert
        Assert.Equal(expected, exclusion.Excludes(AddressOf(address)));
    }

    /// <summary>A pattern selects on how an address is spelled, wherever it is hosted, and casing decides nothing.</summary>
    [Theory]
    [InlineData("*+noreply@*", "anna+noreply@example.test", true)]
    [InlineData("*+noreply@*", "anna@example.test", false)]
    [InlineData("bot-*@example.test", "bot-nightly@example.test", true)]
    [InlineData("bot-*@example.test", "BOT-Nightly@Example.test", true)]
    [InlineData("bot-*@example.test", "bot-nightly@other.test", false)]
    [InlineData("anna@example.test", "anna@example.test", true)]
    [InlineData("ann?@example.test", "anna@example.test", true)]
    [InlineData("ann?@example.test", "annabel@example.test", false)]
    public void Excludes_APatternEntry_MatchesTheAddressesItSelects(string pattern, string address, bool expected)
    {
        // Arrange
        Assert.True(ContactCollectionExclusion.TryCreateForAddressPattern(pattern, out var exclusion));

        // Act & Assert
        Assert.Equal(expected, exclusion.Excludes(AddressOf(address)));
    }

    /// <summary>An entry that excluded everybody would switch collection off through a list written to narrow it.</summary>
    /// <remarks>
    /// The at-sign shapes are the ones worth stating: <c>*@*</c> carries a literal, so a rule refusing only all-wildcard
    /// text accepts it, and it matches every address there is — a normalized address holds exactly one at-sign with
    /// arbitrary text either side. <c>*@</c> and <c>@*</c> are the mirror of it and match nothing, which is the same
    /// typo landing the other way.
    /// </remarks>
    [Theory]
    [InlineData("*")]
    [InlineData("**")]
    [InlineData("*?*")]
    [InlineData("*@*")]
    [InlineData("**@**")]
    [InlineData("*@")]
    [InlineData("@*")]
    [InlineData("?@?")]
    [InlineData("@")]
    public void TryCreateForAddressPattern_APatternSelectingOnNothingAnAddressDiffersBy_IsRefused(string pattern) =>
        Assert.False(ContactCollectionExclusion.TryCreateForAddressPattern(pattern, out _));

    /// <summary>The rule stops at the at-sign, so a pattern carrying any other literal is a narrowing and is kept.</summary>
    /// <remarks>
    /// The control for the refusals above. Without it a rule that had grown to refuse every pattern containing an
    /// at-sign — which is nearly every useful one — would pass every case in that theory.
    /// </remarks>
    [Theory]
    [InlineData("*@example.test")]
    [InlineData("noreply@*")]
    [InlineData("*@*.example.test")]
    public void TryCreateForAddressPattern_APatternCarryingALiteralBesideTheAtSign_IsKept(string pattern) =>
        Assert.True(ContactCollectionExclusion.TryCreateForAddressPattern(pattern, out _));

    /// <summary>Blank text and text longer than any address the book holds are entries nobody could have meant.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryCreateForAddressPattern_TextThatSelectsNothing_IsRefused(string? pattern) =>
        Assert.False(ContactCollectionExclusion.TryCreateForAddressPattern(pattern, out _));

    /// <summary>A pattern beyond the longest address the book can hold matches nothing while still being scanned.</summary>
    [Fact]
    public void TryCreateForAddressPattern_APatternLongerThanAnyAddress_IsRefused() =>
        Assert.False(ContactCollectionExclusion.TryCreateForAddressPattern(
            new string('a', ContactCollectionExclusion.MaximumPatternLength + 1),
            out _));

    /// <summary>A domain nothing can compare is an entry that would exclude nobody, so it never becomes one.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a domain")]
    public void TryCreateForDomain_TextThatIsNoDomain_IsRefused(string domain) =>
        Assert.False(ContactCollectionExclusion.TryCreateForDomain(domain, includeSubdomains: false, out _));

    private static EmailAddress AddressOf(string value)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, value, out var address));

        return address;
    }
}
