// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery;

/// <summary>Reports where one recipient of an outgoing email stands, and what the server last said about them.</summary>
/// <remarks>
/// <para>
/// A message is offered per recipient and answered per recipient, so a refusal is one person's rather than the
/// message's: a mistyped address among five must not stop the other four, and the four who received it must not be
/// offered the message again when the fifth is retried. That is the whole reason the outcome is recorded at this
/// grain.
/// </para>
/// <para>
/// The reply code is the three digits the server answered with and nothing else. Recording it is what lets an operator
/// tell a mailbox that does not exist from one that is over quota without the reply text — which is written by the
/// remote server, may name the recipient, and is therefore never kept.
/// </para>
/// </remarks>
public sealed record OutgoingRecipientOutcome
{
    private OutgoingRecipientOutcome(
        OutgoingRecipient recipient,
        OutgoingRecipientStatus status,
        int? lastReplyCode,
        DateTimeOffset? answeredAt)
    {
        this.Recipient = recipient;
        this.Status = status;
        this.LastReplyCode = lastReplyCode;
        this.AnsweredAt = answeredAt;
    }

    /// <summary>Gets the recipient this outcome is about.</summary>
    public OutgoingRecipient Recipient { get; }

    /// <summary>Gets whether the next attempt offers this recipient, and why it does not when it does not.</summary>
    public OutgoingRecipientStatus Status { get; }

    /// <summary>Gets the reply code the server last answered about this recipient, or <see langword="null" /> while it has answered nothing.</summary>
    public int? LastReplyCode { get; }

    /// <summary>Gets when that answer was recorded, or <see langword="null" /> while there has been none.</summary>
    public DateTimeOffset? AnsweredAt { get; }

    /// <summary>Gets whether the recipient is one a later attempt still offers the message to.</summary>
    public bool IsOutstanding => this.Status == OutgoingRecipientStatus.Pending;

    /// <summary>States a recipient nothing has answered about yet, which is how every recipient of a new record starts.</summary>
    /// <param name="recipient">The recipient the message is addressed to.</param>
    /// <returns>The outcome a recipient is recorded with before any attempt.</returns>
    public static OutgoingRecipientOutcome Unanswered(OutgoingRecipient recipient) => new(
        recipient,
        OutgoingRecipientStatus.Pending,
        lastReplyCode: null,
        answeredAt: null);

    /// <summary>States what one attempt settled about a recipient.</summary>
    /// <param name="recipient">The recipient the server answered about.</param>
    /// <param name="status">Where the answer leaves them.</param>
    /// <param name="replyCode">The three-digit reply code the server answered with.</param>
    /// <param name="answeredAt">When the answer was recorded.</param>
    /// <returns>The outcome to write down.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="status" /> is not a declared status, or when <paramref name="replyCode" /> is not a three-digit reply code.</exception>
    public static OutgoingRecipientOutcome Answered(
        OutgoingRecipient recipient,
        OutgoingRecipientStatus status,
        int replyCode,
        DateTimeOffset answeredAt)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "A recipient outcome names one of the declared statuses.");
        }

        // RFC 5321 makes every reply exactly three digits, so anything else is a value assembled wrongly rather than a
        // server this system has not met. It is refused before it is durable, because the record is read afterwards by
        // an operator deciding whether a send is worth retrying.
        if (replyCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(
                nameof(replyCode),
                replyCode,
                "An SMTP reply code is a three-digit number.");
        }

        return new OutgoingRecipientOutcome(recipient, status, replyCode, answeredAt);
    }

    /// <summary>Restores an outcome exactly as a stored row holds it.</summary>
    /// <param name="recipient">The recipient the row is about.</param>
    /// <param name="status">The stored status.</param>
    /// <param name="lastReplyCode">The stored reply code, where one was recorded.</param>
    /// <param name="answeredAt">When that reply was recorded, where one was.</param>
    /// <returns>The outcome the row states.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="status" /> is not a declared status.</exception>
    /// <remarks>
    /// It accepts the parts loose because a stored row hands them back that way, and it validates only the status: the
    /// reply code is diagnostic detail an older row may hold in a shape this build would no longer write, and refusing
    /// the whole record over it would strand a send rather than let it be read.
    /// </remarks>
    public static OutgoingRecipientOutcome Create(
        OutgoingRecipient recipient,
        OutgoingRecipientStatus status,
        int? lastReplyCode,
        DateTimeOffset? answeredAt)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(
                nameof(status),
                status,
                "A recipient outcome names one of the declared statuses.");
        }

        return new OutgoingRecipientOutcome(recipient, status, lastReplyCode, answeredAt);
    }
}
