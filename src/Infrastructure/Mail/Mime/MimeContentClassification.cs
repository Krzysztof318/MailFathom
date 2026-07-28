// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Emails;
using MimeKit;

namespace MailMcp.Infrastructure.Mail.Mime;

/// <summary>Carries both answers one walk of a message's structure produces.</summary>
/// <param name="Attachments">What the message carries besides its body.</param>
/// <param name="BodyTextParts">The textual parts the walk resolved as the message's body, in the order it found them.</param>
/// <remarks>
/// Body text is selected from the parts the attachment rules already resolved as the body branch rather than from every
/// textual part in the message, because those are different sets: a plain-text file attached to an HTML message is a
/// <c>text/plain</c> part that is not the body, and indexing it as one would put a document's contents into the body
/// text of the mail carrying it. Running the walk once for both answers is also what keeps the two consistent — a
/// second walk under slightly different rules could classify a part as an attachment here and as a body there.
/// </remarks>
internal sealed record MimeContentClassification(
    EmailAttachmentSummary Attachments,
    IReadOnlyList<TextPart> BodyTextParts);
