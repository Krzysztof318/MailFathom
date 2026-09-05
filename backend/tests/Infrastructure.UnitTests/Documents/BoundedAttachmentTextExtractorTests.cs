// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Xml;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Infrastructure.Documents;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Documents;

/// <summary>Covers what one attachment yields, and what every ceiling around it reports when a sender crosses it.</summary>
/// <remarks>
/// The claim these tests exist for is not that a document can be read — it is that nothing a hostile one does reaches a
/// caller as an exception, a hang, or an empty string standing in for "nothing found". Every bound therefore has a case
/// asserting the distinct reason it produces, and the reasons are asserted rather than the absence of text.
/// </remarks>
public sealed class BoundedAttachmentTextExtractorTests
{
    /// <summary>The ordinary case: a PDF's words come back, page by page.</summary>
    [Fact]
    public async Task ExtractTextAsync_APdfCarryingText_ReadsEveryPagesWords()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/pdf",
            "contract.pdf",
            DocumentFixtures.Pdf("The roof is replaced by March", "Payment falls due on completion"));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Extracted, result.Outcome);
        Assert.Contains("roof is replaced", result.Text?.Text, StringComparison.Ordinal);
        Assert.Contains("Payment falls due", result.Text?.Text, StringComparison.Ordinal);
        Assert.Equal(2, result.Text?.PageCount);
        Assert.Empty(result.Text?.PagesWithoutText ?? [0]);
    }

    /// <summary>A page with no text layer is the exact target a later optical-character-recognition pass would be given.</summary>
    [Fact]
    public async Task ExtractTextAsync_APdfWhoseSecondPageCarriesNoText_NamesThatPageAndStillExtracts()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/pdf",
            "scan.pdf",
            DocumentFixtures.Pdf("A covering note", string.Empty, "A closing note"));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal([2], result.Text?.PagesWithoutText);
        Assert.Equal(3, result.Text?.PageCount);
    }

    /// <summary>A document whose every page is a picture is an extraction reporting a scan, never a failure.</summary>
    [Fact]
    public async Task ExtractTextAsync_APdfWithNoTextLayerAtAll_ReportsAScanRatherThanAFailure()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/pdf",
            "scan.pdf",
            DocumentFixtures.Pdf(string.Empty, string.Empty));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal(string.Empty, result.Text?.Text);
        Assert.Equal([1, 2], result.Text?.PagesWithoutText);
    }

    /// <summary>A word-processing document's paragraphs come back in order, one line each.</summary>
    [Fact]
    public async Task ExtractTextAsync_AWordDocument_ReadsItsParagraphsInOrder()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "terms.docx",
            DocumentFixtures.WordDocument("Clause one", "Clause two"));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal("Clause one\nClause two", result.Text?.Text);
        Assert.Equal(1, result.Text?.PageCount);
    }

    /// <summary>A presentation is read one page per slide, and an empty slide is named like an empty page.</summary>
    [Fact]
    public async Task ExtractTextAsync_APresentation_ReadsOnePagePerSlideAndNamesTheEmptyOne()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "deck.pptx",
            DocumentFixtures.Presentation("Opening", string.Empty, "Closing"));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal("Opening\nClosing", result.Text?.Text);
        Assert.Equal(3, result.Text?.PageCount);
        Assert.Equal([2], result.Text?.PagesWithoutText);
    }

    /// <summary>
    /// A workbook holds most of its words in a table every sheet indexes into, so resolving that table is what makes a
    /// sheet's text a fact about that sheet rather than about the file.
    /// </summary>
    [Fact]
    public async Task ExtractTextAsync_AWorkbook_ResolvesTheSharedStringsEachSheetIndexesInto()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "ledger.xlsx",
            DocumentFixtures.Workbook(["Invoice", "Roof repair"], []));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal("Invoice\nRoof repair", result.Text?.Text);
        Assert.Equal(2, result.Text?.PageCount);
        Assert.Equal([2], result.Text?.PagesWithoutText);
    }

    /// <summary>An OpenDocument text document reads back its paragraphs, and counts as one page like its Office equivalent.</summary>
    [Fact]
    public async Task ExtractTextAsync_AnOpenDocumentTextDocument_ReadsItsParagraphsAsOnePage()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.oasis.opendocument.text",
            "terms.odt",
            DocumentFixtures.OpenDocumentText("Clause one", "Clause two"));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal("Clause one\nClause two", result.Text?.Text);
        Assert.Equal(1, result.Text?.PageCount);
        Assert.Empty(result.Text?.PagesWithoutText ?? []);
    }

    /// <summary>An OpenDocument spreadsheet is read one page per sheet, each cell on a line of its own.</summary>
    [Fact]
    public async Task ExtractTextAsync_AnOpenDocumentSpreadsheet_ReadsOnePagePerSheetAndNamesTheEmptyOne()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.oasis.opendocument.spreadsheet",
            "ledger.ods",
            DocumentFixtures.OpenDocumentSpreadsheet(["Invoice", "Roof repair"], []));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal("Invoice\nRoof repair", result.Text?.Text);
        Assert.Equal(2, result.Text?.PageCount);
        Assert.Equal([2], result.Text?.PagesWithoutText);
    }

    /// <summary>An OpenDocument presentation is read one page per drawing page, and an empty one is named like an empty slide.</summary>
    [Fact]
    public async Task ExtractTextAsync_AnOpenDocumentPresentation_ReadsOnePagePerPageAndNamesTheEmptyOne()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.oasis.opendocument.presentation",
            "deck.odp",
            DocumentFixtures.OpenDocumentPresentation("Opening", string.Empty, "Closing"));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal("Opening\nClosing", result.Text?.Text);
        Assert.Equal(3, result.Text?.PageCount);
        Assert.Equal([2], result.Text?.PagesWithoutText);
    }

    /// <summary>
    /// The format writes a run of spaces, a tab, and a line break as elements rather than as characters, so a reader
    /// gathering only text nodes would join the words on either side of one into a word nobody wrote.
    /// </summary>
    [Fact]
    public async Task ExtractTextAsync_AnOpenDocumentParagraphSpacedByElements_KeepsTheWordsApart()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.oasis.opendocument.text",
            "spaced.odt",
            DocumentFixtures.OpenDocumentContentPart("""
                <?xml version="1.0" encoding="UTF-8"?>
                <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
                  <office:body><office:text><text:p>Roof<text:s />repair<text:line-break />invoice<text:tab />paid</text:p></office:text></office:body>
                </office:document-content>
                """));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal("Roof repair\ninvoice paid", result.Text?.Text);
    }

    /// <summary>An OpenDocument package is a zip archive, so the entity refusal has to hold in its content part too.</summary>
    [Fact]
    public async Task ExtractTextAsync_AnOpenDocumentPartDeclaringAnExternalEntity_ReportsMalformed()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.oasis.opendocument.text",
            "hostile.odt",
            DocumentFixtures.OpenDocumentContentPart("""
                <?xml version="1.0" encoding="UTF-8"?>
                <!DOCTYPE office:document-content [<!ENTITY stolen SYSTEM "file:///etc/passwd">]>
                <office:document-content xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0" xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0">
                  <office:body><office:text><text:p>&stolen;</text:p></office:text></office:body>
                </office:document-content>
                """));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Malformed, result.Outcome);
    }

    /// <summary>An archive carrying no content part is a package this reader cannot read rather than an empty document.</summary>
    [Fact]
    public async Task ExtractTextAsync_AnOpenDocumentPackageWithNoContentPart_ReportsMalformed()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.oasis.opendocument.text",
            "empty.odt",
            DocumentFixtures.Package(("mimetype", "application/vnd.oasis.opendocument.text")));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Malformed, result.Outcome);
    }

    /// <summary>An attachment that is not a document is refused before any parser is offered its bytes.</summary>
    [Fact]
    public async Task ExtractTextAsync_AnAttachmentNamingNoDocumentFormat_ReportsThatNothingRecognizedIt()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment("image/png", "diagram.png", [1, 2, 3]);

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.FormatNotRecognized, result.Outcome);
        Assert.Null(result.Text);
    }

    /// <summary>The three legacy binary formats are recognized and skipped, which is a different fact from being unrecognized.</summary>
    [Theory]
    [InlineData("application/msword", "memo.doc")]
    [InlineData("application/vnd.ms-excel", "budget.xls")]
    [InlineData("application/vnd.ms-powerpoint", "pitch.ppt")]
    public async Task ExtractTextAsync_ALegacyBinaryDocument_ReportsARecognizedFormatNothingReads(
        string mediaType,
        string fileName)
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(mediaType, fileName, [1, 2, 3]);

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.FormatNotExtracted, result.Outcome);
    }

    /// <summary>A deployment that narrowed the formats it accepts is not offered the ones it excluded.</summary>
    [Fact]
    public async Task ExtractTextAsync_AFormatTheDeploymentExcluded_ReportsAFormatItDoesNotRead()
    {
        // Arrange
        var bounds = new AttachmentTextExtractionOptions { Formats = [AttachmentDocumentFormat.Pdf] };

        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "terms.docx",
            DocumentFixtures.WordDocument("Clause one"));

        // Act
        var result = await ExtractAsync(attachment, bounds);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.FormatNotExtracted, result.Outcome);
    }

    /// <summary>The size the MIME walk measured refuses an oversized attachment before a single octet is buffered.</summary>
    [Fact]
    public async Task ExtractTextAsync_AnAttachmentPastTheInputCeiling_RefusesItBeforeReadingIt()
    {
        // Arrange
        var bounds = Bounds();
        bounds.MaxInputOctets = 64;

        await using var attachment = new FakeOpenedEmailAttachment(
            "application/pdf",
            "contract.pdf",
            DocumentFixtures.Pdf("Anything at all"));

        // Act
        var result = await ExtractAsync(attachment, bounds);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.InputTooLarge, result.Outcome);
    }

    /// <summary>A measured size is a second reading of the same bytes rather than a guarantee, so the copy is bounded too.</summary>
    [Fact]
    public async Task ExtractTextAsync_AnAttachmentLongerThanItsDescriptionSays_RefusesItWhileItIsBeingCopied()
    {
        // Arrange
        var bounds = Bounds();
        bounds.MaxInputOctets = 512;

        await using var attachment = new FakeOpenedEmailAttachment(
            "application/pdf",
            "contract.pdf",
            DocumentFixtures.Pdf("Anything at all"),
            declaredSizeOctets: 16);

        // Act
        var result = await ExtractAsync(attachment, bounds);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.InputTooLarge, result.Outcome);
    }

    /// <summary>A document yielding more text than one attachment may contribute is abandoned rather than truncated.</summary>
    [Fact]
    public async Task ExtractTextAsync_ADocumentPastTheOutputCeiling_ReportsTheTextAsTooLarge()
    {
        // Arrange
        var bounds = Bounds();
        bounds.MaxExtractedTextCharacters = 32;

        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "terms.docx",
            DocumentFixtures.WordDocument(new string('a', 5_000)));

        // Act
        var result = await ExtractAsync(attachment, bounds);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.ExtractedTextTooLarge, result.Outcome);
        Assert.Null(result.Text);
    }

    /// <summary>A part inflating far past what its compressed length explains is refused while it inflates.</summary>
    [Fact]
    public async Task ExtractTextAsync_AContainerPartWithAnImplausibleInflationRatio_ReportsTheContainerBound()
    {
        // Arrange
        var bounds = Bounds();
        bounds.MaxDecompressionRatio = 5;
        bounds.MaxDecompressedOctets = long.MaxValue;

        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "bomb.docx",
            DocumentFixtures.InflatingWordDocument(200_000));

        // Act
        var result = await ExtractAsync(attachment, bounds);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.ContainerBoundExceeded, result.Outcome);
    }

    /// <summary>The shared total is what catches an archive whose parts are each individually plausible.</summary>
    [Fact]
    public async Task ExtractTextAsync_AContainerPastItsTotalDecompressedSize_ReportsTheContainerBound()
    {
        // Arrange
        var bounds = Bounds();
        bounds.MaxDecompressionRatio = int.MaxValue;
        bounds.MaxDecompressedOctets = 2_048;

        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "bomb.docx",
            DocumentFixtures.InflatingWordDocument(200_000));

        // Act
        var result = await ExtractAsync(attachment, bounds);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.ContainerBoundExceeded, result.Outcome);
    }

    /// <summary>An archive of very many parts costs per part, which neither size bound measures.</summary>
    [Fact]
    public async Task ExtractTextAsync_AContainerDeclaringMorePartsThanAllowed_ReportsTheContainerBound()
    {
        // Arrange
        var bounds = Bounds();
        bounds.MaxContainerParts = 2;

        var parts = Enumerable.Range(0, 8)
            .Select(index => ($"word/part{index}.xml", "<a />"))
            .ToArray();

        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "many.docx",
            DocumentFixtures.Package(parts));

        // Act
        var result = await ExtractAsync(attachment, bounds);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.ContainerBoundExceeded, result.Outcome);
    }

    /// <summary>Deep nesting turns a small part into a walk that consumes stack, which the depth ceiling refuses.</summary>
    [Fact]
    public async Task ExtractTextAsync_AContainerPartNestedPastTheDepthCeiling_ReportsTheContainerBound()
    {
        // Arrange
        var bounds = Bounds();
        bounds.MaxElementDepth = 8;

        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "nested.docx",
            DocumentFixtures.DeeplyNestedWordDocument(depth: 40));

        // Act
        var result = await ExtractAsync(attachment, bounds);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.ContainerBoundExceeded, result.Outcome);
    }

    /// <summary>
    /// A part declaring an external entity is the classic way a document parser is turned into a file reader. Nothing
    /// here resolves one: the declaration itself is refused, so the extraction ends as unreadable markup.
    /// </summary>
    [Fact]
    public async Task ExtractTextAsync_AContainerPartDeclaringAnExternalEntity_RefusesTheDeclarationAndReadsNothing()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "entity.docx",
            DocumentFixtures.WordDocumentDeclaringAnExternalEntity("/etc/hostname"));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Malformed, result.Outcome);
        Assert.Null(result.Text);
    }

    /// <summary>
    /// The refusal above is a property of the reader's configuration rather than of one document. The resolver cannot
    /// be read back — <see cref="XmlReaderSettings.XmlResolver" /> is write-only — so what the configuration does with
    /// a declaration is asserted instead of what it holds.
    /// </summary>
    [Fact]
    public void PartReaderSettings_EveryXmlPartThisAdapterReads_RefusesADocumentTypeDeclaration()
    {
        // Arrange
        var settings = BoundedArchivePartReader.PartReaderSettings();
        using var declared = new StringReader(
            """<!DOCTYPE root [<!ENTITY stolen SYSTEM "file:///etc/hostname">]><root>&stolen;</root>""");

        // Act
        using var reader = XmlReader.Create(declared, settings);

        // Assert
        Assert.Equal(DtdProcessing.Prohibit, settings.DtdProcessing);
        Assert.Throws<XmlException>(() =>
        {
            while (reader.Read())
            {
            }
        });
    }

    /// <summary>
    /// A password-protected contract is not a failure to read and not an empty document. It is one of the reasons a
    /// mailbox owner is owed, because it is the one they can act on.
    /// </summary>
    [Fact]
    public async Task ExtractTextAsync_APdfThisSystemHoldsNoPasswordFor_ReportsItAsEncrypted()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/pdf",
            "sealed.pdf",
            DocumentFixtures.EncryptedPdf());

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Encrypted, result.Outcome);
        Assert.Null(result.Text);
    }

    /// <summary>Bytes that are not the format they declare are expected of real mail rather than exceptional.</summary>
    [Theory]
    [InlineData("application/pdf", "broken.pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "broken.docx")]
    public async Task ExtractTextAsync_BytesThatAreNotTheFormatTheyDeclare_ReportThemAsMalformed(
        string mediaType,
        string fileName)
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            mediaType,
            fileName,
            Encoding.UTF8.GetBytes("this is not a document at all"));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Malformed, result.Outcome);
    }

    /// <summary>A package with no document part is not a word-processing document, whatever it declares itself to be.</summary>
    [Fact]
    public async Task ExtractTextAsync_AnArchiveWithNoDocumentPart_ReportsItAsMalformed()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "empty.docx",
            DocumentFixtures.Package(("unrelated.xml", "<a />")));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Malformed, result.Outcome);
    }

    /// <summary>
    /// An empty entry in the shared string table raises no end element, so a table that skipped it would hand every
    /// later cell the next entry's words — the one failure of this kind that reads as extracted text rather than as an error.
    /// </summary>
    [Fact]
    public async Task ExtractTextAsync_AWorkbookWhoseStringTableCarriesAnEmptyEntry_KeepsEveryLaterCellOnItsOwnWords()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "ledger.xlsx",
            DocumentFixtures.Package(
                ("xl/sharedStrings.xml", """
                    <?xml version="1.0" encoding="UTF-8"?>
                    <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><si /><si><t>Roof repair</t></si></sst>
                    """),
                ("xl/worksheets/sheet1.xml", """
                    <?xml version="1.0" encoding="UTF-8"?>
                    <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData><row><c t="s"><v>1</v></c></row></sheetData></worksheet>
                    """)));

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal("Roof repair", result.Text?.Text);
    }

    /// <summary>
    /// A password-protected Office package is an OLE compound file rather than an archive, and telling an owner their
    /// document is broken when what it is is locked sends them looking for a defect that is not there.
    /// </summary>
    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "locked.docx")]
    [InlineData("application/vnd.oasis.opendocument.text", "locked.odt")]
    public async Task ExtractTextAsync_APackageThatIsAnOleCompoundFile_ReportsEncryptedRatherThanMalformed(
        string mediaType,
        string fileName)
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            mediaType,
            fileName,
            DocumentFixtures.EncryptedOfficePackage());

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Encrypted, result.Outcome);
    }

    /// <summary>
    /// A password-protected OpenDocument file stays an ordinary archive and encrypts the parts inside it, so only its
    /// manifest says what it is — without reading that, a locked document reports as a broken one.
    /// </summary>
    [Fact]
    public async Task ExtractTextAsync_AnOpenDocumentPackageDeclaringEncryptedParts_ReportsEncrypted()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.oasis.opendocument.text",
            "locked.odt",
            DocumentFixtures.EncryptedOpenDocument());

        // Act
        var result = await ExtractAsync(attachment);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Encrypted, result.Outcome);
    }

    /// <summary>
    /// The compressed length a part declares is the sender's number like every other, and overstating it is how a ratio
    /// denominator would be widened until no inflation could reach it — so the guard has to hold against a declaration
    /// rather than against the data it claims to describe.
    /// </summary>
    [Fact]
    public async Task ExtractTextAsync_AnArchiveOverstatingWhatItsPartCompressedTo_StillAppliesTheRatioBound()
    {
        // Arrange
        // Both other container ceilings are set far out of reach, so the ratio is the only one that can answer.
        var bounds = Bounds();
        bounds.MaxDecompressedOctets = 64L * 1024 * 1024;
        bounds.MaxExtractedTextCharacters = 10_000_000;

        await using var attachment = new FakeOpenedEmailAttachment(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "bomb.docx",
            DocumentFixtures.WordDocumentOverstatingItsCompressedLength(400_000));

        // Act
        var result = await ExtractAsync(attachment, bounds);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.ContainerBoundExceeded, result.Outcome);
    }

    /// <summary>
    /// A storage fault is a fact about this attempt rather than about the document, and an outcome the caller may
    /// record once and never revisit would turn a dropped connection into a permanently unreadable attachment.
    /// </summary>
    [Fact]
    public async Task ExtractTextAsync_AStoredContentReadThatFails_LetsTheFailureReachTheCaller()
    {
        // Arrange
        await using var attachment = new FakeOpenedEmailAttachment(
            "application/pdf",
            "contract.pdf",
            DocumentFixtures.Pdf("A page nobody reaches"),
            beforeWriting: () => throw new IOException("The content store dropped the connection."));

        // Act, Assert
        await Assert.ThrowsAsync<IOException>(() => ExtractAsync(attachment));
    }

    /// <summary>The timeout is a fact about the attachment, so it comes back as a reason rather than as a cancellation.</summary>
    [Fact]
    public async Task ExtractTextAsync_AnExtractionPastItsTimeout_ReportsItAsTimedOutRatherThanCancelled()
    {
        // Arrange
        var bounds = Bounds();
        var clock = new FakeTimeProvider();

        await using var attachment = new FakeOpenedEmailAttachment(
            "application/pdf",
            "slow.pdf",
            DocumentFixtures.Pdf("A page nobody reaches"),
            beforeWriting: () => clock.Advance(bounds.Timeout + TimeSpan.FromSeconds(1)));

        // Act
        var result = await new BoundedAttachmentTextExtractor(bounds, clock).ExtractTextAsync(
            attachment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.TimedOut, result.Outcome);
    }

    /// <summary>
    /// The copy into the buffer is not where a long extraction is spent, so the deadline has to be observed by the
    /// parser as well: a document whose octets arrived quickly and whose walk then runs past the timeout is what every
    /// per-page and per-element check exists for, and a test that crosses the deadline before the copy proves none of
    /// them.
    /// </summary>
    [Theory]
    [InlineData("application/pdf", "slow.pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "slow.docx")]
    [InlineData("application/vnd.oasis.opendocument.text", "slow.odt")]
    public async Task ExtractTextAsync_ATimeoutCrossedOnceTheOctetsAreRead_ReportsItAsTimedOut(
        string mediaType,
        string fileName)
    {
        // Arrange
        var bounds = Bounds();
        var clock = new FakeTimeProvider();

        await using var attachment = new FakeOpenedEmailAttachment(
            mediaType,
            fileName,
            SingleParagraphOf(mediaType),
            afterWriting: () => clock.Advance(bounds.Timeout + TimeSpan.FromSeconds(1)));

        // Act
        var result = await new BoundedAttachmentTextExtractor(bounds, clock).ExtractTextAsync(
            attachment,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.TimedOut, result.Outcome);
    }

    /// <summary>A caller's own cancellation belongs to the caller and is never reported as the attachment's fault.</summary>
    [Fact]
    public async Task ExtractTextAsync_ACallerCancellingTheRead_LetsTheCancellationReachTheCaller()
    {
        // Arrange
        using var caller = new CancellationTokenSource();

        await using var attachment = new FakeOpenedEmailAttachment(
            "application/pdf",
            "contract.pdf",
            DocumentFixtures.Pdf("A page nobody reaches"),
            beforeWriting: caller.Cancel);

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new BoundedAttachmentTextExtractor(Bounds(), new FakeTimeProvider()).ExtractTextAsync(
                attachment,
                caller.Token));
    }

    /// <summary>Builds the same one-paragraph document in whichever format the media type names.</summary>
    private static byte[] SingleParagraphOf(string mediaType) => mediaType switch
    {
        "application/pdf" => DocumentFixtures.Pdf("Roof repair invoice"),
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" =>
            DocumentFixtures.WordDocument("Roof repair invoice"),
        _ => DocumentFixtures.OpenDocumentText("Roof repair invoice"),
    };

    private static AttachmentTextExtractionOptions Bounds() => new();

    /// <summary>Reads one attachment under a clock that only moves where a test moves it.</summary>
    /// <remarks>
    /// The subject turns whatever clock it is given into a live deadline, so a real one would let a loaded runner
    /// decide the outcome: a stall inside any test routed through here would answer <c>TimedOut</c> instead of what the
    /// test asserts.
    /// </remarks>
    private static Task<AttachmentTextExtractionResult> ExtractAsync(
        FakeOpenedEmailAttachment attachment,
        AttachmentTextExtractionOptions? bounds = null) =>
        new BoundedAttachmentTextExtractor(bounds ?? Bounds(), new FakeTimeProvider()).ExtractTextAsync(
            attachment,
            TestContext.Current.CancellationToken);
}
