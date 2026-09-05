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
        var bounds = Bounds();
        bounds.Formats.Clear();
        bounds.Formats.Add(AttachmentDocumentFormat.Pdf);

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
        var settings = OpenXmlAttachmentTextReader.PartReaderSettings();
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
            new BoundedAttachmentTextExtractor(Bounds(), TimeProvider.System).ExtractTextAsync(
                attachment,
                caller.Token));
    }

    private static AttachmentTextExtractionOptions Bounds() => new();

    private static Task<AttachmentTextExtractionResult> ExtractAsync(
        FakeOpenedEmailAttachment attachment,
        AttachmentTextExtractionOptions? bounds = null) =>
        new BoundedAttachmentTextExtractor(bounds ?? Bounds(), TimeProvider.System).ExtractTextAsync(
            attachment,
            TestContext.Current.CancellationToken);
}
