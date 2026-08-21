// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Delivery.Transmission;

/// <summary>Reports what a submission server answered about one address the envelope offered.</summary>
/// <remarks>
/// <para>
/// A message is offered per address and answered per address, so this is the grain the answer arrives at and the grain
/// the durable record keeps it at. A mistyped address among five must not stop the other four, and the four the server
/// took must not be offered the message again when the fifth is retried.
/// </para>
/// <para>
/// The reply code travels and the reply text does not. The text is written by the remote server, routinely names the
/// recipient it is about, and would put an address into the first log line an operator reads about a failure; the three
/// digits are what tells a mailbox that does not exist from one that is over quota.
/// </para>
/// </remarks>
/// <param name="Address">The address the server answered about.</param>
/// <param name="ReplyCode">The three-digit reply code it answered with.</param>
/// <param name="Acceptance">What that answer means for this recipient.</param>
public sealed record MailRecipientReply(EmailAddress Address, int ReplyCode, MailRecipientAcceptance Acceptance)
{
    /// <summary>Gets whether the transmission that follows carries the message to this address.</summary>
    public bool IsAccepted => this.Acceptance == MailRecipientAcceptance.Accepted;

    /// <summary>Describes the reply by its code and its meaning, and never by the address it is about.</summary>
    /// <returns>The reply code and the acceptance alone.</returns>
    /// <remarks>
    /// The override exists to suppress the one a record would synthesize, which prints every property and would
    /// therefore put a recipient's address into any interpolated string, log template, or exception message that
    /// mentions a reply.
    /// </remarks>
    public override string ToString() => $"{this.ReplyCode} {this.Acceptance}";
}
