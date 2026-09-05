// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Extraction.Attachments;

/// <summary>Names one document format an attachment may be recognized as.</summary>
/// <remarks>
/// The set is closed and is the whole of what recognition may conclude, which is what makes an attachment outside it a
/// stated refusal rather than an attempt. Recognizing a format is not the same as parsing one: the three legacy binary
/// formats are recognized so that skipping one can say what it was, and
/// <see cref="AttachmentDocumentFormats.IsExtracted" /> is what separates the two.
/// </remarks>
public enum AttachmentDocumentFormat
{
    /// <summary>A PDF document.</summary>
    Pdf = 0,

    /// <summary>An Office Open XML word-processing document, the format a <c>.docx</c> file carries.</summary>
    WordOpenXml = 1,

    /// <summary>An Office Open XML workbook, the format an <c>.xlsx</c> file carries.</summary>
    SpreadsheetOpenXml = 2,

    /// <summary>An Office Open XML presentation, the format a <c>.pptx</c> file carries.</summary>
    PresentationOpenXml = 3,

    /// <summary>The legacy binary Word document a <c>.doc</c> file carries.</summary>
    LegacyWord = 4,

    /// <summary>The legacy binary Excel workbook an <c>.xls</c> file carries.</summary>
    LegacySpreadsheet = 5,

    /// <summary>The legacy binary PowerPoint presentation a <c>.ppt</c> file carries.</summary>
    LegacyPresentation = 6,
}
