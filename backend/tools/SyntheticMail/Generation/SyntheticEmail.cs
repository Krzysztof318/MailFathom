// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation;

/// <summary>One message a seed produced, described in the terms a mail header carries.</summary>
/// <param name="MessageId">The value of the <c>Message-Id</c> header, without the angle brackets.</param>
/// <param name="InReplyTo">The message this one answers, or <see langword="null" /> when it opens a thread.</param>
/// <param name="References">The whole ancestry, oldest first, empty when the message opens a thread.</param>
/// <param name="Author">Who the message is from.</param>
/// <param name="CarbonCopies">The invented participants copied on it, possibly none.</param>
/// <param name="Subject">The subject line.</param>
/// <param name="SentAt">When the message claims to have been sent.</param>
/// <param name="Body">What it says, and in which MIME shape.</param>
/// <param name="Attachment">What it carries, or <see langword="null" /> when it carries nothing.</param>
/// <param name="AiOrigin">What the message was generated under, or <see langword="null" /> when the seeded vocabulary wrote it.</param>
/// <remarks>
/// This is deliberately not a <c>MimeMessage</c>. A description can be asserted against, compared between two runs of
/// the same seed, and printed by a dry run without a mail server being involved; composing it into MIME is a separate
/// step that happens once, immediately before delivery.
/// </remarks>
internal sealed record SyntheticEmail(
    string MessageId,
    string? InReplyTo,
    IReadOnlyList<string> References,
    SyntheticParticipant Author,
    IReadOnlyList<SyntheticParticipant> CarbonCopies,
    string Subject,
    DateTimeOffset SentAt,
    SyntheticEmailBody Body,
    SyntheticEmailAttachment? Attachment,
    SyntheticEmailAiOrigin? AiOrigin);
