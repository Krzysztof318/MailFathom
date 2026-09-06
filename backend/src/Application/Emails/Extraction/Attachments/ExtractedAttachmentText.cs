// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction.Attachments;

/// <summary>Carries what one attachment yielded as text, and which of its pages yielded none.</summary>
/// <param name="Text">
/// The extracted text, which is untrusted content a sender composed. Nothing renders it as markup, nothing logs it,
/// and nothing downstream may treat it as anything but opaque characters.
/// </param>
/// <param name="PageCount">
/// How many pages the document was read as. A PDF page, a presentation slide, and a worksheet each count as one; a
/// word-processing document counts as one page whatever it prints as, because Office Open XML records no pagination
/// and reading one would mean laying the document out.
/// </param>
/// <param name="PagesWithoutText">
/// The one-based numbers of the pages that yielded no text at all, in ascending order. A page here is the exact target
/// a later optical-character-recognition pass would read, which is why it is a list of pages rather than a flag on the
/// document: a scanned page bound into an otherwise textual report is the ordinary case.
/// </param>
public sealed record ExtractedAttachmentText(
    string Text,
    int PageCount,
    IReadOnlyList<int> PagesWithoutText);
