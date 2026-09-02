// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Carries the answers one walk of a message's structure produces.</summary>
/// <param name="Summary">What the message carries besides its body, counted and never described.</param>
/// <param name="Attachments">One entry per attachment, described and carrying none of what it holds.</param>
/// <param name="BodyTextParts">The textual parts the walk resolved as the message's body, in the order it found them.</param>
/// <param name="BodyIsEncrypted">Whether it was the message's own body that arrived inside a cryptographic envelope.</param>
/// <remarks>
/// <para>
/// Body text is selected from the parts the attachment rules already resolved as the body branch rather than from every
/// textual part in the message, because those are different sets: a plain-text file attached to an HTML message is a
/// <c>text/plain</c> part that is not the body, and indexing it as one would put a document's contents into the body
/// text of the mail carrying it. Running the walk once for both answers is also what keeps the two consistent — a
/// second walk under slightly different rules could classify a part as an attachment here and as a body there.
/// </para>
/// <para>
/// <paramref name="BodyIsEncrypted" /> is narrower than the summary's own encryption marker and deliberately so. The
/// summary says the message carries encrypted content somewhere, which is what a mailbox filter asks; this says the
/// body itself cannot be read here. A readable message that forwards an encrypted one as an attachment satisfies the
/// first and not the second.
/// </para>
/// </remarks>
internal sealed record MimeContentClassification(
    EmailAttachmentSummary Summary,
    IReadOnlyList<ExtractedEmailAttachment> Attachments,
    IReadOnlyList<TextPart> BodyTextParts,
    bool BodyIsEncrypted);
