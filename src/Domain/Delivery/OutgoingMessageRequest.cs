// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Domain.Delivery;

/// <summary>States one send somebody authored, in the form it is written down before any SMTP command is issued.</summary>
/// <remarks>
/// <para>
/// A new message, a reply, and a forward are one request shape rather than three. Each needs the same answers — which
/// account sends, who it goes to, and what asked — and the difference between them is entirely in the MIME, which this
/// type does not carry and never sees.
/// </para>
/// <para>
/// The sending account is named and the sending address is not. A message is sent as the account that sends it and its
/// <c>From</c> address is derived from that account's own configuration, so there is nothing here an input path could
/// set to send as somebody else.
/// </para>
/// <para>
/// Every recipient appears once. An envelope offers one address one time, so the same mailbox written in two headers is
/// one <c>RCPT TO</c> and would otherwise be a second copy in that person's mailbox — which is refused where the request
/// is built rather than deduplicated silently, because a caller naming one person twice has described something they
/// did not mean.
/// </para>
/// </remarks>
public sealed record OutgoingMessageRequest
{
    /// <summary>The greatest number of people one message may be addressed to.</summary>
    /// <remarks>
    /// It is the same order as the bound a received message's addresses are stored under, and it is here for the reason
    /// every bound in this system is: a recipient list is written into a table and offered one command at a time, so an
    /// unbounded one is an unbounded insert followed by an unbounded conversation with a server. Nothing legitimate
    /// approaches it — the parent feature refuses mailing-list behaviour outright — so a request that does is a caller
    /// that has assembled something it did not mean.
    /// </remarks>
    public const int MaximumRecipientCount = 256;

    private OutgoingMessageRequest(
        MailAccountId accountId,
        OutgoingMessageRequester requester,
        IReadOnlyList<OutgoingRecipient> recipients)
    {
        this.AccountId = accountId;
        this.Requester = requester;
        this.Recipients = recipients;
    }

    /// <summary>Gets the account the message is submitted through and sent as.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the authored act that asked, which is what makes the same request twice one delivery.</summary>
    public OutgoingMessageRequester Requester { get; }

    /// <summary>Gets the people the message is offered to, each named once.</summary>
    public IReadOnlyList<OutgoingRecipient> Recipients { get; }

    /// <summary>Asks for one message to be submitted through an account and delivered to the recipients it names.</summary>
    /// <param name="accountId">The account the message is sent as.</param>
    /// <param name="requester">The authored act asking.</param>
    /// <param name="recipients">The people the message is offered to.</param>
    /// <returns>The request to write down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requester" /> or <paramref name="recipients" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="recipients" /> is empty, holds more than <see cref="MaximumRecipientCount" /> entries, or names one mailbox more than once.</exception>
    public static OutgoingMessageRequest Create(
        MailAccountId accountId,
        OutgoingMessageRequester requester,
        IReadOnlyList<OutgoingRecipient> recipients)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(recipients);

        if (recipients.Count == 0)
        {
            throw new ArgumentException("An outgoing message names at least one recipient.", nameof(recipients));
        }

        if (recipients.Count > MaximumRecipientCount)
        {
            throw new ArgumentException(
                $"An outgoing message names at most {MaximumRecipientCount} recipients.",
                nameof(recipients));
        }

        var duplicatedAddressCount = recipients.Count
            - recipients.Select(recipient => recipient.Address).Distinct().Count();
        if (duplicatedAddressCount > 0)
        {
            // Counted rather than named: reporting which address repeated would put a recipient's address into an
            // exception message, and the caller already holds the list they passed.
            throw new ArgumentException(
                $"An outgoing message names each recipient once, and {duplicatedAddressCount} of them repeat.",
                nameof(recipients));
        }

        return new OutgoingMessageRequest(accountId, requester, [.. recipients]);
    }
}
