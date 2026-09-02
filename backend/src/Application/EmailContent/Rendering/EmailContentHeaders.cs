// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.EmailContent.Rendering;

/// <summary>Carries the normalized headers one email displays above its body.</summary>
/// <param name="Subject">The decoded subject, or <see langword="null" /> when the message carried none.</param>
/// <param name="SentAt">The <c>Date</c> header in UTC, or <see langword="null" /> when the message carried none or wrote an unparseable one.</param>
/// <param name="ReceivedAt">When the last receiving hop recorded the message in UTC, or <see langword="null" /> when no <c>Received</c> header carried a usable date.</param>
/// <param name="Participants">Every usable address the message wrote, each paired with the header it appeared in.</param>
/// <param name="ThreadReferences">The identifiers that place the message in a conversation.</param>
/// <remarks>
/// <para>
/// These are read from the stored raw MIME during the parse that produces the body, rather than from the columns the
/// mailbox listing is served out of. The row keeps only the comparison forms a filter needs, so display names, the
/// <c>Bcc</c> a message may carry for its own recipient, and the <c>Sender</c> header exist nowhere else — a reader
/// shown the listing's copy would be shown a narrower message than the one that arrived.
/// </para>
/// <para>
/// Every value here is mail content and personal data. Nothing in this record may be written to a log; only the counts
/// and the fact that a header was absent are safe to report.
/// </para>
/// </remarks>
public sealed record EmailContentHeaders(
    string? Subject,
    DateTimeOffset? SentAt,
    DateTimeOffset? ReceivedAt,
    IReadOnlyList<EmailParticipant> Participants,
    EmailThreadReferences ThreadReferences);
