// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Domain.Delivery.Scheduling;

/// <summary>States one message an owner wrote once and asked to be sent again on every occasion a schedule names.</summary>
/// <remarks>
/// <para>
/// It is a declaration rather than a queue: nothing here is due, and nothing here is transmitted. Each occasion the
/// schedule reaches produces an outgoing record of its own, with its own idempotency identity, its own attempts, and
/// its own ending — so one Monday's provider outage is not the next Monday's, and a message refused for good stops one
/// occurrence rather than the declaration behind it.
/// </para>
/// <para>
/// The schedule is kept as the text an operator wrote and is parsed above this type, because the syntax belongs to the
/// dispatch mechanism this declaration rides rather than to the mail domain. What is refused where the declaration is
/// made is a schedule that does not parse, so nothing durable states an occasion nothing can resolve.
/// </para>
/// <para>
/// The message itself is not here. Every occurrence has to be a message of its own — a repeated <c>Message-ID</c> would
/// thread a year of Mondays as one message in every recipient's client — so what is kept is the authored draft, in the
/// content store beside every other piece of RFC 822 this system holds, and each occasion composes from it. The
/// addresses are here because an envelope is built from them and because they are what an operator reads back when they
/// ask who this declaration writes to.
/// </para>
/// <para>
/// It is derived personal data of the same kind an outgoing record is, and inherits the same retention, deletion, and
/// export obligations: it says who this mailbox's owner writes to, and how often.
/// </para>
/// </remarks>
public sealed record RecurringSend
{
    /// <summary>The greatest length a declared schedule may have, which bounds the column it is stored in.</summary>
    /// <remarks>The declared forms are short phrases such as <c>Daily at 09:00 Europe/Warsaw</c>, so the bound is well above anything the syntax can express and exists to keep an unbounded string out of the table.</remarks>
    public const int MaximumScheduleLength = 128;

    /// <summary>Gets what every occurrence and every later act refers to this declaration by.</summary>
    public required RecurringSendId Id { get; init; }

    /// <summary>Gets the account each occurrence is submitted through and sent as.</summary>
    public required MailAccountId AccountId { get; init; }

    /// <summary>Gets the authored act that asked for the declaration, which every occurrence's identity is composed from.</summary>
    public required OutgoingEmailRequester Requester { get; init; }

    /// <summary>Gets the people every occurrence is offered to, each named once.</summary>
    public required IReadOnlyList<OutgoingRecipient> Recipients { get; init; }

    /// <summary>Gets the schedule as it was declared, in the syntax the dispatch mechanism parses.</summary>
    public required string Schedule { get; init; }

    /// <summary>Gets how many bytes of MIME were stored as the draft every occurrence is composed from.</summary>
    public required long DraftByteLength { get; init; }

    /// <summary>Gets when the declaration was written down.</summary>
    public required DateTimeOffset DeclaredAt { get; init; }

    /// <summary>Gets the occasion this declaration last produced a message for, or <see langword="null" /> while it has produced none.</summary>
    /// <remarks>It is the occasion's own instant rather than the moment the message was composed, so a dispatch that noticed an occasion late does not move the declaration off the schedule it declared.</remarks>
    public DateTimeOffset? LastOccurrenceAt { get; init; }

    /// <summary>Gets the message the last occasion produced, or <see langword="null" /> while there has been none.</summary>
    /// <remarks>
    /// It is what makes one occurrence at a time enforceable rather than assumed. The next occasion asks what became of
    /// this message, and stands down while it is still queued — so a weekly send whose provider has been unreachable
    /// all week does not put a second week's copy behind the first.
    /// </remarks>
    public OutgoingEmailId? LastOccurrenceEmailId { get; init; }

    /// <summary>Gets when the declaration was stopped, or <see langword="null" /> while it still produces occurrences.</summary>
    /// <remarks>
    /// An instant rather than a flag, because stopping a recurring send is an act somebody took and the record of a send
    /// that stopped producing mail is worth reading in the order it happened. It is what makes cancelling the
    /// declaration a different act from cancelling one held occurrence: this stops every occasion still to come, and
    /// that one stops a message already written down.
    /// </remarks>
    public DateTimeOffset? CancelledAt { get; init; }

    /// <summary>Gets whether the declaration still produces an occurrence when its schedule comes round.</summary>
    public bool IsActive => this.CancelledAt is null;
}
