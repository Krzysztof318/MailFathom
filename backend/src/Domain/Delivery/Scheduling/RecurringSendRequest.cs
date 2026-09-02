// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Domain.Delivery.Scheduling;

/// <summary>States one message somebody asked to have sent again on every occasion a schedule names.</summary>
/// <remarks>
/// <para>
/// It is the outgoing request's shape with a schedule in place of a due time, and deliberately so: what repeats is an
/// ordinary send, and everything that decides whether one may be written down — the account, the people it is offered
/// to, and the act asking — decides it here identically. What is different is that this describes many messages, which
/// is why the recipients are validated once here rather than once per occasion.
/// </para>
/// <para>
/// The schedule arrives as the text an operator or a caller wrote. Whether it names occasions this system can run is
/// established before the declaration is durable, by the mechanism that owns the syntax, so nothing here parses it and
/// nothing stored states a repetition nobody can resolve.
/// </para>
/// </remarks>
public sealed record RecurringSendRequest
{
    private RecurringSendRequest(
        MailAccountIdentity account,
        OutgoingEmailRequester requester,
        IReadOnlyList<OutgoingRecipient> recipients,
        string schedule)
    {
        this.Account = account;
        this.Requester = requester;
        this.Recipients = recipients;
        this.Schedule = schedule;
    }

    /// <summary>Gets the account every occurrence is submitted through and sent as, named by its owner and its identifier.</summary>
    /// <remarks>
    /// The pair, for the reason <see cref="OutgoingEmailRequest.Account" /> is one: the declaration becomes a row that
    /// records whose repetition it is, and the owner comes with the account the catalog resolved rather than from a
    /// second read of the account table.
    /// </remarks>
    public MailAccountIdentity Account { get; }

    /// <summary>Gets the authored act asking, which is what makes the same declaration twice one declaration.</summary>
    public OutgoingEmailRequester Requester { get; }

    /// <summary>Gets the people every occurrence is offered to, each named once.</summary>
    public IReadOnlyList<OutgoingRecipient> Recipients { get; }

    /// <summary>Gets the repetition as it was written.</summary>
    public string Schedule { get; }

    /// <summary>Asks for one message to be sent again on every occasion a schedule names.</summary>
    /// <param name="account">The account every occurrence is sent as, named by its owner and its identifier.</param>
    /// <param name="requester">The authored act asking.</param>
    /// <param name="recipients">The people every occurrence is offered to.</param>
    /// <param name="schedule">The repetition as it was written.</param>
    /// <returns>The declaration to write down.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="requester" /> or <paramref name="recipients" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="recipients" /> is empty, holds more than <see cref="OutgoingEmailRequest.MaximumRecipientCount" /> entries, or names one mailbox more than once, or when <paramref name="schedule" /> is blank, carries a control character, or is longer than <see cref="RecurringSend.MaximumScheduleLength" />.</exception>
    public static RecurringSendRequest Create(
        MailAccountIdentity account,
        OutgoingEmailRequester requester,
        IReadOnlyList<OutgoingRecipient> recipients,
        string schedule)
    {
        ArgumentNullException.ThrowIfNull(requester);
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedule);

        // Every occasion becomes an outgoing request, so a declaration this system could not write one message for is
        // refused where somebody is still present to be told rather than every week from a worker.
        var occurrence = OutgoingEmailRequest.Create(account, requester, recipients);

        var trimmedSchedule = schedule.Trim();

        if (trimmedSchedule.Length > RecurringSend.MaximumScheduleLength)
        {
            throw new ArgumentException(
                $"A declared schedule may be at most {RecurringSend.MaximumScheduleLength} characters long.",
                nameof(schedule));
        }

        // A control character would make the declaration unreadable in the answer an operator asks what repeats with,
        // and it is never part of a schedule anybody wrote.
        if (trimmedSchedule.Any(char.IsControl))
        {
            throw new ArgumentException("A declared schedule cannot contain a control character.", nameof(schedule));
        }

        return new RecurringSendRequest(account, requester, occurrence.Recipients, trimmedSchedule);
    }
}
