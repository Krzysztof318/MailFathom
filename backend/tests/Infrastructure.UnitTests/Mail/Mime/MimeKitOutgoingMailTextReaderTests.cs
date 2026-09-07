// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Infrastructure.Mail.Mime;
using Microsoft.Extensions.Time.Testing;
using MimeKit;
using MimeKit.Text;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>Covers reading a composed outgoing message back into the words a screen is asked about.</summary>
/// <remarks>
/// The document parsers are not exercised here. What this reader owns is which parts are offered to the extractor at
/// all, what the ceilings do across a message, and how an outcome it cannot use becomes one refusal — so the extractor
/// is scripted and the parsing is covered where it lives.
/// </remarks>
public sealed class MimeKitOutgoingMailTextReaderTests
{
    private static readonly DateTimeOffset Composed = new(2026, 9, 6, 12, 0, 0, TimeSpan.Zero);

    private readonly ScriptedAttachmentTextExtractor extractor = new();
    private readonly FakeTimeProvider timeProvider = new(Composed);
    private readonly MimeKitOutgoingMailTextReader reader;

    /// <summary>Builds the reader every test reads through, over the scripted extractor and a clock a test advances.</summary>
    public MimeKitOutgoingMailTextReaderTests() =>
        this.reader = new MimeKitOutgoingMailTextReader(
            this.extractor,
            new AttachmentTextExtractionOptions(),
            EmailAttachmentTextBounds.Disabled,
            this.timeProvider);

    [Fact]
    public async Task ReadForScreeningAsync_AMessageWithBothRepresentations_ReadsTheSubjectAndBoth()
    {
        // Arrange
        var raw = Compose("Quarterly figures", "the plain text", "<p>the markup</p>");

        // Act
        var text = await this.reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Quarterly figures", text.Subject);
        Assert.Equal("the plain text", text.PlainTextBody);
        Assert.Equal("<p>the markup</p>", text.HtmlBody);
    }

    [Fact]
    public async Task ReadForScreeningAsync_AMessageWithNoMarkup_ReportsTheAbsenceRatherThanEmptyText()
    {
        // Arrange
        var raw = Compose("Quarterly figures", "the plain text", htmlBody: null);

        // Act
        var text = await this.reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(text.HtmlBody);
        Assert.Equal(2, text.ScreenedValues.Count);
        Assert.Contains("the plain text", text.PlainTextBody, StringComparison.Ordinal);
    }

    /// <summary>A message nobody titled reads as empty text, so the value list drops it rather than scanning nothing.</summary>
    [Fact]
    public async Task ReadForScreeningAsync_AMessageWithNoSubject_ReadsItAsEmptyText()
    {
        // Arrange
        var raw = Compose(subject: null, "the plain text", htmlBody: null);

        // Act
        var text = await this.reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(string.Empty, text.Subject);
        Assert.Equal(text.PlainTextBody, Assert.Single(text.ScreenedValues));
    }

    /// <summary>
    /// The markup is returned as it will be transmitted rather than as a sanitizer would allow a browser to render it,
    /// because an attribute a sanitizer strips still leaves in the message.
    /// </summary>
    [Fact]
    public async Task ReadForScreeningAsync_MarkupASanitizerWouldStrip_ReadsItBackWhole()
    {
        // Arrange
        var raw = Compose(
            "Quarterly figures",
            "the plain text",
            "<p title=\"AKIAEXAMPLEKEY\">the markup</p><!-- AKIAEXAMPLEKEY -->");

        // Act
        var text = await this.reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(text.HtmlBody);
        Assert.Contains("title=\"AKIAEXAMPLEKEY\"", text.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("<!-- AKIAEXAMPLEKEY -->", text.HtmlBody, StringComparison.Ordinal);
    }

    /// <summary>
    /// The point of the whole change: what an author attached is screened with what they typed, so a credential inside
    /// the attached document is one of the values a screen is handed rather than something that leaves unexamined.
    /// </summary>
    [Fact]
    public async Task ReadForScreeningAsync_AMessageCarryingAReadableDocument_ScreensItsTextBesideTheBody()
    {
        // Arrange
        this.extractor.Reads("terms.pdf", "the signing key is AKIAEXAMPLEKEY");

        var raw = MessageAttaching(Attachment("terms.pdf", "application/pdf", "%PDF-1.7"));

        // Act
        var text = await this.reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the signing key is AKIAEXAMPLEKEY", Assert.Single(text.AttachmentTexts));
        Assert.Null(text.AttachmentRefusal);
        Assert.Contains("the signing key is AKIAEXAMPLEKEY", text.ScreenedValues, StringComparer.Ordinal);
    }

    /// <summary>
    /// A file nothing recognized as a document is stepped over rather than refused. An image is the ordinary attachment,
    /// and no text scanner ever undertook to read a photograph of anything.
    /// </summary>
    [Fact]
    public async Task ReadForScreeningAsync_AnAttachmentNoReaderRecognizes_ContributesNothingAndRefusesNothing()
    {
        // Arrange
        var raw = MessageAttaching(Attachment("photo.png", "image/png", "not really a picture"));

        // Act
        var text = await this.reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(text.AttachmentTexts);
        Assert.Null(text.AttachmentRefusal);
        Assert.Equal(2, text.ScreenedValues.Count);
    }

    /// <summary>
    /// The words-only read opens no attachment at all, which is what keeps a document parser off every path that only
    /// wants what a message says: a caller handing an author their own draft back reads the bodies and discards the
    /// rest, so running the parsers there would charge a request for work nothing ever looks at.
    /// </summary>
    [Fact]
    public async Task ReadWordsAsync_AMessageCarryingReadableDocuments_ReadsNoneOfThemAndRefusesNothing()
    {
        // Arrange
        this.extractor.Reads("invoice.pdf", "the signing key is AKIAEXAMPLEKEY");

        var raw = MessageAttaching(Attachment("invoice.pdf", "application/pdf", "%PDF-1.7 one"));

        // Act
        var text = await this.reader.ReadWordsAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(this.extractor.ReadFileNames);
        Assert.Empty(text.AttachmentTexts);
        Assert.Null(text.AttachmentRefusal);
    }

    /// <summary>
    /// A file nothing could read refuses only the read that would have judged it. The words-only read is not asked to
    /// judge anything, so the same message comes back as its words rather than as a refusal nobody would act on.
    /// </summary>
    [Fact]
    public async Task ReadWordsAsync_AMessageCarryingADocumentNothingCouldRead_RefusesNothing()
    {
        // Arrange
        this.extractor.Reports("locked.pdf", AttachmentTextExtractionResult.Encrypted());

        var raw = MessageAttaching(Attachment("locked.pdf", "application/pdf", "%PDF-1.7 one"));

        // Act
        var text = await this.reader.ReadWordsAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(text.AttachmentRefusal);
        Assert.Empty(this.extractor.ReadFileNames);
    }

    /// <summary>
    /// The attachment the reader opens is offered to the extractor as the octets that part actually carries, decoded
    /// out of the message rather than named by it. Nothing else in the repository reads a byte through that opened
    /// attachment, so a reader handing over the wrong part, or nothing at all, would leave a screened deployment
    /// scanning an empty document while every assertion about names and outcomes stayed green.
    /// </summary>
    [Fact]
    public async Task ReadForScreeningAsync_ADocumentTheExtractorWritesOut_OffersTheOctetsThatPartCarries()
    {
        // Arrange
        const string Contents = "%PDF-1.7 the signing key is AKIAEXAMPLEKEY";

        this.extractor.Reads("invoice.pdf", "the signing key is AKIAEXAMPLEKEY");

        var raw = MessageAttaching(
            Attachment("note.txt", "text/plain", "nothing here"),
            Attachment("invoice.pdf", "application/pdf", Contents));

        // Act
        await this.reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Encoding.UTF8.GetBytes(Contents), this.extractor.WrittenOctets["invoice.pdf"]);
        Assert.Equal(Encoding.UTF8.GetBytes("nothing here"), this.extractor.WrittenOctets["note.txt"]);
    }

    /// <summary>
    /// A document nothing could read is reported rather than passed as clean, whichever way it defeated the reader.
    /// Every reason but the one above means the file is going out and nobody knows what is in it.
    /// </summary>
    [Theory]
    [InlineData(nameof(AttachmentTextExtractionOutcome.Encrypted))]
    [InlineData(nameof(AttachmentTextExtractionOutcome.Malformed))]
    [InlineData(nameof(AttachmentTextExtractionOutcome.FormatNotExtracted))]
    [InlineData(nameof(AttachmentTextExtractionOutcome.TimedOut))]
    [InlineData(nameof(AttachmentTextExtractionOutcome.InputTooLarge))]
    [InlineData(nameof(AttachmentTextExtractionOutcome.ContainerBoundExceeded))]
    public async Task ReadForScreeningAsync_ADocumentNothingCouldRead_RefusesForTheFileAndReadsNoText(string outcome)
    {
        // Arrange
        this.extractor.Reports("locked.docx", UnreadableResult(outcome));

        var raw = MessageAttaching(Attachment("locked.docx", "application/octet-stream", "PK"));

        // Act
        var text = await this.reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingAttachmentRefusal.NotRead, text.AttachmentRefusal);
        Assert.Empty(text.AttachmentTexts);
    }

    /// <summary>
    /// One unreadable document refuses the message, so nothing after it is opened and nothing read before it is carried
    /// on: screening part of a message the screen has already decided it cannot judge would be worse than not screening.
    /// </summary>
    [Fact]
    public async Task ReadForScreeningAsync_ADocumentNothingCouldRead_StopsTheWalkAndDropsWhatCameBefore()
    {
        // Arrange
        this.extractor.Reads("first.pdf", "an ordinary invoice");
        this.extractor.Reports("locked.pdf", AttachmentTextExtractionResult.Encrypted());
        this.extractor.Reads("third.pdf", "another ordinary invoice");

        var raw = MessageAttaching(
            Attachment("first.pdf", "application/pdf", "%PDF-1.7 one"),
            Attachment("locked.pdf", "application/pdf", "%PDF-1.7 two"),
            Attachment("third.pdf", "application/pdf", "%PDF-1.7 three"));

        // Act
        var text = await this.reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingAttachmentRefusal.NotRead, text.AttachmentRefusal);
        Assert.Empty(text.AttachmentTexts);
        Assert.Equal(["first.pdf", "locked.pdf"], this.extractor.ReadFileNames);
    }

    /// <summary>
    /// The octets a whole message may be read from are the account run's own ceiling, applied here so one message costs
    /// the same on either path. It is reported apart from a file nobody could read, because every document here was
    /// read successfully and telling the author to convert one would name a file that was never the problem.
    /// </summary>
    [Fact]
    public async Task ReadForScreeningAsync_DocumentsHoldingMoreOctetsThanTheMessageCeiling_RefusesForTheMessage()
    {
        // Arrange
        var reader = new MimeKitOutgoingMailTextReader(
            this.extractor,
            new AttachmentTextExtractionOptions(),
            MessageBoundedTo(maxInputOctetsPerEmail: 16),
            this.timeProvider);

        this.extractor.Reads("first.pdf", "an ordinary invoice");
        this.extractor.Reads("second.pdf", "another ordinary invoice");

        var raw = MessageAttaching(
            Attachment("first.pdf", "application/pdf", "0123456789"),
            Attachment("second.pdf", "application/pdf", "0123456789"));

        // Act
        var text = await reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingAttachmentRefusal.MessageCeilingReached, text.AttachmentRefusal);
    }

    /// <summary>
    /// Two ordinary documents that each fit are read rather than refused, which is the case the message-wide ceiling
    /// must not swallow: a message of two large reports is an ordinary message, and answering it as an unreadable file
    /// would tell its author to convert something nothing was wrong with.
    /// </summary>
    [Fact]
    public async Task ReadForScreeningAsync_TwoDocumentsThatFitTheMessageCeilingTogether_ReadsBothAndRefusesNothing()
    {
        // Arrange
        this.extractor.Reads("first.pdf", "an ordinary invoice");
        this.extractor.Reads("second.pdf", "another ordinary invoice");

        var raw = MessageAttaching(
            Attachment("first.pdf", "application/pdf", "0123456789"),
            Attachment("second.pdf", "application/pdf", "0123456789"));

        // Act
        var text = await this.reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(text.AttachmentRefusal);
        Assert.Equal(["an ordinary invoice", "another ordinary invoice"], text.AttachmentTexts);
    }

    /// <summary>
    /// How many of a message's <i>documents</i> are read is the same ceiling, counted over what the extractor
    /// recognized: passing it refuses the act, because a screen has no option of reading what fits and leaving the
    /// rest, which is what the account run does with the same number.
    /// </summary>
    [Fact]
    public async Task ReadForScreeningAsync_MoreDocumentsThanTheMessageCeiling_RefusesForTheCount()
    {
        // Arrange
        var reader = new MimeKitOutgoingMailTextReader(
            this.extractor,
            new AttachmentTextExtractionOptions(),
            MessageBoundedTo(maxAttachmentsPerEmail: 1),
            this.timeProvider);

        this.extractor.Reads("first.pdf", "an ordinary invoice");
        this.extractor.Reads("second.pdf", "another ordinary invoice");

        var raw = MessageAttaching(
            Attachment("first.pdf", "application/pdf", "%PDF-1.7 one"),
            Attachment("second.pdf", "application/pdf", "%PDF-1.7 two"));

        // Act
        var text = await reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingAttachmentRefusal.MessageCeilingReached, text.AttachmentRefusal);
    }

    /// <summary>
    /// The count is spent by the documents the extractor recognized and by nothing else, which is the same rule the
    /// octets follow. Counting the parts a message declares instead would refuse a message carrying six photographs on
    /// a deployment whose ceiling is five — and tell its author to send fewer attached documents than it holds none of,
    /// which is exactly what an operator lowering this number to cut indexing cost would produce.
    /// </summary>
    [Fact]
    public async Task ReadForScreeningAsync_MorePartsThanTheCeilingButFewerDocuments_ScreensTheMessage()
    {
        // Arrange
        var reader = new MimeKitOutgoingMailTextReader(
            this.extractor,
            new AttachmentTextExtractionOptions(),
            MessageBoundedTo(maxAttachmentsPerEmail: 1),
            this.timeProvider);

        this.extractor.Reads("invoice.pdf", "an ordinary invoice");

        var raw = MessageAttaching(
            Attachment("holiday.jpg", "image/jpeg", "not a document"),
            Attachment("beach.jpg", "image/jpeg", "not a document either"),
            Attachment("invoice.pdf", "application/pdf", "%PDF-1.7 one"));

        // Act
        var text = await reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(text.AttachmentRefusal);
        Assert.Equal(["an ordinary invoice"], text.AttachmentTexts);
    }

    /// <summary>
    /// The per-attachment timeout bounds one read; without a bound across the message a sender would multiply it by
    /// however many small documents they chose to attach.
    /// </summary>
    [Fact]
    public async Task ReadForScreeningAsync_DocumentsTakingLongerThanTheCeilingTogether_RefusesForTheTime()
    {
        // Arrange
        var bounds = new AttachmentTextExtractionOptions();
        this.extractor.Reads("first.pdf", "an ordinary invoice");
        this.extractor.Reads("second.pdf", "another ordinary invoice");
        this.extractor.WhileReading = () => this.timeProvider.Advance(bounds.Timeout + TimeSpan.FromSeconds(1));

        var raw = MessageAttaching(
            Attachment("first.pdf", "application/pdf", "%PDF-1.7 one"),
            Attachment("second.pdf", "application/pdf", "%PDF-1.7 two"));

        // Act
        var text = await this.reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingAttachmentRefusal.MessageCeilingReached, text.AttachmentRefusal);
        Assert.Equal(["first.pdf"], this.extractor.ReadFileNames);
    }

    /// <summary>
    /// The extractor decides what to do from the part's own declaration and its decoded length, so the description it is
    /// handed has to be the one measured from this message rather than anything a caller stated.
    /// </summary>
    [Fact]
    public async Task ReadForScreeningAsync_AnAttachedDocument_IsOfferedTheDescriptionMeasuredFromTheMessage()
    {
        // Arrange
        this.extractor.Reads("terms.pdf", "an ordinary invoice");

        var raw = MessageAttaching(Attachment("terms.pdf", "application/pdf", "%PDF-1.7"));

        // Act
        await this.reader.ReadForScreeningAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        var offered = Assert.Single(this.extractor.Offered);
        Assert.Equal("terms.pdf", offered.FileName?.Value);
        Assert.Equal("application/pdf", offered.MediaType);
        Assert.Equal("%PDF-1.7"u8.Length, offered.DecodedSizeOctets);
    }

    [Fact]
    public async Task ReadForScreeningAsync_NoMimeAtAll_Refuses()
    {
        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => this.reader.ReadForScreeningAsync(ReadOnlyMemory<byte>.Empty, TestContext.Current.CancellationToken));
    }

    /// <summary>The message-wide ceilings, narrowed to whichever one a test is about.</summary>
    private static EmailAttachmentTextBounds MessageBoundedTo(
        int maxAttachmentsPerEmail = int.MaxValue,
        long maxInputOctetsPerEmail = long.MaxValue) =>
        EmailAttachmentTextBounds.Disabled with
        {
            MaxAttachmentsPerEmail = maxAttachmentsPerEmail,
            MaxInputOctetsPerEmail = maxInputOctetsPerEmail,
        };

    private static AttachmentTextExtractionResult UnreadableResult(string outcome) => outcome switch
    {
        nameof(AttachmentTextExtractionOutcome.Encrypted) => AttachmentTextExtractionResult.Encrypted(),
        nameof(AttachmentTextExtractionOutcome.Malformed) => AttachmentTextExtractionResult.Malformed(),
        nameof(AttachmentTextExtractionOutcome.FormatNotExtracted) =>
            AttachmentTextExtractionResult.FormatNotExtracted(),
        nameof(AttachmentTextExtractionOutcome.TimedOut) => AttachmentTextExtractionResult.TimedOut(),
        nameof(AttachmentTextExtractionOutcome.InputTooLarge) => AttachmentTextExtractionResult.InputTooLarge(),
        _ => AttachmentTextExtractionResult.ContainerBoundExceeded(),
    };

    private static MimePart Attachment(string fileName, string mediaType, string content)
    {
        var type = mediaType.Split('/');

        return new MimePart(type[0], type[1])
        {
            Content = new MimeContent(new MemoryStream(Encoding.UTF8.GetBytes(content))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            FileName = fileName,
        };
    }

    private static ReadOnlyMemory<byte> MessageAttaching(params MimePart[] attachments)
    {
        var body = new Multipart("mixed") { new TextPart(TextFormat.Plain) { Text = "the plain text" } };

        foreach (var attachment in attachments)
        {
            body.Add(attachment);
        }

        using var message = new MimeMessage { Subject = "Quarterly figures" };
        message.From.Add(new MailboxAddress("Anna", "anna@example.test"));
        message.To.Add(new MailboxAddress("Bruno", "bruno@example.test"));
        message.Body = body;

        return Serialize(message);
    }

    private static ReadOnlyMemory<byte> Compose(string? subject, string plainTextBody, string? htmlBody)
    {
        var builder = new BodyBuilder { TextBody = plainTextBody };

        if (htmlBody is not null)
        {
            builder.HtmlBody = htmlBody;
        }

        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Anna", "anna@example.test"));
        message.To.Add(new MailboxAddress("Bruno", "bruno@example.test"));

        if (subject is not null)
        {
            message.Subject = subject;
        }

        message.Body = builder.ToMessageBody();

        return Serialize(message);
    }

    private static ReadOnlyMemory<byte> Serialize(MimeMessage message)
    {
        using var buffer = new MemoryStream();

        message.WriteTo(buffer);

        return buffer.ToArray().AsMemory();
    }

    /// <summary>Answers for each attachment by the name it declares, and records everything it was offered.</summary>
    /// <remarks>
    /// A file it was told nothing about reads as one no parser recognizes, which is what the real extractor answers for
    /// anything outside the document formats it names.
    /// </remarks>
    private sealed class ScriptedAttachmentTextExtractor : IAttachmentTextExtractor
    {
        private readonly Dictionary<string, AttachmentTextExtractionResult> scripted =
            new(StringComparer.Ordinal);

        private readonly List<ExtractedEmailAttachment> offered = [];

        /// <summary>Gets what the reader handed over, in the order it did.</summary>
        public IReadOnlyList<ExtractedEmailAttachment> Offered => this.offered;

        /// <summary>Gets the octets each offered attachment wrote out, keyed by the file name it declared.</summary>
        /// <remarks>
        /// The fake writes rather than only inspects, because the opened attachment the reader hands over is
        /// production code nothing else can reach: an implementation that decoded the wrong part, wrote nothing, or
        /// failed against a persistent parse mid-walk would leave every assertion about names and outcomes green while
        /// a screened deployment scanned an empty document.
        /// </remarks>
        public Dictionary<string, byte[]> WrittenOctets { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets the names of the files this extractor was asked about.</summary>
        public IReadOnlyList<string> ReadFileNames =>
            [.. this.offered.Select(attachment => attachment.FileName?.Value ?? string.Empty)];

        /// <summary>Gets or sets what happens while one attachment is being read, which a clock test advances from.</summary>
        public Action? WhileReading { get; set; }

        /// <summary>Scripts a file that reads as the text given.</summary>
        public void Reads(string fileName, string text) => this.Reports(
            fileName,
            AttachmentTextExtractionResult.Extracted(new ExtractedAttachmentText(text, PageCount: 1, [], [])));

        /// <summary>Scripts a file that produces one outcome.</summary>
        public void Reports(string fileName, AttachmentTextExtractionResult result) =>
            this.scripted[fileName] = result;

        public async Task<AttachmentTextExtractionResult> ExtractTextAsync(
            IOpenedEmailAttachment attachment,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(attachment);

            this.offered.Add(attachment.Description);

            var fileName = attachment.Description.FileName?.Value ?? string.Empty;

            using var content = new MemoryStream();
            await attachment.WriteContentToAsync(content, cancellationToken);
            this.WrittenOctets[fileName] = content.ToArray();

            this.WhileReading?.Invoke();

            return this.scripted.TryGetValue(fileName, out var scripted)
                ? scripted
                : AttachmentTextExtractionResult.FormatNotRecognized();
        }
    }
}
