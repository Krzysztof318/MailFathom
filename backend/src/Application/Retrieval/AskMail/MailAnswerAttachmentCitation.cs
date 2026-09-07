// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Application.Retrieval.AskMail;

/// <summary>One file inside a cited message that an answer was drawn from, named so the reader can open it.</summary>
/// <remarks>
/// <para>
/// The walk position rather than the file name is what resolves: it is the coordinate the single-message read and the
/// download route are addressed with, and a name is text a sender chose, which is neither unique within a message nor
/// required to be present at all. The name travels beside it because it is what a person recognizes the file by.
/// </para>
/// <para>
/// It carries no extract, on the same rule the message citation follows: the passage has already reached a provider, and
/// republishing it here would return mail content from a tool whose result is an answer. What it adds over the message
/// citation alone is the place — a claim traced to page fourteen of a report is checkable, and the same claim traced to
/// "an attachment" of a two-hundred-page report is not.
/// </para>
/// <para>
/// <see cref="Kind" /> is published rather than kept, because a reader weighing a claim needs to know whether its source
/// was a document somebody wrote or a machine's account of a picture. The second is a guess about an image and is worth
/// what a guess is worth.
/// </para>
/// </remarks>
/// <param name="AttachmentPosition">The zero-based walk position of the attachment within its message.</param>
/// <param name="FileName">The normalized file name, or <see langword="null" /> where the part carried no usable name.</param>
/// <param name="Kind">Whether the words drawn on were the file's own or a model's account of a picture.</param>
/// <param name="Segment">The page, slide, or sheet they were read from, or <see langword="null" /> where the reading recorded no boundaries.</param>
public sealed record MailAnswerAttachmentCitation(
    int AttachmentPosition,
    string? FileName,
    AttachmentTextKind Kind,
    AttachmentTextSegment? Segment);
