// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails.Authentication;

/// <summary>Covers what one entry of a trusted-sender list recognizes, and what it deliberately does not.</summary>
public sealed class TrustedSenderEntryTests
{
    /// <summary>A domain entry recognizes exactly the domain it names.</summary>
    [Theory]
    [InlineData("partner.example", true)]
    [InlineData("Partner.Example", true)]
    [InlineData("mail.partner.example", false)]
    [InlineData("otherpartner.example", false)]
    public void Matches_DomainEntryWithoutSubdomains_RecognizesThatDomainAlone(string authenticated, bool expected)
    {
        // Arrange
        var entry = DomainEntry("partner.example", includeSubdomains: false);

        // Act
        var recognized = entry.Matches(DomainOf(authenticated), displayedSender: null);

        // Assert
        Assert.Equal(expected, recognized);
    }

    /// <summary>The opt-in is what reaches under a domain, and it reaches the domain itself as well.</summary>
    [Theory]
    [InlineData("partner.example", true)]
    [InlineData("mail.partner.example", true)]
    [InlineData("a.b.partner.example", true)]
    [InlineData("otherpartner.example", false)]
    public void Matches_DomainEntryIncludingSubdomains_RecognizesTheWholeBranch(string authenticated, bool expected)
    {
        // Arrange
        var entry = DomainEntry("partner.example", includeSubdomains: true);

        // Act
        var recognized = entry.Matches(DomainOf(authenticated), displayedSender: null);

        // Assert
        Assert.Equal(expected, recognized);
    }

    /// <summary>An address entry reads the local part from the header the authenticated domain answers for.</summary>
    [Fact]
    public void Matches_AddressEntryAndTheDisplayedSenderItNames_RecognizesTheSender()
    {
        // Arrange
        var entry = AddressEntry("alice@partner.example");

        // Act
        var recognized = entry.Matches(DomainOf("partner.example"), AddressOf("Alice@Partner.Example"));

        // Assert
        Assert.True(recognized);
    }

    /// <summary>A verdict that names only a domain recognizes nobody through an address entry, whatever the domain is.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("bob@partner.example")]
    [InlineData("alice@elsewhere.example")]
    public void Matches_AddressEntryAgainstADomainOnlyVerdict_RecognizesNobody(string? displayed)
    {
        // Arrange
        var entry = AddressEntry("alice@partner.example");

        // Act
        var recognized = entry.Matches(DomainOf("partner.example"), displayed is null ? null : AddressOf(displayed));

        // Assert
        Assert.False(recognized);
    }

    /// <summary>An address entry never reaches beneath its own domain, because a mailbox has nothing beneath it.</summary>
    [Fact]
    public void Matches_AddressEntryAgainstASubdomain_RecognizesNobody()
    {
        // Arrange
        var entry = AddressEntry("alice@partner.example");

        // Act
        var recognized = entry.Matches(DomainOf("mail.partner.example"), AddressOf("alice@mail.partner.example"));

        // Assert
        Assert.False(recognized);
    }

    /// <summary>An entry written in one encoding of an internationalized name recognizes mail that carried the other.</summary>
    [Fact]
    public void Matches_DomainEntryWrittenInUnicode_RecognizesTheAsciiFormAMessageCarried()
    {
        // Arrange
        var entry = DomainEntry("bücher.example", includeSubdomains: false);

        // Act
        var recognized = entry.Matches(DomainOf("xn--bcher-kva.example"), displayedSender: null);

        // Assert
        Assert.True(recognized);
    }

    /// <summary>Text nothing can compare on names no sender, so no entry is built from it.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("part ner.example")]
    [InlineData("alice@partner.example")]
    public void TryCreateForDomain_UnusableText_IsRefused(string? written)
    {
        // Act
        var created = TrustedSenderEntry.TryCreateForDomain(written, includeSubdomains: false, out _);

        // Assert
        Assert.False(created);
    }

    /// <summary>An address entry needs a mailbox, and a bare domain is not one.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("partner.example")]
    [InlineData("alice@")]
    [InlineData("@partner.example")]
    [InlineData("alice@partner..example")]
    public void TryCreateForAddress_UnusableText_IsRefused(string? written)
    {
        // Act
        var created = TrustedSenderEntry.TryCreateForAddress(written, out _);

        // Assert
        Assert.False(created);
    }

    /// <summary>Two entries that recognize different senders write different lines, so a change to the list is never invisible.</summary>
    [Fact]
    public void ToPolicyStatement_EntriesRecognizingDifferentSenders_AreDistinguishable()
    {
        // Arrange
        var exact = DomainEntry("partner.example", includeSubdomains: false);
        var branch = DomainEntry("partner.example", includeSubdomains: true);
        var mailbox = AddressEntry("alice@partner.example");

        // Act
        var statements = new[] { exact, branch, mailbox }.Select(entry => entry.ToPolicyStatement()).ToArray();

        // Assert
        Assert.Equal(statements.Length, statements.Distinct(StringComparer.Ordinal).Count());
    }

    private static TrustedSenderEntry DomainEntry(string domain, bool includeSubdomains)
    {
        Assert.True(TrustedSenderEntry.TryCreateForDomain(domain, includeSubdomains, out var entry));
        Assert.NotNull(entry);

        return entry;
    }

    private static TrustedSenderEntry AddressEntry(string address)
    {
        Assert.True(TrustedSenderEntry.TryCreateForAddress(address, out var entry));
        Assert.NotNull(entry);

        return entry;
    }

    private static SenderDomain DomainOf(string written)
    {
        Assert.True(SenderDomain.TryCreate(written, out var domain));

        return domain;
    }

    private static EmailAddress AddressOf(string written)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, written, out var address));

        return address;
    }
}
