// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery;

public sealed class OutgoingRecipientTests
{
    private static readonly ContactId Contact =
        ContactId.Create(Guid.Parse("0198f0a0-2222-7000-8000-000000000001"));

    /// <summary>An address an author wrote down came from nobody in the book, and the record says so.</summary>
    [Fact]
    public void Create_AddressWithoutAContact_NamesNoContact()
    {
        // Arrange
        var address = Address("anna@example.test");

        // Act
        var recipient = OutgoingRecipient.Create(address, OutgoingRecipientRole.To);

        // Assert
        Assert.Null(recipient.Contact);
        Assert.Equal(address, recipient.Address);
    }

    /// <summary>
    /// The address and the contact are both kept, because they answer different questions: what a resumed attempt
    /// offers, and which person the message was addressed by naming.
    /// </summary>
    [Fact]
    public void Create_AddressResolvedFromAContact_KeepsBoth()
    {
        // Arrange
        var address = Address("anna@example.test");

        // Act
        var recipient = OutgoingRecipient.Create(address, OutgoingRecipientRole.Cc, Contact);

        // Assert
        Assert.Equal(address, recipient.Address);
        Assert.Equal(Contact, recipient.Contact);
        Assert.Equal(OutgoingRecipientRole.Cc, recipient.Role);
    }

    /// <summary>
    /// The description names the header and nothing else. An address is personal data of a third party and would
    /// otherwise reach every log template, exception message, and interpolated string that mentions a recipient.
    /// </summary>
    [Fact]
    public void ToString_RecipientResolvedFromAContact_DescribesTheRoleAlone()
    {
        // Arrange
        var recipient = OutgoingRecipient.Create(Address("anna@example.test"), OutgoingRecipientRole.Bcc, Contact);

        // Act
        var described = recipient.ToString();

        // Assert
        Assert.Equal("Bcc recipient", described);
        Assert.DoesNotContain("example.test", described, StringComparison.OrdinalIgnoreCase);
    }

    private static EmailAddress Address(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return emailAddress;
    }
}
