// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Names what a counted place inside an attachment is, in the word its own format uses.</summary>
/// <remarks>
/// <para>
/// The word is what makes the number usable: telling somebody their contract matched on <em>page 4</em> sends them to a
/// page, and telling them a spreadsheet matched on <em>page 4</em> sends them nowhere.
/// </para>
/// <para>
/// The transport carries its own enumeration for the reason <see cref="EmailRetrievalMode" /> does: the member names are
/// the published wire values, so they belong to the boundary that publishes them.
/// </para>
/// </remarks>
internal enum AttachmentSegmentKind
{
    /// <summary>A page of a paginated document, or the whole of a word-processing document, numbered from one.</summary>
    Page = 0,

    /// <summary>A slide of a presentation, numbered from one.</summary>
    Slide = 1,

    /// <summary>A worksheet of a workbook, numbered from one.</summary>
    Sheet = 2,
}
