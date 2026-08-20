// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Domain.Delivery;

namespace MailFathom.Host.Api;

/// <summary>What a deployment is asked when one send is to be withdrawn.</summary>
/// <param name="OutgoingEmail">The identifier the outbox reading reports for the send.</param>
internal sealed record OutboxCancellationRequest(Guid? OutgoingEmail);

/// <summary>What a deployment is asked when one send is to be offered again.</summary>
/// <param name="OutgoingEmail">The identifier the outbox reading reports for the send.</param>
/// <param name="RefusalRestated">Whether the caller has restated a permanent refusal, which is what a refused send needs before it is offered again.</param>
internal sealed record OutboxRequeueRequest(Guid? OutgoingEmail, bool RefusalRestated);

/// <summary>How much stands at each stage of an outbox.</summary>
/// <param name="Stages">One count per declared stage, in the order the stages are declared.</param>
/// <param name="OutstandingCount">How many sends nothing has finished with, which is the depth an operator means.</param>
internal sealed record OutboxSummaryResponse(IReadOnlyList<OutboxStageCountResponse> Stages, int OutstandingCount)
{
    /// <summary>Describes the summary as the administrative surface reports it.</summary>
    /// <param name="summary">The summary read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="summary" /> is <see langword="null" />.</exception>
    internal static OutboxSummaryResponse For(OutboxSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        return new OutboxSummaryResponse(
            [.. summary.Stages.Select(stage => new OutboxStageCountResponse(stage.Stage.ToString(), stage.Count))],
            summary.OutstandingCount);
    }
}

/// <summary>How many sends stand at one stage.</summary>
/// <param name="Stage">The stage.</param>
/// <param name="Count">How many sends stand at it.</param>
internal sealed record OutboxStageCountResponse(string Stage, int Count);

/// <summary>One page of what a deployment has been asked to send.</summary>
/// <param name="Sends">The sends, ordered by when each one was written down, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end.</param>
internal sealed record OutboxPageResponse(IReadOnlyList<OutboxEntryResponse> Sends, string? NextCursor)
{
    /// <summary>Describes one page as the administrative surface reports it.</summary>
    /// <param name="page">The page read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static OutboxPageResponse For(OutboxPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new OutboxPageResponse(
            [.. page.Sends.Select(OutboxEntryResponse.For)],
            page.NextCursor?.Encode());
    }
}

/// <summary>One recorded send as a listing names it.</summary>
/// <param name="OutgoingEmail">The identifier a decision names it by.</param>
/// <param name="Account">The account the message is sent from.</param>
/// <param name="Stage">How far along its submission sequence it has durably reached.</param>
/// <param name="Origin">What asked for the send.</param>
/// <param name="AttemptCount">How many attempts have been handed out for it.</param>
/// <param name="MimeByteLength">How many bytes of MIME are stored for the message.</param>
/// <param name="RecordedAt">When the send was written down.</param>
/// <param name="StageChangedAt">When it last moved between stages.</param>
/// <param name="AvailableAt">The instant from which it may be claimed again.</param>
/// <param name="LastFailureCode">The code identifying what the last attempt ended in, absent where the row records none.</param>
/// <param name="LastReplyCode">The reply code the server answered with, absent where it answered none.</param>
internal sealed record OutboxEntryResponse(
    Guid OutgoingEmail,
    string Account,
    string Stage,
    string Origin,
    int AttemptCount,
    long MimeByteLength,
    DateTimeOffset RecordedAt,
    DateTimeOffset StageChangedAt,
    DateTimeOffset AvailableAt,
    int? LastFailureCode,
    int? LastReplyCode)
{
    /// <summary>Describes one listing entry as the administrative surface reports it.</summary>
    /// <param name="entry">The entry read.</param>
    /// <returns>The response record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="entry" /> is <see langword="null" />.</exception>
    internal static OutboxEntryResponse For(OutboxEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new OutboxEntryResponse(
            entry.OutgoingEmailId.Value,
            entry.AccountId.Value,
            entry.Stage.ToString(),
            entry.Origin.ToString(),
            entry.AttemptCount,
            entry.MimeByteLength,
            entry.RecordedAt,
            entry.StageChangedAt,
            entry.AvailableAt,
            entry.LastFailure?.Value,
            entry.LastReplyCode);
    }
}

/// <summary>One recorded send, with what each of its recipients was told.</summary>
/// <param name="OutgoingEmail">The identifier a decision names it by.</param>
/// <param name="Account">The account the message is sent from.</param>
/// <param name="Stage">How far along its submission sequence it has durably reached.</param>
/// <param name="Origin">What asked for the send.</param>
/// <param name="Requester">The identity the send is idempotent under, which is MailFathom's own name for what asked.</param>
/// <param name="AttemptCount">How many attempts have been handed out for it.</param>
/// <param name="MimeByteLength">How many bytes of MIME are stored for the message.</param>
/// <param name="RecordedAt">When the send was written down.</param>
/// <param name="StageChangedAt">When it last moved between stages.</param>
/// <param name="AvailableAt">The instant from which it may be claimed again.</param>
/// <param name="LastFailureCode">The code identifying what the last attempt ended in, absent where the row records none.</param>
/// <param name="LastReplyCode">The reply code the server answered with, absent where it answered none.</param>
/// <param name="Recipients">Who the message is offered to, and what each of them was told.</param>
internal sealed record OutboxSendResponse(
    Guid OutgoingEmail,
    string Account,
    string Stage,
    string Origin,
    string Requester,
    int AttemptCount,
    long MimeByteLength,
    DateTimeOffset RecordedAt,
    DateTimeOffset StageChangedAt,
    DateTimeOffset AvailableAt,
    int? LastFailureCode,
    int? LastReplyCode,
    IReadOnlyList<OutboxRecipientResponse> Recipients)
{
    /// <summary>Describes one send as the administrative surface reports it.</summary>
    /// <param name="record">The record read.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="record" /> is <see langword="null" />.</exception>
    internal static OutboxSendResponse For(OutgoingEmailRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new OutboxSendResponse(
            record.Id.Value,
            record.AccountId.Value,
            record.Stage.ToString(),
            record.Requester.Origin.ToString(),
            record.Requester.Identity,
            record.AttemptCount,
            record.MimeByteLength,
            record.RecordedAt,
            record.StageChangedAt,
            record.AvailableAt,
            record.LastFailure?.Value,
            record.LastReplyCode,
            [.. record.Recipients.Select(OutboxRecipientResponse.For)]);
    }
}

/// <summary>One person a message is offered to, and what the server said about them.</summary>
/// <param name="Address">The address the envelope names.</param>
/// <param name="Role">Whether the address is on the message as a recipient, a copy, or a blind copy.</param>
/// <param name="Status">What the last attempt settled about it.</param>
/// <param name="LastReplyCode">The reply code the server answered for this address, absent where it answered none.</param>
/// <param name="AnsweredAt">When that answer was recorded, absent where none was.</param>
internal sealed record OutboxRecipientResponse(
    string Address,
    string Role,
    string Status,
    int? LastReplyCode,
    DateTimeOffset? AnsweredAt)
{
    /// <summary>Describes one recipient as the administrative surface reports it.</summary>
    /// <param name="outcome">The outcome read.</param>
    /// <returns>The response record.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="outcome" /> is <see langword="null" />.</exception>
    internal static OutboxRecipientResponse For(OutgoingRecipientOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new OutboxRecipientResponse(
            outcome.Recipient.Address.Address,
            outcome.Recipient.Role.ToString(),
            outcome.Status.ToString(),
            outcome.LastReplyCode,
            outcome.AnsweredAt);
    }
}

/// <summary>What became of a send an operator decided about.</summary>
/// <param name="OutgoingEmail">The send the decision named.</param>
/// <param name="Outcome">What happened: <c>Accepted</c>, <c>RecordUnknown</c>, <c>StageDoesNotAllowIt</c>, <c>AttemptUnderWay</c>, or <c>RefusalNotRestated</c>.</param>
internal sealed record OutboxDecisionResponse(Guid OutgoingEmail, string Outcome)
{
    /// <summary>Describes one decision as the administrative surface reports it.</summary>
    /// <param name="outgoingEmailId">The send the decision named.</param>
    /// <param name="outcome">What happened.</param>
    /// <returns>The response body.</returns>
    internal static OutboxDecisionResponse For(OutgoingEmailId outgoingEmailId, OutboxDecisionOutcome outcome) =>
        new(outgoingEmailId.Value, outcome.ToString());
}
