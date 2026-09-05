// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Extraction.Attachments;

/// <summary>Recognizes the document format an attachment declares, and says which of them are parsed.</summary>
/// <remarks>
/// <para>
/// Recognition reads the declared media type first and the file name's extension only where the media type says
/// nothing. Both are the sender's to write, so neither is evidence about the bytes — what recognition decides is which
/// parser is offered the content, and every parser treats what it is handed as hostile regardless.
/// </para>
/// <para>
/// The extension fallback exists because a generic <c>application/octet-stream</c> over a correctly named file is the
/// ordinary shape of a mail-borne document rather than an edge case. It is a fallback and never an override: a part
/// declaring a recognized media type is that format whatever it is called.
/// </para>
/// </remarks>
public static class AttachmentDocumentFormats
{
    private static readonly Dictionary<string, AttachmentDocumentFormat> FormatsByMediaType =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["application/pdf"] = AttachmentDocumentFormat.Pdf,
            ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = AttachmentDocumentFormat.WordOpenXml,
            ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = AttachmentDocumentFormat.SpreadsheetOpenXml,
            ["application/vnd.openxmlformats-officedocument.presentationml.presentation"] = AttachmentDocumentFormat.PresentationOpenXml,
            ["application/msword"] = AttachmentDocumentFormat.LegacyWord,
            ["application/vnd.ms-excel"] = AttachmentDocumentFormat.LegacySpreadsheet,
            ["application/vnd.ms-powerpoint"] = AttachmentDocumentFormat.LegacyPresentation,
        };

    private static readonly Dictionary<string, AttachmentDocumentFormat> FormatsByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = AttachmentDocumentFormat.Pdf,
            [".docx"] = AttachmentDocumentFormat.WordOpenXml,
            [".xlsx"] = AttachmentDocumentFormat.SpreadsheetOpenXml,
            [".pptx"] = AttachmentDocumentFormat.PresentationOpenXml,
            [".doc"] = AttachmentDocumentFormat.LegacyWord,
            [".xls"] = AttachmentDocumentFormat.LegacySpreadsheet,
            [".ppt"] = AttachmentDocumentFormat.LegacyPresentation,
        };

    /// <summary>Gets the formats text is extracted from, as opposed to those recognition only names.</summary>
    public static IReadOnlyList<AttachmentDocumentFormat> Extracted { get; } =
    [
        AttachmentDocumentFormat.Pdf,
        AttachmentDocumentFormat.WordOpenXml,
        AttachmentDocumentFormat.SpreadsheetOpenXml,
        AttachmentDocumentFormat.PresentationOpenXml,
    ];

    /// <summary>Recognizes what an attachment declares itself to be.</summary>
    /// <param name="mediaType">The part's declared media type.</param>
    /// <param name="fileName">The normalized file name, or <see langword="null" /> when the part is unnamed.</param>
    /// <returns>The recognized format, or <see langword="null" /> when nothing in the declaration names one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="mediaType" /> is <see langword="null" />.</exception>
    public static AttachmentDocumentFormat? Recognize(string mediaType, AttachmentFileName? fileName)
    {
        ArgumentNullException.ThrowIfNull(mediaType);

        if (FormatsByMediaType.TryGetValue(mediaType.Trim(), out var declared))
        {
            return declared;
        }

        if (fileName is not { } named)
        {
            return null;
        }

        var extension = Path.GetExtension(named.Value);

        return FormatsByExtension.TryGetValue(extension, out var fromExtension) ? fromExtension : null;
    }

    /// <summary>States whether text is extracted from a recognized format, or whether recognition only names it.</summary>
    /// <param name="format">The recognized format.</param>
    /// <returns><see langword="true" /> when a parser reads it; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The three legacy binary formats are the ones this answers <see langword="false" /> for. They are OLE compound
    /// files, and no permissively licensed .NET parser reads all three — so an attachment carrying one is reported as a
    /// format nothing here parses rather than parsed by something the licence register could not admit.
    /// </remarks>
    public static bool IsExtracted(AttachmentDocumentFormat format) => Extracted.Contains(format);
}
