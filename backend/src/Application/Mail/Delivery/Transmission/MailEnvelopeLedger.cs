// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Transmission;

/// <summary>Collects what a submission server answered about each address, as it answers.</summary>
/// <remarks>
/// <para>
/// It is handed to the submission rather than returned by it, because its value is greatest exactly where there is no
/// return value: a server that stopped answering leaves the caller holding a durable record and no idea what the
/// recipients received. What the ledger says then is the one thing that settles it — a submission server is offered the
/// message body only once it has accepted at least one address, so an envelope that accepted nobody proves that
/// nothing was transmitted and the send can be attempted again without anybody receiving it twice.
/// </para>
/// <para>
/// It is filled by the adapter as the replies arrive and read by the caller afterwards. One submission fills one
/// ledger, and it is not safe for concurrent use — which matches the session it is used with, since one session serves
/// one caller at a time.
/// </para>
/// </remarks>
public sealed class MailEnvelopeLedger
{
    private readonly List<MailRecipientReply> replies = [];

    /// <summary>Gets what the server answered about each address it was offered, in the order it answered.</summary>
    public IReadOnlyList<MailRecipientReply> Replies => this.replies;

    /// <summary>Gets the addresses the server took, which are the ones an acknowledged transmission settles.</summary>
    public IReadOnlyList<MailRecipientReply> AcceptedRecipients =>
        [.. this.replies.Where(reply => reply.IsAccepted)];

    /// <summary>Gets whether any byte of the message body may already have reached the server.</summary>
    /// <remarks>
    /// It errs in the safe direction on purpose. Reporting a transmission that never happened costs a send that waits
    /// for a person; reporting none where the body went out would send the message a second time to everybody who had
    /// already received it.
    /// </remarks>
    public bool MayHaveReachedRecipients => this.replies.Any(reply => reply.IsAccepted);

    /// <summary>Writes down one answer the server gave about one address.</summary>
    /// <param name="reply">What it said.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reply" /> is <see langword="null" />.</exception>
    public void Record(MailRecipientReply reply)
    {
        ArgumentNullException.ThrowIfNull(reply);

        this.replies.Add(reply);
    }
}
