// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery;

public sealed class OutgoingEmailRequestTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly OutgoingEmailRequester Requester =
        OutgoingEmailRequester.Command("mfctl-4f2a");

    [Fact]
    public void Create_Recipients_KeepsThemInTheOrderTheyWereNamed()
    {
        // Arrange
        var recipients = new[]
        {
            OutgoingDeliveryFixture.Recipient("anna@example.test", OutgoingRecipientRole.To),
            OutgoingDeliveryFixture.Recipient("bruno@example.test", OutgoingRecipientRole.Cc),
            OutgoingDeliveryFixture.Recipient("clara@example.test", OutgoingRecipientRole.Bcc),
        };

        // Act
        var request = OutgoingEmailRequest.Create(Account, Requester, recipients);

        // Assert
        Assert.Equal(recipients, request.Recipients);
        Assert.Equal(Account, request.AccountId);
        Assert.Equal(Requester, request.Requester);
    }

    /// <summary>An envelope offers one address once, so a mailbox written in two headers would be a second copy.</summary>
    [Fact]
    public void Create_OneMailboxInTwoHeaders_IsRefused()
    {
        // Arrange
        var recipients = new[]
        {
            OutgoingDeliveryFixture.Recipient("anna@example.test", OutgoingRecipientRole.To),
            OutgoingDeliveryFixture.Recipient("Anna@Example.test", OutgoingRecipientRole.Cc),
        };

        // Act
        var thrown = Assert.Throws<ArgumentException>(
            () => OutgoingEmailRequest.Create(Account, Requester, recipients));

        // Assert
        Assert.Equal("recipients", thrown.ParamName);
        Assert.DoesNotContain("anna@example.test", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_NoRecipients_IsRefused()
    {
        // Act
        var thrown = Assert.Throws<ArgumentException>(
            () => OutgoingEmailRequest.Create(Account, Requester, []));

        // Assert
        Assert.Equal("recipients", thrown.ParamName);
    }

    /// <summary>The list is copied, so a caller mutating theirs afterwards cannot change what was asked for.</summary>
    [Fact]
    public void Create_CallerMutatesTheirListAfterwards_LeavesTheRequestAlone()
    {
        // Arrange
        var recipients = new List<OutgoingRecipient>
        {
            OutgoingDeliveryFixture.Recipient("anna@example.test", OutgoingRecipientRole.To),
        };
        var request = OutgoingEmailRequest.Create(Account, Requester, recipients);

        // Act
        recipients.Add(OutgoingDeliveryFixture.Recipient("bruno@example.test", OutgoingRecipientRole.Bcc));

        // Assert
        Assert.Single(request.Recipients);
    }

    /// <summary>An unbounded recipient list is an unbounded insert and an unbounded conversation with a server.</summary>
    [Fact]
    public void Create_MoreRecipientsThanAMessageMayName_IsRefused()
    {
        // Arrange
        var recipients = Enumerable
            .Range(0, OutgoingEmailRequest.MaximumRecipientCount + 1)
            .Select(index => OutgoingDeliveryFixture.Recipient(
                $"recipient-{index}@example.test",
                OutgoingRecipientRole.Bcc))
            .ToArray();

        // Act
        var thrown = Assert.Throws<ArgumentException>(
            () => OutgoingEmailRequest.Create(Account, Requester, recipients));

        // Assert
        Assert.Equal("recipients", thrown.ParamName);
    }

    /// <summary>
    /// An address longer than the column is refused rather than dropped, because dropping one would be a person the
    /// owner wrote to who never receives the message and is told nothing about it.
    /// </summary>
    [Fact]
    public void Recipient_AddressLongerThanTheColumn_IsRefused()
    {
        // Arrange
        var localPart = new string('a', OutgoingRecipient.MaximumAddressLength);

        // Act
        var thrown = Assert.Throws<ArgumentException>(
            () => OutgoingDeliveryFixture.Recipient($"{localPart}@example.test", OutgoingRecipientRole.To));

        // Assert
        Assert.Equal("address", thrown.ParamName);
        Assert.DoesNotContain(localPart, thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>A default value carries no address, and a record written with one could be offered to nobody.</summary>
    [Fact]
    public void Recipient_DefaultAddress_IsRefused()
    {
        // Act
        var thrown = Assert.Throws<ArgumentException>(
            () => OutgoingRecipient.Create(default, OutgoingRecipientRole.To));

        // Assert
        Assert.Equal("address", thrown.ParamName);
    }

    /// <summary>
    /// A recipient's address is personal data of somebody who is not this mailbox's owner, so nothing that describes a
    /// recipient — including the description a record struct would synthesize — may carry it into a log line, a span
    /// attribute, or an exception message.
    /// </summary>
    [Fact]
    public void ToString_ARecipientAndTheOutcomeAroundIt_NamesTheRoleAndNeverTheAddress()
    {
        // Arrange
        var recipient = OutgoingDeliveryFixture.Recipient("anna@example.test", OutgoingRecipientRole.Bcc);

        // Act
        var described = $"{recipient} {OutgoingRecipientOutcome.Unanswered(recipient)}";

        // Assert
        Assert.DoesNotContain("anna@example.test", described, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(OutgoingRecipientRole.Bcc), described, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_RoleOutsideTheDeclaredSet_IsRefused() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => OutgoingDeliveryFixture.Recipient("anna@example.test", (OutgoingRecipientRole)9));
}
