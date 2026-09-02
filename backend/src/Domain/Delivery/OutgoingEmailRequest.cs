// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Scheduling;

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
public sealed record OutgoingEmailRequest
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

    private OutgoingEmailRequest(
        MailAccountIdentity account,
        OutgoingEmailRequester requester,
        IReadOnlyList<OutgoingRecipient> recipients,
        ZonedInstant? dueAt)
    {
        this.Account = account;
        this.Requester = requester;
        this.Recipients = recipients;
        this.DueAt = dueAt;
    }

    /// <summary>Gets the account the message is submitted through and sent as, named by its owner and its identifier.</summary>
    /// <remarks>
    /// The pair rather than the identifier alone, because an identifier names one account within its owner and the row
    /// this request becomes records whose send it was. The owner is the one the catalog resolved the account through, so
    /// the write that keeps the request supplies it without asking the account table again.
    /// </remarks>
    public MailAccountIdentity Account { get; }

    /// <summary>Gets the authored act that asked, which is what makes the same request twice one delivery.</summary>
    public OutgoingEmailRequester Requester { get; }

    /// <summary>Gets the people the message is offered to, each named once.</summary>
    public IReadOnlyList<OutgoingRecipient> Recipients { get; }

    /// <summary>Gets the time the author asked the message to leave at, or <see langword="null" /> when they asked for it to leave at once.</summary>
    /// <remarks>
    /// A time already past is not refused here. Whether a named time is still one somebody may ask for is a question
    /// about the clock, and this type holds no clock; what it does is carry the author's statement to the record
    /// unchanged, so the boundary that refused a time in the past and the record that holds one agree on what was asked
    /// for.
    /// </remarks>
    public ZonedInstant? DueAt { get; }

    /// <summary>Asks for one message to be submitted through an account and delivered to the recipients it names.</summary>
    /// <param name="account">The account the message is sent as, named by its owner and its identifier.</param>
    /// <param name="requester">The authored act asking.</param>
    /// <param name="recipients">The people the message is offered to.</param>
    /// <param name="dueAt">The time the message is to leave at, or <see langword="null" /> for as soon as it can.</param>
    /// <returns>The request to write down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requester" /> or <paramref name="recipients" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="recipients" /> is empty, holds more than <see cref="MaximumRecipientCount" /> entries, or names one mailbox more than once.</exception>
    public static OutgoingEmailRequest Create(
        MailAccountIdentity account,
        OutgoingEmailRequester requester,
        IReadOnlyList<OutgoingRecipient> recipients,
        ZonedInstant? dueAt = null)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(recipients);

        if (recipients.Count == 0)
        {
            throw new ArgumentException("An outgoing email names at least one recipient.", nameof(recipients));
        }

        if (recipients.Count > MaximumRecipientCount)
        {
            throw new ArgumentException(
                $"An outgoing email names at most {MaximumRecipientCount} recipients.",
                nameof(recipients));
        }

        var duplicatedAddressCount = recipients.Count
            - recipients.Select(recipient => recipient.Address).Distinct().Count();
        if (duplicatedAddressCount > 0)
        {
            // Counted rather than named: reporting which address repeated would put a recipient's address into an
            // exception message, and the caller already holds the list they passed.
            throw new ArgumentException(
                $"An outgoing email names each recipient once, and {duplicatedAddressCount} of them repeat.",
                nameof(recipients));
        }

        return new OutgoingEmailRequest(account, requester, [.. recipients], dueAt);
    }

    /// <summary>States the same send, held until the time the author named.</summary>
    /// <param name="dueAt">The time the message is to leave at.</param>
    /// <returns>The request, due at that time.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dueAt" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// It exists so that composing a message and deciding when it leaves stay two separate acts. Nothing about a due
    /// time reaches the MIME — a held message is byte-for-byte the message it would have been sent as at once — so the
    /// composer never sees one, and the boundary that took the author's time says so here instead.
    /// </remarks>
    public OutgoingEmailRequest HeldUntil(ZonedInstant dueAt)
    {
        ArgumentNullException.ThrowIfNull(dueAt);

        return new OutgoingEmailRequest(this.Account, this.Requester, this.Recipients, dueAt);
    }
}
