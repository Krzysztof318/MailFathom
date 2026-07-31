// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.EmailContent;
using MailFathom.Application.Emails;
using MailFathom.Infrastructure.Mail.Mime;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests;

/// <summary>Covers the searchable text one message's body yields, which is what the lexical index is built from.</summary>
public sealed class EmailBodyTextExtractionTests
{
    /// <summary>A plain-text alternative is what the sender wrote, so it wins over the HTML rendering of the same words.</summary>
    [Fact]
    public async Task ReadMetadataAsync_AlternativeBody_PrefersThePlainTextPart()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Alternative",
            "Content-Type: multipart/alternative; boundary=\"alt\"",
            string.Empty,
            "--alt",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "The plain reading.",
            "--alt",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            "<html><body><p>The marked-up reading.</p></body></html>",
            "--alt--");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal(ExtractedEmailTextSource.PlainTextBodyPart, text.Source);
        Assert.False(text.IsDerivedFromHtml);
        Assert.Equal("The plain reading.", text.TrimmedText);
    }

    /// <summary>Without a plain-text alternative the text is inferred from markup, and the record says so.</summary>
    [Fact]
    public async Task ReadMetadataAsync_HtmlOnlyBody_DerivesTextAndMarksItLossy()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Markup only",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            "<html><body><h1>Invoice</h1><p>Amount due: 100&nbsp;PLN &amp; VAT.</p>",
            "<p>Second paragraph.</p></body></html>");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal(ExtractedEmailTextSource.DerivedFromHtmlBodyPart, text.Source);
        Assert.True(text.IsDerivedFromHtml);
        // Block elements become line breaks and a non-breaking space becomes an ordinary one, so the derived
        // text is the words a reader saw rather than the markup that displayed them.
        Assert.Equal("Invoice\n\nAmount due: 100 PLN & VAT.\n\nSecond paragraph.", text.TrimmedText);
    }

    /// <summary>Script and style content is machinery rather than words, so no query can match on it.</summary>
    [Fact]
    public async Task ReadMetadataAsync_HtmlBodyWithScriptAndStyle_IndexesNeither()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Machinery",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            "<html><head><title>Ignored title</title><style>.hidden { color: red; }</style></head>",
            "<body><script>var secret = 'scriptword';</script><p>Visible sentence.</p></body></html>");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal("Visible sentence.", text.TrimmedText);
    }

    /// <summary>A void element never gets an end tag, so treating one as a container would swallow the rest of the body.</summary>
    [Fact]
    public async Task ReadMetadataAsync_HtmlFragmentWithAVoidElement_StillReadsTheTextAfterIt()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Fragment",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            "<meta http-equiv=\"Content-Type\" content=\"text/html\">",
            "<link rel=\"stylesheet\" href=\"https://example.test/mail.css\">",
            "<div>Everything after the void elements.</div>");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal("Everything after the void elements.", text.TrimmedText);
    }

    /// <summary>A plain-text file beside an HTML body is an attachment, so its contents never become the message's text.</summary>
    [Fact]
    public async Task ReadMetadataAsync_PlainTextAttachmentBesideAnHtmlBody_DerivesTextFromTheBodyOnly()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Mixed",
            "Content-Type: multipart/mixed; boundary=\"mix\"",
            string.Empty,
            "--mix",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            "<html><body><p>Body sentence.</p></body></html>",
            "--mix",
            "Content-Type: text/plain; charset=utf-8",
            "Content-Disposition: attachment; filename=\"notes.txt\"",
            string.Empty,
            "Attachment sentence.",
            "--mix--");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal(ExtractedEmailTextSource.DerivedFromHtmlBodyPart, text.Source);
        Assert.Equal("Body sentence.", text.TrimmedText);
    }

    /// <summary>An encrypted body is unreadable rather than empty, and the two must stay distinguishable in search.</summary>
    [Fact]
    public async Task ReadMetadataAsync_EncryptedMessage_RecordsNoExtractableTextWithTheEncryptedReason()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Sealed",
            "Content-Type: multipart/encrypted; protocol=\"application/pgp-encrypted\"; boundary=\"enc\"",
            string.Empty,
            "--enc",
            "Content-Type: application/pgp-encrypted",
            string.Empty,
            "Version: 1",
            "--enc",
            "Content-Type: application/octet-stream",
            string.Empty,
            "-----BEGIN PGP MESSAGE-----",
            "--enc--");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal(ExtractedEmailTextSource.EncryptedBody, text.Source);
        Assert.False(text.HasText);
        Assert.Null(text.TrimmedText);
        Assert.Null(text.OriginalText);
    }

    /// <summary>
    /// An encrypted attachment says nothing about whether this message's own body can be read. Reading the summary's
    /// marker instead of the body's would discard a body its author wrote and can see.
    /// </summary>
    [Fact]
    public async Task ReadMetadataAsync_EncryptedAttachmentBesideAReadableBody_StillExtractsTheBody()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Forwarding a sealed message",
            "Content-Type: multipart/mixed; boundary=\"mix\"",
            string.Empty,
            "--mix",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "Here is the sealed message you asked for.",
            "--mix",
            "Content-Type: application/pkcs7-mime; smime-type=enveloped-data; name=\"smime.p7m\"",
            "Content-Disposition: attachment; filename=\"smime.p7m\"",
            string.Empty,
            "MIIBsealed",
            "--mix--");

        // Act
        var result = await CreateReader(new EmailMimeExtractionOptions())
            .ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Metadata);
        Assert.Equal(ExtractedEmailTextSource.PlainTextBodyPart, result.Metadata.Text.Source);
        Assert.Equal("Here is the sealed message you asked for.", result.Metadata.Text.TrimmedText);

        // The summary keeps its own meaning: the message does carry encrypted content, and a mailbox filter asks that.
        Assert.True(result.Metadata.Attachments.IsEncrypted);
    }

    /// <summary>A message whose body says nothing is a complete record, and it must not look like an encrypted one.</summary>
    [Fact]
    public async Task ReadMetadataAsync_MessageWithoutATextualBody_RecordsNoTextualBody()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Nothing to read",
            "Content-Type: application/octet-stream",
            "Content-Disposition: attachment; filename=\"report.bin\"",
            string.Empty,
            "binary");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal(ExtractedEmailTextSource.NoTextualBodyPart, text.Source);
        Assert.False(text.HasText);
    }

    /// <summary>The quoted message a reply sits above is removed from the index, and the untrimmed reading is kept.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ReplyAboveQuotedHistory_TrimsTheQuoteAndRetainsTheOriginal()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Re: Invoice",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "Confirmed, the amount is correct.",
            string.Empty,
            "On Tue, 28 Jul 2026, Bob <bob@example.test> wrote:",
            "> Could you confirm the amount?",
            ">",
            "> Thanks, Bob");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal("Confirmed, the amount is correct.", text.TrimmedText);
        Assert.Contains("Could you confirm the amount?", text.OriginalText, StringComparison.Ordinal);
    }

    /// <summary>An RFC 3676 signature separator ends the message as far as the index is concerned.</summary>
    [Fact]
    public async Task ReadMetadataAsync_BodyWithASignature_TrimsFromTheSeparator()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Signed off",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "The report is attached.",
            "--",
            "Anna Kowalska",
            "Example sp. z o.o.");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal("The report is attached.", text.TrimmedText);
        Assert.Contains("Anna Kowalska", text.OriginalText, StringComparison.Ordinal);
    }

    /// <summary>The forwarding separator several clients write ends the message the same way.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ForwardedMessage_TrimsFromTheOriginalMessageMarker()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: FW: Invoice",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "Please handle this one.",
            string.Empty,
            "-----Original Message-----",
            "From: bob@example.test",
            "Subject: Invoice",
            string.Empty,
            "The invoice is overdue.");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal("Please handle this one.", text.TrimmedText);
        Assert.Contains("The invoice is overdue.", text.OriginalText, StringComparison.Ordinal);
    }

    /// <summary>
    /// The topmost marker is the outermost one. Cutting at the innermost would index every forwarded message above it
    /// as though its text belonged to this one.
    /// </summary>
    [Fact]
    public async Task ReadMetadataAsync_ForwardedChain_TrimsFromTheOutermostMarker()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: FW: FW: Invoice",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "Passing this along.",
            string.Empty,
            "-----Original Message-----",
            "From: bob@example.test",
            string.Empty,
            "Bob forwarded this to me.",
            string.Empty,
            "-----Original Message-----",
            "From: carol@example.test",
            string.Empty,
            "Carol wrote the invoice.");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal("Passing this along.", text.TrimmedText);
        Assert.DoesNotContain("Bob forwarded this to me.", text.TrimmedText, StringComparison.Ordinal);
        Assert.Contains("Carol wrote the invoice.", text.OriginalText, StringComparison.Ordinal);
    }

    /// <summary>A message that is nothing but a quoted block is a message whose whole content is that block.</summary>
    [Fact]
    public async Task ReadMetadataAsync_BodyThatIsEntirelyQuoted_TrimsNothing()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Re: Invoice",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "> Could you confirm the amount?",
            ">",
            "> Thanks, Bob");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal(text.OriginalText, text.TrimmedText);
        Assert.Contains("Could you confirm the amount?", text.TrimmedText, StringComparison.Ordinal);
    }

    /// <summary>A quotation a reply was written underneath is not trailing history, so the answer below it survives.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ReplyWrittenBelowTheQuote_TrimsNothing()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Re: Invoice",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "> Could you confirm the amount?",
            string.Empty,
            "Confirmed, the amount is correct.");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal(text.OriginalText, text.TrimmedText);
        Assert.Contains("Confirmed, the amount is correct.", text.TrimmedText, StringComparison.Ordinal);
    }

    /// <summary>A sentence that merely ends in "wrote:" is prose, not an attribution, so nothing after it is removed.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ProseMentioningWriting_TrimsNothing()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Notes",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "Here is what the auditor wrote:",
            "The figures balance.");

        // Act
        var text = await ExtractTextAsync(content);

        // Assert
        Assert.Equal("Here is what the auditor wrote:\nThe figures balance.", text.TrimmedText);
    }

    /// <summary>The body is bounded, because an unbounded one would make the generated search vector unwritable.</summary>
    [Fact]
    public async Task ReadMetadataAsync_BodyLongerThanTheConfiguredBound_KeepsOnlyTheBoundedPrefix()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Long",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            new string('a', 500));

        // Act
        var text = await ExtractTextAsync(content, maxExtractedTextCharacters: 100);

        // Assert
        Assert.Equal(100, text.OriginalText?.Length);
        Assert.Equal(new string('a', 100), text.TrimmedText);
    }

    /// <summary>Extraction reports the text on the same record as the participants, so one row is one reading of one message.</summary>
    [Fact]
    public async Task ReadMetadataAsync_PlainMessage_ReportsTextBesideTheRestOfTheMetadata()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Together",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "One reading.");

        // Act
        var result = await CreateReader(new EmailMimeExtractionOptions())
            .ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        Assert.Equal(EmailMimeExtractionOutcome.Extracted, result.Outcome);
        Assert.Equal("Together", result.Metadata?.Subject);
        Assert.Equal("One reading.", result.Metadata?.Text.TrimmedText);
    }

    private static MimeKitEmailMimeReader CreateReader(EmailMimeExtractionOptions options) => new(options);

    private static async Task<ExtractedEmailText> ExtractTextAsync(
        RemoteEmailContent content,
        int? maxExtractedTextCharacters = null)
    {
        var options = new EmailMimeExtractionOptions();
        if (maxExtractedTextCharacters is { } bound)
        {
            options.MaxExtractedTextCharacters = bound;
        }

        var result = await CreateReader(options).ReadMetadataAsync(content, CancellationToken.None);

        Assert.Equal(EmailMimeExtractionOutcome.Extracted, result.Outcome);
        Assert.NotNull(result.Metadata);

        return result.Metadata.Text;
    }
}
