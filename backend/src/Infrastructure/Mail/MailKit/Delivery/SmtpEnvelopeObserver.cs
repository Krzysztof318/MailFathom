// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Transmission;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailKit.Net.Smtp;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>Turns what a submission server answers about each address into the ledger a delivery attempt reads.</summary>
/// <remarks>
/// <para>
/// It exists because the mail library reports the envelope through callbacks rather than in a return value, and those
/// callbacks are the only place a per-recipient reply can be seen. Everything here is a pure translation — a lookup, a
/// classification, and an append — so nothing waits, blocks, or reaches a database from inside a protocol callback.
/// </para>
/// <para>
/// The address written down is the one the attempt offered rather than the one echoed back. They are the same mailbox,
/// and using the offered value is what lets the record match the reply to the recipient it already holds without
/// depending on how a server spells an address back.
/// </para>
/// <para>
/// An answer about somebody the attempt did not offer is dropped. It cannot happen against a server that follows the
/// protocol, and taking it would put an address on the ledger that the record does not name.
/// </para>
/// </remarks>
internal sealed class SmtpEnvelopeObserver
{
    private readonly Dictionary<string, EmailAddress> offeredByAddress;
    private readonly MailEnvelopeLedger ledger;

    /// <summary>Creates an observer for one submission.</summary>
    /// <param name="offered">The recipients this attempt offers the message to.</param>
    /// <param name="ledger">The ledger the answers are written into.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="offered" /> or <paramref name="ledger" /> is <see langword="null" />.</exception>
    internal SmtpEnvelopeObserver(IReadOnlyList<OutgoingRecipient> offered, MailEnvelopeLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(offered);
        ArgumentNullException.ThrowIfNull(ledger);

        this.offeredByAddress = offered.ToDictionary(
            recipient => recipient.Address.NormalizedAddress,
            recipient => recipient.Address);
        this.ledger = ledger;
    }

    /// <summary>Writes down what the server said about one address.</summary>
    /// <param name="mailbox">The mailbox the <c>RCPT TO</c> command named.</param>
    /// <param name="response">The reply it answered with.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mailbox" /> or <paramref name="response" /> is <see langword="null" />.</exception>
    internal void RecipientAnswered(MailboxAddress mailbox, SmtpResponse response)
    {
        ArgumentNullException.ThrowIfNull(mailbox);
        ArgumentNullException.ThrowIfNull(response);

        if (!EmailAddress.TryCreate(displayName: null, mailbox.Address, out var answered)
            || !this.offeredByAddress.TryGetValue(answered.NormalizedAddress, out var offered))
        {
            return;
        }

        this.ledger.Record(new MailRecipientReply(offered, (int)response.StatusCode, AcceptanceOf(response)));
    }

    /// <summary>Reads a reply as what it means for the recipient it is about.</summary>
    /// <remarks>
    /// A 2yz reply is an acceptance and every other class is judged by the same rules a refusal of the message is, so
    /// one reply cannot be read as temporary here and permanent there. A 3yz reply has no meaning after
    /// <c>RCPT TO</c> and is treated as settled for the reason every unrecognized refusal is: repeating a submission
    /// nobody understood is how a second copy reaches a mailbox.
    /// </remarks>
    private static MailRecipientAcceptance AcceptanceOf(SmtpResponse response)
    {
        var replyCode = (int)response.StatusCode;

        if (replyCode / 100 == 2)
        {
            return MailRecipientAcceptance.Accepted;
        }

        return SmtpReplyClassifier.Classify(replyCode, response.Response).Disposition == SmtpRejectionDisposition.Transient
            ? MailRecipientAcceptance.RefusedTemporarily
            : MailRecipientAcceptance.RefusedPermanently;
    }
}
