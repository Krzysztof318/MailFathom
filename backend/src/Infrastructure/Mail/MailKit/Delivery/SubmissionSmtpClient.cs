// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailKit.Net.Smtp;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>The SMTP client MailFathom submits through, which reports the envelope instead of failing on it.</summary>
/// <remarks>
/// <para>
/// The library's own client ends a submission the moment one recipient is refused, which is the right default for a
/// caller with nowhere to write the answer down and the wrong one here: a mistyped address among five must not stop the
/// other four, and the four who received the message must not be offered it again when the fifth is retried. So a
/// refusal is recorded and the submission continues, and the one case that genuinely cannot continue — an envelope with
/// no accepted address at all — is what raises.
/// </para>
/// <para>
/// Each override is a hand-off and nothing more. They run inside the protocol exchange, between the envelope and the
/// message body, so anything that waited here would hold the connection open while it did.
/// </para>
/// </remarks>
internal sealed class SubmissionSmtpClient : SmtpClient, ISubmissionClient
{
    /// <inheritdoc />
    public SmtpEnvelopeObserver? Envelope { get; set; }

    /// <inheritdoc />
    protected override void OnRecipientAccepted(MimeMessage message, MailboxAddress mailbox, SmtpResponse response) =>
        this.Envelope?.RecipientAnswered(mailbox, response);

    /// <inheritdoc />
    /// <remarks>
    /// Deliberately does not raise, which is the whole of what this subclass changes. The refused address is settled on
    /// the ledger and the submission goes on to the addresses that were accepted.
    /// </remarks>
    protected override void OnRecipientNotAccepted(MimeMessage message, MailboxAddress mailbox, SmtpResponse response) =>
        this.Envelope?.RecipientAnswered(mailbox, response);

    /// <inheritdoc />
    /// <remarks>
    /// The library calls this only when the overridden refusal above let every recipient through, and something has to
    /// stop the submission here: there is no envelope to transmit into. What the refusal was is on the ledger.
    /// </remarks>
    protected override void OnNoRecipientsAccepted(MimeMessage message) =>
        throw new SmtpNoRecipientsAcceptedException();
}
