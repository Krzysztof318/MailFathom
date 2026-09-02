// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Addressing;

/// <summary>Covers what a recipient may be named by, and what naming nobody is refused as.</summary>
public sealed class NamedRecipientTests
{
    private static readonly ContactId Contact =
        ContactId.Create(Guid.Parse("0198f0a0-3333-7000-8000-000000000001"));

    /// <summary>Exactly one of the three ways of naming somebody is present, so nothing downstream has to choose.</summary>
    [Fact]
    public void AtAddress_AnAddressTheAuthorWrote_NamesNoContact()
    {
        // Act
        var named = NamedRecipient.AtAddress(OutgoingRecipientRole.To, "anna@example.test", "Anna Kowalska");

        // Assert
        Assert.Equal("anna@example.test", named.Address);
        Assert.Equal("Anna Kowalska", named.DisplayName);
        Assert.Null(named.Contact);
        Assert.Null(named.ContactName);
        Assert.Null(named.ContactAddress);
    }

    /// <summary>A contact named by identity carries no address of its own until the book has been read.</summary>
    [Fact]
    public void ByContact_AContactIdentity_CarriesNoAddress()
    {
        // Act
        var named = NamedRecipient.ByContact(OutgoingRecipientRole.Cc, Contact);

        // Assert
        Assert.Equal(Contact, named.Contact);
        Assert.Null(named.Address);
        Assert.Null(named.ContactAddress);
        Assert.Equal(OutgoingRecipientRole.Cc, named.Role);
    }

    /// <summary>A contact named by name carries the name in the form the book compares its own values in.</summary>
    [Fact]
    public void ByContactName_AName_CarriesItAndNoIdentity()
    {
        // Act
        var named = NamedRecipient.ByContactName(
            OutgoingRecipientRole.Bcc,
            ContactDisplayName.Create("Anna Kowalska"));

        // Assert
        Assert.Equal("Anna Kowalska", named.ContactName?.Value);
        Assert.Null(named.Contact);
        Assert.Null(named.Address);
    }

    /// <summary>Blank text names nobody, so it is refused rather than composed into a message addressed to nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AtAddress_BlankAddress_IsRefused(string address) =>

        // Act, Assert
        Assert.Throws<ArgumentException>(() =>
            NamedRecipient.AtAddress(OutgoingRecipientRole.To, address));

    /// <summary>
    /// A blank chosen address is refused rather than read as no choice at all, which would silently send to the
    /// preferred address the caller did not ask for.
    /// </summary>
    [Fact]
    public void ByContact_BlankChosenAddress_IsRefused() =>

        // Act, Assert
        Assert.Throws<ArgumentException>(() =>
            NamedRecipient.ByContact(OutgoingRecipientRole.To, Contact, "  "));

    /// <summary>A header this system does not declare is a boundary that mapped its input wrongly.</summary>
    [Fact]
    public void AtAddress_UndeclaredRole_IsRefused() =>

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NamedRecipient.AtAddress((OutgoingRecipientRole)99, "anna@example.test"));
}
