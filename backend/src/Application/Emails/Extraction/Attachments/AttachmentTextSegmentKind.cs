// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction.Attachments;

/// <summary>Names what one counted place inside a document is, in the word the format's own reader uses for it.</summary>
/// <remarks>
/// The three are what the formats MailFathom reads actually divide into, and a citation is worth only as much as the
/// word it uses: telling somebody their contract matched on <em>page 4</em> sends them to a page, and telling them a
/// spreadsheet matched on <em>page 4</em> sends them nowhere. A word-processing document is one <see cref="Page" />
/// because Office Open XML records no pagination, which is the same answer
/// <see cref="ExtractedAttachmentText.PageCount" /> already gives.
/// </remarks>
public enum AttachmentTextSegmentKind
{
    /// <summary>A page of a paginated document, or the whole of a word-processing document, numbered from one.</summary>
    Page = 0,

    /// <summary>A slide of a presentation, numbered from one.</summary>
    Slide = 1,

    /// <summary>A worksheet of a workbook, numbered from one and named where the format records a name.</summary>
    Sheet = 2,
}
