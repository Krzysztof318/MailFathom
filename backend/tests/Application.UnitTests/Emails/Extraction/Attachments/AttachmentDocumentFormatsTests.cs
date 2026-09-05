// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Extraction.Attachments;

/// <summary>Covers which attachments are recognized as documents, and which of those a parser is offered.</summary>
/// <remarks>
/// Recognition decides whether a sender's bytes reach a document parser at all, so what it admits is the outer edge of
/// this system's largest attack surface. The tests below are therefore as much about what is refused as about what is
/// named.
/// </remarks>
public sealed class AttachmentDocumentFormatsTests
{
    /// <summary>The declared media type is the first thing read, because it is what the format was actually announced as.</summary>
    [Theory]
    [InlineData("application/pdf", AttachmentDocumentFormat.Pdf)]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", AttachmentDocumentFormat.WordOpenXml)]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", AttachmentDocumentFormat.SpreadsheetOpenXml)]
    [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation", AttachmentDocumentFormat.PresentationOpenXml)]
    [InlineData("application/msword", AttachmentDocumentFormat.LegacyWord)]
    [InlineData("application/vnd.ms-excel", AttachmentDocumentFormat.LegacySpreadsheet)]
    [InlineData("application/vnd.ms-powerpoint", AttachmentDocumentFormat.LegacyPresentation)]
    [InlineData("application/vnd.oasis.opendocument.text", AttachmentDocumentFormat.OpenDocumentText)]
    [InlineData("application/vnd.oasis.opendocument.spreadsheet", AttachmentDocumentFormat.OpenDocumentSpreadsheet)]
    [InlineData("application/vnd.oasis.opendocument.presentation", AttachmentDocumentFormat.OpenDocumentPresentation)]
    public void Recognize_ADeclaredDocumentMediaType_NamesTheFormatItDeclares(
        string mediaType,
        AttachmentDocumentFormat expected)
    {
        // Arrange, Act
        var recognized = AttachmentDocumentFormats.Recognize(mediaType, FileNamed("anything.bin"));

        // Assert
        Assert.Equal(expected, recognized);
    }

    /// <summary>A generic media type over a named file is the ordinary shape of a mail-borne document rather than an edge case.</summary>
    [Theory]
    [InlineData("contract.pdf", AttachmentDocumentFormat.Pdf)]
    [InlineData("Contract.PDF", AttachmentDocumentFormat.Pdf)]
    [InlineData("terms.docx", AttachmentDocumentFormat.WordOpenXml)]
    [InlineData("ledger.xlsx", AttachmentDocumentFormat.SpreadsheetOpenXml)]
    [InlineData("deck.pptx", AttachmentDocumentFormat.PresentationOpenXml)]
    [InlineData("memo.doc", AttachmentDocumentFormat.LegacyWord)]
    [InlineData("budget.xls", AttachmentDocumentFormat.LegacySpreadsheet)]
    [InlineData("pitch.ppt", AttachmentDocumentFormat.LegacyPresentation)]
    [InlineData("terms.odt", AttachmentDocumentFormat.OpenDocumentText)]
    [InlineData("ledger.ods", AttachmentDocumentFormat.OpenDocumentSpreadsheet)]
    [InlineData("deck.odp", AttachmentDocumentFormat.OpenDocumentPresentation)]
    public void Recognize_AGenericMediaTypeOverANamedFile_FallsBackToTheExtension(
        string fileName,
        AttachmentDocumentFormat expected)
    {
        // Arrange, Act
        var recognized = AttachmentDocumentFormats.Recognize("application/octet-stream", FileNamed(fileName));

        // Assert
        Assert.Equal(expected, recognized);
    }

    /// <summary>The extension is a fallback and never an override, or a renamed file would decide which parser reads it.</summary>
    [Fact]
    public void Recognize_ADeclaredMediaTypeDisagreeingWithTheExtension_KeepsTheDeclaredFormat()
    {
        // Arrange, Act
        var recognized = AttachmentDocumentFormats.Recognize("application/pdf", FileNamed("ledger.xlsx"));

        // Assert
        Assert.Equal(AttachmentDocumentFormat.Pdf, recognized);
    }

    /// <summary>Nothing in the declaration naming a document is a refusal rather than an attempt.</summary>
    [Theory]
    [InlineData("application/octet-stream", "photo.jpeg")]
    [InlineData("image/png", "diagram.png")]
    [InlineData("application/zip", "archive.zip")]
    [InlineData("text/html", "page.html")]
    [InlineData("application/x-msdownload", "installer.exe")]
    public void Recognize_ADeclarationNamingNoDocumentFormat_RecognizesNothing(string mediaType, string fileName)
    {
        // Arrange, Act
        var recognized = AttachmentDocumentFormats.Recognize(mediaType, FileNamed(fileName));

        // Assert
        Assert.Null(recognized);
    }

    /// <summary>An unnamed part carrying a generic type leaves nothing to read, which is not a reason to guess.</summary>
    [Fact]
    public void Recognize_AnUnnamedPartUnderAGenericMediaType_RecognizesNothing()
    {
        // Arrange, Act
        var recognized = AttachmentDocumentFormats.Recognize("application/octet-stream", fileName: null);

        // Assert
        Assert.Null(recognized);
    }

    /// <summary>PDF and both office families are read; the three legacy binary ones are named and not read.</summary>
    [Theory]
    [InlineData(AttachmentDocumentFormat.Pdf, true)]
    [InlineData(AttachmentDocumentFormat.WordOpenXml, true)]
    [InlineData(AttachmentDocumentFormat.SpreadsheetOpenXml, true)]
    [InlineData(AttachmentDocumentFormat.PresentationOpenXml, true)]
    [InlineData(AttachmentDocumentFormat.LegacyWord, false)]
    [InlineData(AttachmentDocumentFormat.LegacySpreadsheet, false)]
    [InlineData(AttachmentDocumentFormat.LegacyPresentation, false)]
    [InlineData(AttachmentDocumentFormat.OpenDocumentText, true)]
    [InlineData(AttachmentDocumentFormat.OpenDocumentSpreadsheet, true)]
    [InlineData(AttachmentDocumentFormat.OpenDocumentPresentation, true)]
    public void IsExtracted_EveryRecognizedFormat_SeparatesWhatIsReadFromWhatIsOnlyNamed(
        AttachmentDocumentFormat format,
        bool expected)
    {
        // Arrange, Act
        var extracted = AttachmentDocumentFormats.IsExtracted(format);

        // Assert
        Assert.Equal(expected, extracted);
    }

    private static AttachmentFileName? FileNamed(string value) =>
        AttachmentFileName.TryNormalize(value, out var fileName) ? fileName : null;
}
