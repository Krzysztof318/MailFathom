// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery;
using MailFathom.Infrastructure.Persistence.Delivery;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Delivery;

/// <summary>
/// Covers the one reading a send, a draft, and a recurring send all restore their recipients through. Two of its rules
/// are the reason it exists at all: an address that no longer parses stops the read rather than quietly shrinking the
/// envelope, and the refusal names the row without carrying the address into a message an operator will read.
/// </summary>
public sealed class StoredOutgoingRecipientTests
{
    private static readonly Guid Carrier = new("2f1c9d4e-0b71-4f2a-9d3c-5e6a7b8c9d01");

    /// <summary>The ordinary row: an address, a header, and the contact it was resolved from.</summary>
    [Fact]
    public void ToRecipient_ARowNamingAContact_RestoresTheRecipientAndTheContact()
    {
        // Arrange
        var contactId = Guid.NewGuid();

        // Act
        var recipient = StoredOutgoingRecipient.ToRecipient(
            "Outgoing email record",
            Carrier,
            ordinal: 0,
            "someone@example.test",
            OutgoingRecipientRole.To,
            contactId);

        // Assert
        Assert.Equal("someone@example.test", recipient.Address.Address);
        Assert.Equal(OutgoingRecipientRole.To, recipient.Role);
        Assert.Equal(contactId, recipient.Contact?.Value);
    }

    /// <summary>An address the author supplied by hand carries no contact, and neither does a row written with none.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void ToRecipient_ARowNamingNoContact_RestoresTheRecipientWithoutOne(string? storedContact)
    {
        // Arrange
        var contactId = storedContact is null ? (Guid?)null : Guid.Parse(storedContact);

        // Act
        var recipient = StoredOutgoingRecipient.ToRecipient(
            "Mail draft",
            Carrier,
            ordinal: 1,
            "someone@example.test",
            OutgoingRecipientRole.Cc,
            contactId);

        // Assert
        Assert.Null(recipient.Contact);
    }

    /// <summary>
    /// The read stops rather than dropping the row. A message offered to fewer people than it was written for is
    /// somebody who never receives it and is told nothing about it.
    /// </summary>
    [Fact]
    public void ToRecipient_AnAddressThatNoLongerParses_StopsTheReadNamingTheRowRatherThanTheAddress()
    {
        // Act
        var refusal = Record.Exception(() => StoredOutgoingRecipient.ToRecipient(
            "Recurring send",
            Carrier,
            ordinal: 3,
            "not-a-mailbox",
            OutgoingRecipientRole.Bcc,
            contactId: null));

        // Assert
        var stopped = Assert.IsType<InvalidOperationException>(refusal);
        Assert.Contains("Recurring send", stopped.Message, StringComparison.Ordinal);
        Assert.Contains(Carrier.ToString(), stopped.Message, StringComparison.Ordinal);
        Assert.Contains("position 3", stopped.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-mailbox", stopped.Message, StringComparison.Ordinal);
    }
}
