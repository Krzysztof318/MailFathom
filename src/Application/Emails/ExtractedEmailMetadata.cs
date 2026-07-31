// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Domain.Emails;

namespace MailMcp.Application.Emails;

/// <summary>Carries the normalized metadata read out of one message's raw MIME.</summary>
/// <param name="OccurrenceId">The stable remote occurrence the metadata was read from.</param>
/// <param name="Subject">The decoded subject, or <see langword="null" /> when the message carried none.</param>
/// <param name="SentAt">The <c>Date</c> header in UTC, or <see langword="null" /> when the message carried none or wrote an unparseable one.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded the message in UTC, or <see langword="null" /> when no <c>Received</c> header carried a usable date.</param>
/// <param name="Participants">Every usable address the message wrote, each paired with the header it appeared in.</param>
/// <param name="ThreadReferences">The identifiers that place the message in a conversation.</param>
/// <param name="Attachments">What the message carries besides its body.</param>
/// <param name="Text">The searchable text the body yielded, or the reason it yielded none.</param>
/// <remarks>
/// Participants, subject, thread identifiers, and body text are personal data by default. Nothing in this record may be
/// written to a log; only counts and the occurrence identity are safe to report.
/// </remarks>
public sealed record ExtractedEmailMetadata(
    EmailOccurrenceId OccurrenceId,
    string? Subject,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<EmailParticipant> Participants,
    EmailThreadReferences ThreadReferences,
    EmailAttachmentSummary Attachments,
    ExtractedEmailText Text);
