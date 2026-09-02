// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery.Scheduling;

/// <summary>Covers what a declaration accepts, given that everything it accepts describes many messages rather than one.</summary>
public sealed class RecurringSendRequestTests
{
    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    /// <summary>A declaration keeps what it was made with, and the schedule as written bar the space around it.</summary>
    [Fact]
    public void Create_ARepetitionAndTheMessageItRepeats_KeepsBoth()
    {
        // Act
        var declaration = RecurringSendRequest.Create(
            Account,
            OutgoingEmailRequester.Command("declare-1"),
            [Recipient("anna@example.test", OutgoingRecipientRole.To)],
            "  Daily at 09:00 Europe/Warsaw  ");

        // Assert
        Assert.Equal("Daily at 09:00 Europe/Warsaw", declaration.Schedule);
        Assert.Equal(Account, declaration.Account);
        Assert.Single(declaration.Recipients);
    }

    /// <summary>
    /// The recipients are validated by the rules an outgoing request applies, because every occasion becomes one — so a
    /// declaration no message could be written for is refused where somebody is present to be told.
    /// </summary>
    [Fact]
    public void Create_OneMailboxNamedTwice_IsRefusedTheWayASingleSendIs()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => RecurringSendRequest.Create(
            Account,
            OutgoingEmailRequester.Command("declare-1"),
            [
                Recipient("anna@example.test", OutgoingRecipientRole.To),
                Recipient("ANNA@example.test", OutgoingRecipientRole.Cc),
            ],
            "Daily at 09:00"));
    }

    /// <summary>A declaration nobody is offered describes a message with nowhere to go.</summary>
    [Fact]
    public void Create_NoRecipientAtAll_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => RecurringSendRequest.Create(
            Account,
            OutgoingEmailRequester.Command("declare-1"),
            [],
            "Daily at 09:00"));
    }

    /// <summary>A schedule naming nothing or carrying a control character is refused rather than stored unreadable.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Daily at 09:00\u0007")]
    public void Create_AScheduleThatCouldNotBeStoredOrRead_IsRefused(string schedule)
    {
        // Act
        var thrown = Assert.Throws<ArgumentException>(() => RecurringSendRequest.Create(
            Account,
            OutgoingEmailRequester.Command("declare-1"),
            [Recipient("anna@example.test", OutgoingRecipientRole.To)],
            schedule));

        // Assert
        Assert.Equal("schedule", thrown.ParamName);
    }

    /// <summary>A schedule longer than the column that holds it is refused rather than truncated.</summary>
    [Fact]
    public void Create_AScheduleLongerThanTheColumnThatHoldsIt_IsRefused()
    {
        // Act
        var thrown = Assert.Throws<ArgumentException>(() => RecurringSendRequest.Create(
            Account,
            OutgoingEmailRequester.Command("declare-1"),
            [Recipient("anna@example.test", OutgoingRecipientRole.To)],
            new string('e', RecurringSend.MaximumScheduleLength + 1)));

        // Assert
        Assert.Equal("schedule", thrown.ParamName);
    }

    /// <summary>A declaration nothing asked for could not be answered idempotently, so the act asking is required.</summary>
    [Fact]
    public void Create_NoRequesterAtAll_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => RecurringSendRequest.Create(
            Account,
            requester: null!,
            [Recipient("anna@example.test", OutgoingRecipientRole.To)],
            "Daily at 09:00"));
    }

    private static OutgoingRecipient Recipient(string address, OutgoingRecipientRole role)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var emailAddress))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return OutgoingRecipient.Create(emailAddress, role);
    }
}
