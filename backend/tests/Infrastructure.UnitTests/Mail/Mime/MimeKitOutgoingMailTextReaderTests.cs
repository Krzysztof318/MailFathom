// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Extraction.Attachments;
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
            this.timeProvider);

    [Fact]
    public async Task ReadAsync_AMessageWithBothRepresentations_ReadsTheSubjectAndBoth()
    {
        // Arrange
        var raw = Compose("Quarterly figures", "the plain text", "<p>the markup</p>");

        // Act
        var text = await this.reader.ReadAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Quarterly figures", text.Subject);
        Assert.Equal("the plain text", text.PlainTextBody);
        Assert.Equal("<p>the markup</p>", text.HtmlBody);
    }

    [Fact]
    public async Task ReadAsync_AMessageWithNoMarkup_ReportsTheAbsenceRatherThanEmptyText()
    {
        // Arrange
        var raw = Compose("Quarterly figures", "the plain text", htmlBody: null);

        // Act
        var text = await this.reader.ReadAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(text.HtmlBody);
        Assert.Equal(2, text.ScreenedValues.Count);
        Assert.Contains("the plain text", text.PlainTextBody, StringComparison.Ordinal);
    }

    /// <summary>A message nobody titled reads as empty text, so the value list drops it rather than scanning nothing.</summary>
    [Fact]
    public async Task ReadAsync_AMessageWithNoSubject_ReadsItAsEmptyText()
    {
        // Arrange
        var raw = Compose(subject: null, "the plain text", htmlBody: null);

        // Act
        var text = await this.reader.ReadAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(string.Empty, text.Subject);
        Assert.Equal(text.PlainTextBody, Assert.Single(text.ScreenedValues));
    }

    /// <summary>
    /// The markup is returned as it will be transmitted rather than as a sanitizer would allow a browser to render it,
    /// because an attribute a sanitizer strips still leaves in the message.
    /// </summary>
    [Fact]
    public async Task ReadAsync_MarkupASanitizerWouldStrip_ReadsItBackWhole()
    {
        // Arrange
        var raw = Compose(
            "Quarterly figures",
            "the plain text",
            "<p title=\"AKIAEXAMPLEKEY\">the markup</p><!-- AKIAEXAMPLEKEY -->");

        // Act
        var text = await this.reader.ReadAsync(raw, TestContext.Current.CancellationToken);

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
    public async Task ReadAsync_AMessageCarryingAReadableDocument_ScreensItsTextBesideTheBody()
    {
        // Arrange
        this.extractor.Reads("terms.pdf", "the signing key is AKIAEXAMPLEKEY");

        var raw = MessageAttaching(Attachment("terms.pdf", "application/pdf", "%PDF-1.7"));

        // Act
        var text = await this.reader.ReadAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the signing key is AKIAEXAMPLEKEY", Assert.Single(text.AttachmentTexts));
        Assert.Null(text.UnreadableAttachment);
        Assert.Contains("the signing key is AKIAEXAMPLEKEY", text.ScreenedValues, StringComparer.Ordinal);
    }

    /// <summary>
    /// A file nothing recognized as a document is stepped over rather than refused. An image is the ordinary attachment,
    /// and no text scanner ever undertook to read a photograph of anything.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AnAttachmentNoReaderRecognizes_ContributesNothingAndRefusesNothing()
    {
        // Arrange
        var raw = MessageAttaching(Attachment("photo.png", "image/png", "not really a picture"));

        // Act
        var text = await this.reader.ReadAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(text.AttachmentTexts);
        Assert.Null(text.UnreadableAttachment);
        Assert.Equal(2, text.ScreenedValues.Count);
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
    public async Task ReadAsync_ADocumentNothingCouldRead_ReportsTheOutcomeAndNoText(string outcome)
    {
        // Arrange
        this.extractor.Reports("locked.docx", UnreadableResult(outcome));

        var raw = MessageAttaching(Attachment("locked.docx", "application/octet-stream", "PK"));

        // Act
        var text = await this.reader.ReadAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Enum.Parse<AttachmentTextExtractionOutcome>(outcome), text.UnreadableAttachment);
        Assert.Empty(text.AttachmentTexts);
    }

    /// <summary>
    /// One unreadable document refuses the message, so nothing after it is opened and nothing read before it is carried
    /// on: screening part of a message the screen has already decided it cannot judge would be worse than not screening.
    /// </summary>
    [Fact]
    public async Task ReadAsync_ADocumentNothingCouldRead_StopsTheWalkAndDropsWhatCameBefore()
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
        var text = await this.reader.ReadAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.Encrypted, text.UnreadableAttachment);
        Assert.Empty(text.AttachmentTexts);
        Assert.Equal(["first.pdf", "locked.pdf"], this.extractor.ReadFileNames);
    }

    /// <summary>
    /// The output ceiling binds the message rather than each file, or a sender would get it once per attachment. The
    /// answer is the same one a single oversized document produces, because the fact is the same: more text than one
    /// scan of this message undertakes to read.
    /// </summary>
    [Fact]
    public async Task ReadAsync_DocumentsYieldingMoreTextThanTheCeilingTogether_RefusesForTheOutput()
    {
        // Arrange
        var reader = new MimeKitOutgoingMailTextReader(
            this.extractor,
            new AttachmentTextExtractionOptions { MaxExtractedTextCharacters = 12 },
            this.timeProvider);

        this.extractor.Reads("first.pdf", "0123456789");
        this.extractor.Reads("second.pdf", "0123456789");

        var raw = MessageAttaching(
            Attachment("first.pdf", "application/pdf", "%PDF-1.7 one"),
            Attachment("second.pdf", "application/pdf", "%PDF-1.7 two"));

        // Act
        var text = await reader.ReadAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.ExtractedTextTooLarge, text.UnreadableAttachment);
    }

    /// <summary>The input ceiling binds the message the same way, and for the same reason.</summary>
    [Fact]
    public async Task ReadAsync_DocumentsHoldingMoreOctetsThanTheCeilingTogether_RefusesForTheInput()
    {
        // Arrange
        var reader = new MimeKitOutgoingMailTextReader(
            this.extractor,
            new AttachmentTextExtractionOptions { MaxInputOctets = 16 },
            this.timeProvider);

        this.extractor.Reads("first.pdf", "an ordinary invoice");
        this.extractor.Reads("second.pdf", "another ordinary invoice");

        var raw = MessageAttaching(
            Attachment("first.pdf", "application/pdf", "0123456789"),
            Attachment("second.pdf", "application/pdf", "0123456789"));

        // Act
        var text = await reader.ReadAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.InputTooLarge, text.UnreadableAttachment);
    }

    /// <summary>
    /// The per-attachment timeout bounds one read; without a bound across the message a sender would multiply it by
    /// however many small documents they chose to attach.
    /// </summary>
    [Fact]
    public async Task ReadAsync_DocumentsTakingLongerThanTheCeilingTogether_RefusesForTheTime()
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
        var text = await this.reader.ReadAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AttachmentTextExtractionOutcome.TimedOut, text.UnreadableAttachment);
        Assert.Equal(["first.pdf"], this.extractor.ReadFileNames);
    }

    /// <summary>
    /// The extractor decides what to do from the part's own declaration and its decoded length, so the description it is
    /// handed has to be the one measured from this message rather than anything a caller stated.
    /// </summary>
    [Fact]
    public async Task ReadAsync_AnAttachedDocument_IsOfferedTheDescriptionMeasuredFromTheMessage()
    {
        // Arrange
        this.extractor.Reads("terms.pdf", "an ordinary invoice");

        var raw = MessageAttaching(Attachment("terms.pdf", "application/pdf", "%PDF-1.7"));

        // Act
        await this.reader.ReadAsync(raw, TestContext.Current.CancellationToken);

        // Assert
        var offered = Assert.Single(this.extractor.Offered);
        Assert.Equal("terms.pdf", offered.FileName?.Value);
        Assert.Equal("application/pdf", offered.MediaType);
        Assert.Equal("%PDF-1.7"u8.Length, offered.DecodedSizeOctets);
    }

    [Fact]
    public async Task ReadAsync_NoMimeAtAll_Refuses()
    {
        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => this.reader.ReadAsync(ReadOnlyMemory<byte>.Empty, TestContext.Current.CancellationToken));
    }

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

        /// <summary>Gets the names of the files this extractor was asked about.</summary>
        public IReadOnlyList<string> ReadFileNames =>
            [.. this.offered.Select(attachment => attachment.FileName?.Value ?? string.Empty)];

        /// <summary>Gets or sets what happens while one attachment is being read, which a clock test advances from.</summary>
        public Action? WhileReading { get; set; }

        /// <summary>Scripts a file that reads as the text given.</summary>
        public void Reads(string fileName, string text) => this.Reports(
            fileName,
            AttachmentTextExtractionResult.Extracted(new ExtractedAttachmentText(text, PageCount: 1, [])));

        /// <summary>Scripts a file that produces one outcome.</summary>
        public void Reports(string fileName, AttachmentTextExtractionResult result) =>
            this.scripted[fileName] = result;

        public Task<AttachmentTextExtractionResult> ExtractTextAsync(
            IOpenedEmailAttachment attachment,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(attachment);

            this.offered.Add(attachment.Description);
            this.WhileReading?.Invoke();

            return Task.FromResult(
                this.scripted.TryGetValue(attachment.Description.FileName?.Value ?? string.Empty, out var scripted)
                    ? scripted
                    : AttachmentTextExtractionResult.FormatNotRecognized());
        }
    }
}
