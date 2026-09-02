// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Addressing;

/// <summary>Covers the one reading of the three recipient headers every way of authoring a message shares.</summary>
/// <remarks>
/// What it answers is the order the headers are read in, how long they may be together, and what a blank entry means.
/// Whether text names a mailbox is the composition's question and is not asked here, which is why an address that no
/// mail server would accept travels through unchanged.
/// </remarks>
public sealed class AuthoredRecipientHeadersTests
{
    /// <summary>The three headers become one list, each entry carrying the role of the header it came from.</summary>
    [Fact]
    public void NamedRecipients_ThreeHeaders_ReadsThemInTheOrderAMessageWritesThem()
    {
        // Arrange
        // Act
        var named = AuthoredRecipientHeaders.NamedRecipients(
            ["to@example.test"],
            ["cc@example.test"],
            ["bcc@example.test"],
            TooMany,
            Unusable);

        // Assert
        Assert.Equal(
            [
                (OutgoingRecipientRole.To, "to@example.test"),
                (OutgoingRecipientRole.Cc, "cc@example.test"),
                (OutgoingRecipientRole.Bcc, "bcc@example.test"),
            ],
            named.Select(recipient => (recipient.Role, recipient.Address)));
    }

    /// <summary>A message naming nobody is read as naming nobody, because writing before deciding is what a draft is for.</summary>
    [Fact]
    public void NamedRecipients_NoHeaderAtAll_ReadsAsAMessageAddressedToNobody()
    {
        // Arrange
        // Act
        var named = AuthoredRecipientHeaders.NamedRecipients(to: null, cc: null, bcc: null, TooMany, Unusable);

        // Assert
        Assert.Empty(named);
    }

    /// <summary>An entry naming nobody stops the reading, and reports which header carried it rather than what it said.</summary>
    /// <param name="header">The header the blank entry was written in.</param>
    [Theory]
    [InlineData(AuthoredEmailField.To)]
    [InlineData(AuthoredEmailField.Cc)]
    [InlineData(AuthoredEmailField.Bcc)]
    public void NamedRecipients_AHeaderCarryingABlankEntry_IsRefusedNamingThatHeader(AuthoredEmailField header)
    {
        // Arrange
        var blank = new[] { " " };

        // Act
        var refusal = Assert.Throws<MailDraftRefusedException>(() => AuthoredRecipientHeaders.NamedRecipients(
            header == AuthoredEmailField.To ? blank : null,
            header == AuthoredEmailField.Cc ? blank : null,
            header == AuthoredEmailField.Bcc ? blank : null,
            TooMany,
            Unusable));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailFieldRefused, refusal.ErrorCode);
    }

    /// <summary>The three headers are counted together, and a message naming more people than a record holds is refused.</summary>
    /// <remarks>
    /// Ahead of the expansion rather than after it, so a caller cannot make this deployment build a list it is about to
    /// refuse by splitting the same addresses across the three headers.
    /// </remarks>
    [Fact]
    public void NamedRecipients_MorePeopleAcrossTheThreeHeadersThanARecordHolds_IsRefused()
    {
        // Arrange
        var third = Enumerable
            .Range(0, (OutgoingEmailRequest.MaximumRecipientCount / 3) + 1)
            .Select(position => $"person-{position}@example.test")
            .ToArray();

        // Act
        var refusal = Assert.Throws<MailDraftRefusedException>(
            () => AuthoredRecipientHeaders.NamedRecipients(third, third, third, TooMany, Unusable));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailBoundExceeded, refusal.ErrorCode);
    }

    /// <summary>Text no mail server would accept travels through, because reading an address is the composition's act.</summary>
    [Fact]
    public void NamedRecipients_TextThatNamesNoMailbox_IsCarriedToTheCompositionUnparsed()
    {
        // Arrange
        // Act
        var named = AuthoredRecipientHeaders.NamedRecipients(
            ["not an address"],
            cc: null,
            bcc: null,
            TooMany,
            Unusable);

        // Assert
        Assert.Equal("not an address", Assert.Single(named).Address);
    }

    /// <summary>Raises the refusal a draft states for a list longer than a record holds.</summary>
    private static MailFathomException TooMany() => MailDraftRefusedException.TooManyRecipients();

    /// <summary>Raises the refusal a draft states for a header carrying an entry that names nobody.</summary>
    private static MailFathomException Unusable(AuthoredEmailRefusal refusal) => MailDraftRefusedException.From(refusal);
}
