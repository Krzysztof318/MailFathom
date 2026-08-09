// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>Covers what a stored message yields for a reader: its headers, its body, and what it carries besides.</summary>
public sealed class MimeKitEmailContentRendererTests
{
    /// <summary>What the sender wrote wins over a reading of how it was displayed.</summary>
    [Fact]
    public async Task RenderAsync_MessageOfferingBothRepresentations_ReturnsThePlainTextPartAsTheBody()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Subject: Quarterly report",
            "Content-Type: multipart/alternative; boundary=\"alt\"",
            string.Empty,
            "--alt",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "The plain body.",
            "--alt",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            "<p>The HTML body.</p>",
            "--alt--");

        // Act
        var rendering = await RenderAsync(content);

        // Assert
        Assert.Equal("The plain body.", rendering.PlainTextBody.Text);
        Assert.Equal("Quarterly report", rendering.Headers.Subject);
        Assert.False(rendering.PlainTextBody.WasTruncated);
    }

    /// <summary>A message with only markup is still readable as words, and the derivation is what produces them.</summary>
    [Fact]
    public async Task RenderAsync_MessageWithOnlyAnHtmlBody_DerivesThePlainTextFromItsMarkup()
    {
        // Arrange
        var content = HtmlOnlyMessage("<p>First line</p><p>Second line</p>");

        // Act
        var rendering = await RenderAsync(content);

        // Assert
        // The blank line between them is the paragraph boundary the markup expressed, which is the one piece of
        // structure the derivation keeps.
        Assert.Equal("First line\n\nSecond line", rendering.PlainTextBody.Text);
    }

    /// <summary>Sanitized HTML costs a pass over hostile markup, so it is produced only when it was asked for.</summary>
    [Fact]
    public async Task RenderAsync_SanitizedHtmlNotRequested_ReturnsNoHtmlRepresentation()
    {
        // Arrange
        var content = HtmlOnlyMessage("<p>Body</p>");

        // Act
        var rendering = await RenderAsync(content, includeSanitizedHtml: false);

        // Assert
        Assert.Null(rendering.SanitizedHtmlBody);
    }

    [Fact]
    public async Task RenderAsync_SanitizedHtmlRequested_ReturnsTheSanitizedMarkup()
    {
        // Arrange
        var content = HtmlOnlyMessage("""<p>Body</p><script>alert('xss')</script>""");

        // Act
        var rendering = await RenderAsync(content, includeSanitizedHtml: true);

        // Assert
        Assert.NotNull(rendering.SanitizedHtmlBody);
        Assert.Contains("Body", rendering.SanitizedHtmlBody.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("script", rendering.SanitizedHtmlBody.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A message that has no HTML part returns none of it even when it was asked for.</summary>
    [Fact]
    public async Task RenderAsync_SanitizedHtmlRequestedAndTheMessageHasNoHtmlPart_ReturnsNoHtmlRepresentation()
    {
        // Arrange
        var content = PlainTextMessage("Just words.");

        // Act
        var rendering = await RenderAsync(content, includeSanitizedHtml: true);

        // Assert
        Assert.Null(rendering.SanitizedHtmlBody);
        Assert.Equal("Just words.", rendering.PlainTextBody.Text);
    }

    /// <summary>A body beyond the bound is cut and says so, together with the length it had.</summary>
    [Fact]
    public async Task RenderAsync_PlainTextBodyBeyondTheBound_CutsItAndReportsTheOriginalLength()
    {
        // Arrange
        var body = new string('a', 500);
        var content = PlainTextMessage(body);

        // Act
        var rendering = await RenderAsync(content, maxBodyCharacters: 100);

        // Assert
        Assert.Equal(100, rendering.PlainTextBody.Text.Length);
        Assert.Equal(500, rendering.PlainTextBody.OriginalCharacterCount);
        Assert.Equal(EmailBodyTruncation.BodyCharacterLimit, rendering.PlainTextBody.Truncation);
    }

    /// <summary>
    /// The narrower of the two bounds is what applies, and which one it was is the caller's next decision: a body cut by
    /// the read's budget is one that would return more in a call naming fewer emails.
    /// </summary>
    [Fact]
    public async Task RenderAsync_ReadBudgetNarrowerThanTheBodyBound_CutsToTheBudgetAndNamesIt()
    {
        // Arrange
        var content = PlainTextMessage(new string('a', 500));

        // Act
        var rendering = await RenderAsync(content, maxBodyCharacters: 100, remainingCharactersForRead: 40);

        // Assert
        Assert.Equal(40, rendering.PlainTextBody.Text.Length);
        Assert.Equal(EmailBodyTruncation.ReadCharacterBudget, rendering.PlainTextBody.Truncation);
    }

    /// <summary>
    /// The plain text is bounded first and the markup gets what is left, so the representation every caller receives is
    /// never starved by the one it opted into.
    /// </summary>
    [Fact]
    public async Task RenderAsync_ReadBudgetCoveringOnlyThePlainText_SpendsItThereAndLeavesTheMarkupEmpty()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/alternative; boundary=\"alt\"",
            string.Empty,
            "--alt",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            new string('a', 500),
            "--alt",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            "<p>" + new string('b', 500) + "</p>",
            "--alt--");

        // Act
        var rendering = await RenderAsync(
            content,
            includeSanitizedHtml: true,
            maxBodyCharacters: 100,
            remainingCharactersForRead: 100);

        // Assert
        Assert.Equal(100, rendering.PlainTextBody.Text.Length);
        Assert.NotNull(rendering.SanitizedHtmlBody);
        Assert.Empty(rendering.SanitizedHtmlBody.Text);
        Assert.Equal(EmailBodyTruncation.ReadCharacterBudget, rendering.SanitizedHtmlBody.Truncation);
    }

    /// <summary>Markup is cut before it is parsed, and the parse then closes what the cut left open.</summary>
    [Fact]
    public async Task RenderAsync_HtmlBodyBeyondTheBound_CutsTheSourceAndStillReturnsBalancedMarkup()
    {
        // Arrange
        var markup = "<p>" + new string('a', 500) + "</p>";
        var content = HtmlOnlyMessage(markup);

        // Act
        var rendering = await RenderAsync(content, includeSanitizedHtml: true, maxBodyCharacters: 100);

        // Assert
        Assert.NotNull(rendering.SanitizedHtmlBody);
        Assert.True(rendering.SanitizedHtmlBody.WasTruncated);
        Assert.Equal(markup.Length, rendering.SanitizedHtmlBody.OriginalCharacterCount);
        Assert.EndsWith("</p>", rendering.SanitizedHtmlBody.Text, StringComparison.Ordinal);
    }

    /// <summary>Deeply nested markup cannot serialize past the bound by spending it all on tags that must be closed.</summary>
    [Fact]
    public async Task RenderAsync_HtmlBodyNestedDeeplyEnoughToDoubleWhenClosed_StaysWithinTheBound()
    {
        // Arrange
        const int maxBodyCharacters = 500;
        var nesting = 200;
        var markup = string.Concat(Enumerable.Repeat("<div>", nesting)) + "Text";
        var content = HtmlOnlyMessage(markup);

        // Act
        var rendering = await RenderAsync(content, includeSanitizedHtml: true, maxBodyCharacters: maxBodyCharacters);

        // Assert
        Assert.NotNull(rendering.SanitizedHtmlBody);
        Assert.True(
            rendering.SanitizedHtmlBody.Text.Length <= maxBodyCharacters,
            $"The sanitized representation is {rendering.SanitizedHtmlBody.Text.Length} characters, past the bound of {maxBodyCharacters}.");
        Assert.True(rendering.SanitizedHtmlBody.WasTruncated);
        Assert.EndsWith("</div>", rendering.SanitizedHtmlBody.Text, StringComparison.Ordinal);
    }

    /// <summary>Whitespace a sender wrote at either edge of a plain-text body is theirs, and survives.</summary>
    [Fact]
    public async Task RenderAsync_PlainTextBodyWithEdgeWhitespace_ReturnsItUnchanged()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "    indented first line",
            "last line",
            "   ");

        // Act
        var rendering = await RenderAsync(content);

        // Assert
        Assert.StartsWith("    indented", rendering.PlainTextBody.Text, StringComparison.Ordinal);
        Assert.EndsWith("   ", rendering.PlainTextBody.Text, StringComparison.Ordinal);
        Assert.Equal(rendering.PlainTextBody.Text.Length, rendering.PlainTextBody.OriginalCharacterCount);
    }

    /// <summary>
    /// A message offering a readable alternative beside an encrypted one has a body something can read, so the
    /// unreadable state would be a false claim. It is reserved for a body that left nothing behind.
    /// </summary>
    [Fact]
    public async Task RenderAsync_AlternativeOfferingReadableTextBesideAnEncryptedMember_ReturnsTheReadableText()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/alternative; boundary=\"alt\"",
            string.Empty,
            "--alt",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "The readable alternative.",
            "--alt",
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
            "-----END PGP MESSAGE-----",
            "--enc--",
            "--alt--");

        // Act
        var rendering = await RenderAsync(content);

        // Assert
        Assert.False(rendering.BodyIsEncrypted);
        Assert.Equal("The readable alternative.", rendering.PlainTextBody.Text);

        // The summary still records that the message carries encrypted content, which is a different question.
        Assert.True(rendering.AttachmentSummary.IsEncrypted);
    }

    /// <summary>
    /// A budget the earlier emails of the same call already spent empties this representation for a reason that belongs
    /// to the call rather than to the message. Reading that as the encrypted-unreadable state would tell a caller the
    /// message can never be read locally, when naming it alone returns the readable alternative in full.
    /// </summary>
    [Fact]
    public async Task RenderAsync_ReadableAlternativeBesideAnEncryptedMemberStarvedByTheBudget_StaysReadableAndSaysTheBudgetCutIt()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/alternative; boundary=\"alt\"",
            string.Empty,
            "--alt",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "The readable alternative.",
            "--alt",
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
            "-----END PGP MESSAGE-----",
            "--enc--",
            "--alt--");

        // Act
        var rendering = await RenderAsync(content, remainingCharactersForRead: 0);

        // Assert
        Assert.False(rendering.BodyIsEncrypted);
        Assert.Empty(rendering.PlainTextBody.Text);
        Assert.Equal(EmailBodyTruncation.ReadCharacterBudget, rendering.PlainTextBody.Truncation);
        Assert.Equal("The readable alternative.".Length, rendering.PlainTextBody.OriginalCharacterCount);
    }

    /// <summary>The exhausted budget must not turn a genuinely encrypted body into a readable one either.</summary>
    [Fact]
    public async Task RenderAsync_EncryptedBodyStarvedByTheBudget_StillReportsItAsEncrypted()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
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
            "-----END PGP MESSAGE-----",
            "--enc--");

        // Act
        var rendering = await RenderAsync(content, remainingCharactersForRead: 0);

        // Assert
        Assert.True(rendering.BodyIsEncrypted);
        Assert.Empty(rendering.PlainTextBody.Text);
    }

    /// <summary>One header cannot decide how large a result is by carrying an unbounded number of addresses.</summary>
    [Fact]
    public async Task RenderAsync_HeaderCarryingMoreAddressesThanTheBound_ReturnsNoMoreThanTheBoundForThatRole()
    {
        // Arrange
        var recipients = string.Join(
            ", ",
            Enumerable.Range(0, EmailParticipant.MaximumPerRole + 50).Select(index => $"recipient{index}@example.test"));
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            $"To: {recipients}",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "Body");

        // Act
        var rendering = await RenderAsync(content);

        // Assert
        Assert.Equal(
            EmailParticipant.MaximumPerRole,
            rendering.Headers.Participants.Count(participant => participant.Role == EmailAddressRole.To));
    }

    /// <summary>An encrypted body is reported as one, not as a message that said nothing.</summary>
    [Fact]
    public async Task RenderAsync_MessageWhoseBodyArrivedEncrypted_ReportsItRatherThanReturningAnEmptyBody()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
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
            "-----END PGP MESSAGE-----",
            "--enc--");

        // Act
        var rendering = await RenderAsync(content);

        // Assert
        Assert.True(rendering.BodyIsEncrypted);
        Assert.Equal(string.Empty, rendering.PlainTextBody.Text);
        Assert.True(rendering.AttachmentSummary.IsEncrypted);
    }

    /// <summary>
    /// The octets a caller receives are what the transfer encoding decoded to, which is the whole point of returning
    /// them: a caller handed the encoded form would be handed the message's storage rather than the file.
    /// </summary>
    [Fact]
    public async Task RenderAsync_AttachmentContentAskedForAndWithinTheBounds_ReturnsTheDecodedOctets()
    {
        // Act
        var rendering = await RenderAsync(
            MessageAttaching("pdf-bytes"),
            attachmentContent: new EmailAttachmentContentBounds(MaxOctetsPerAttachment: 1024, RemainingOctetsForRead: 1024));

        // Assert
        var attachment = Assert.Single(rendering.Attachments ?? []);
        Assert.Equal(EmailAttachmentContentAvailability.Returned, attachment.Content.Availability);
        Assert.Equal("pdf-bytes"u8.ToArray(), attachment.Content.Octets.ToArray());
        Assert.Equal("pdf-bytes".Length, attachment.Description.DecodedSizeOctets);
    }

    /// <summary>A read that asked for no attachment content publishes no attachment list, so nothing can hold octets.</summary>
    [Fact]
    public async Task RenderAsync_AttachmentContentNotAskedFor_MeasuresTheAttachmentAndPublishesNoList()
    {
        // Act
        var rendering = await RenderAsync(MessageAttaching("pdf-bytes"));

        // Assert
        Assert.Null(rendering.Attachments);
        Assert.Equal("pdf-bytes".Length, Assert.Single(rendering.AttachmentSummary.Attachments).DecodedSizeOctets);
    }

    /// <summary>
    /// A file above either bound is measured, described, and released rather than returned in part. The size is still
    /// what the message holds, because that is what tells a caller what it did not receive.
    /// </summary>
    [Theory]
    [InlineData(4, 1024, nameof(EmailAttachmentContentAvailability.ExceededAttachmentByteLimit))]
    [InlineData(1024, 4, nameof(EmailAttachmentContentAvailability.ReadByteBudgetExhausted))]
    public async Task RenderAsync_AttachmentAboveOneOfTheOctetBounds_ReturnsNoContentAndNamesThatBound(
        int maxOctetsPerAttachment,
        int remainingOctetsForRead,
        string expectedAvailability)
    {
        // Act
        var rendering = await RenderAsync(
            MessageAttaching("pdf-bytes"),
            attachmentContent: new EmailAttachmentContentBounds(maxOctetsPerAttachment, remainingOctetsForRead));

        // Assert
        var attachment = Assert.Single(rendering.Attachments ?? []);
        Assert.Equal(expectedAvailability, attachment.Content.Availability.ToString());
        Assert.True(attachment.Content.Octets.IsEmpty);
        Assert.Equal("pdf-bytes".Length, attachment.Description.DecodedSizeOctets);
    }

    /// <summary>
    /// The budget falls to the attachments of one message in the order they were walked, so a message whose first file
    /// spends it leaves the second described and empty rather than shortening either of them.
    /// </summary>
    [Fact]
    public async Task RenderAsync_SecondAttachmentReachedAfterTheBudgetIsSpent_ReturnsTheFirstAndWithholdsTheSecond()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/mixed; boundary=\"mix\"",
            string.Empty,
            "--mix",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "Two files.",
            "--mix",
            "Content-Type: application/pdf",
            "Content-Disposition: attachment; filename=\"first.pdf\"",
            string.Empty,
            "first",
            "--mix",
            "Content-Type: application/pdf",
            "Content-Disposition: attachment; filename=\"second.pdf\"",
            string.Empty,
            "second",
            "--mix--");

        // Act
        var rendering = await RenderAsync(
            content,
            attachmentContent: new EmailAttachmentContentBounds(
                MaxOctetsPerAttachment: 1024,
                RemainingOctetsForRead: "first".Length));

        // Assert
        var attachments = rendering.Attachments ?? [];
        Assert.Equal(2, attachments.Count);
        Assert.Equal(EmailAttachmentContentAvailability.Returned, attachments[0].Content.Availability);
        Assert.Equal("first"u8.ToArray(), attachments[0].Content.Octets.ToArray());
        Assert.Equal(
            EmailAttachmentContentAvailability.ReadByteBudgetExhausted,
            attachments[1].Content.Availability);
    }

    /// <summary>An embedded resource is not a file, so asking for attachment content never returns one.</summary>
    [Fact]
    public async Task RenderAsync_AttachmentContentAskedForOnAMessageEmbeddingAnImage_ReturnsNoContentForTheResource()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/related; boundary=\"rel\"",
            string.Empty,
            "--rel",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            """<p>Chart:</p><img src="cid:chart@example.test">""",
            "--rel",
            "Content-Type: image/png",
            "Content-ID: <chart@example.test>",
            string.Empty,
            "image",
            "--rel--");

        // Act
        var rendering = await RenderAsync(
            content,
            attachmentContent: new EmailAttachmentContentBounds(MaxOctetsPerAttachment: 1024, RemainingOctetsForRead: 1024));

        // Assert
        Assert.Empty(rendering.Attachments ?? []);
        Assert.Equal(1, rendering.AttachmentSummary.InlineResourceCount);
    }

    /// <summary>The per-attachment list is what the same parse found, with names normalized.</summary>
    [Fact]
    public async Task RenderAsync_MessageCarryingAnAttachment_DescribesItWithoutItsContent()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/mixed; boundary=\"mix\"",
            string.Empty,
            "--mix",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "See the attachment.",
            "--mix",
            "Content-Type: application/pdf",
            "Content-Disposition: attachment; filename=\"../../etc/report.pdf\"",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            Convert.ToBase64String(Encoding.UTF8.GetBytes("pdf-bytes")),
            "--mix--");

        // Act
        var rendering = await RenderAsync(content);

        // Assert
        var attachment = Assert.Single(rendering.AttachmentSummary.Attachments);
        Assert.Equal("report.pdf", attachment.FileName?.Value);
        Assert.Equal("application/pdf", attachment.MediaType);
        Assert.Equal("pdf-bytes".Length, attachment.DecodedSizeOctets);
        Assert.Equal("See the attachment.", rendering.PlainTextBody.Text);
    }

    /// <summary>A signature is not a file a person would open, so it never reaches the attachment list.</summary>
    [Fact]
    public async Task RenderAsync_SignedMessage_ReturnsNoAttachmentForItsSignaturePart()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/signed; protocol=\"application/pkcs7-signature\"; micalg=sha-256; boundary=\"sig\"",
            string.Empty,
            "--sig",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "Signed words.",
            "--sig",
            "Content-Type: application/pkcs7-signature; name=\"smime.p7s\"",
            "Content-Disposition: attachment; filename=\"smime.p7s\"",
            string.Empty,
            "signature",
            "--sig--");

        // Act
        var rendering = await RenderAsync(content);

        // Assert
        Assert.Empty(rendering.AttachmentSummary.Attachments);
        Assert.True(rendering.AttachmentSummary.CarriesUnverifiedSignature);
        Assert.Equal("Signed words.", rendering.PlainTextBody.Text);
    }

    /// <summary>An embedded image is counted rather than listed, which is what a reader is told instead of the image.</summary>
    [Fact]
    public async Task RenderAsync_HtmlBodyEmbeddingAnImage_CountsItAsAnInlineResourceRatherThanAnAttachment()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/related; boundary=\"rel\"",
            string.Empty,
            "--rel",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            """<p>Chart:</p><img src="cid:chart@example.test">""",
            "--rel",
            "Content-Type: image/png",
            "Content-ID: <chart@example.test>",
            string.Empty,
            "image",
            "--rel--");

        // Act
        var rendering = await RenderAsync(content, includeSanitizedHtml: true);

        // Assert
        Assert.Empty(rendering.AttachmentSummary.Attachments);
        Assert.Equal(1, rendering.AttachmentSummary.InlineResourceCount);
        Assert.DoesNotContain("cid:", rendering.SanitizedHtmlBody!.Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Bytes that are not a message leave nothing a reader could be shown.</summary>
    [Fact]
    public async Task RenderAsync_ContentThatIsNotAMimeMessage_ReportsItAsUnreadable()
    {
        // Arrange
        var content = MimeFixtures.StoredRawContent([0x00, 0x01, 0x02, 0x03]);

        // Act
        var result = await CreateRenderer().RenderAsync(
            content,
            BoundsOf(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailContentRenderingOutcome.Unreadable, result.Outcome);
        Assert.Null(result.Rendering);
    }

    /// <summary>A message beyond the structural limits is abandoned here exactly as it is during extraction.</summary>
    [Fact]
    public async Task RenderAsync_MessageDeclaringMorePartsThanTheLimit_ReportsItAsUnreadable()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: sender@example.test",
            "Content-Type: multipart/mixed; boundary=\"mix\"",
            string.Empty,
            "--mix",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "First",
            "--mix",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "Second",
            "--mix--");
        var renderer = CreateRenderer(maxPartCount: 1);

        // Act
        var result = await renderer.RenderAsync(
            content,
            BoundsOf(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(EmailContentRenderingOutcome.Unreadable, result.Outcome);
    }

    /// <summary>Every participant the message wrote is returned, including the ones a listing row cannot hold.</summary>
    [Fact]
    public async Task RenderAsync_MessageAddressingSeveralHeaders_ReturnsEveryParticipantUnderItsRole()
    {
        // Arrange
        var content = MimeFixtures.StoredMessage(
            "From: Anna Kowalska <anna@example.test>",
            "To: recipient@example.test",
            "Cc: copied@example.test",
            "Reply-To: replies@example.test",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "Body");

        // Act
        var rendering = await RenderAsync(content);

        // Assert
        Assert.Equal(
            [EmailAddressRole.From, EmailAddressRole.ReplyTo, EmailAddressRole.To, EmailAddressRole.Cc],
            rendering.Headers.Participants.Select(participant => participant.Role));
        Assert.Equal("Anna Kowalska", rendering.Headers.Participants[0].Address.DisplayName);
    }

    private static async Task<EmailContentRendering> RenderAsync(
        StoredEmailContent content,
        bool includeSanitizedHtml = false,
        int maxBodyCharacters = 100_000,
        int remainingCharactersForRead = int.MaxValue,
        EmailAttachmentContentBounds? attachmentContent = null)
    {
        var result = await CreateRenderer().RenderAsync(
            content,
            BoundsOf(includeSanitizedHtml, maxBodyCharacters, remainingCharactersForRead, attachmentContent),
            TestContext.Current.CancellationToken);

        Assert.Equal(EmailContentRenderingOutcome.Rendered, result.Outcome);

        return result.Rendering!;
    }

    private static EmailContentRenderingBounds BoundsOf(
        bool includeSanitizedHtml = false,
        int maxBodyCharacters = 100_000,
        int remainingCharactersForRead = int.MaxValue,
        EmailAttachmentContentBounds? attachmentContent = null) =>
        new(includeSanitizedHtml, maxBodyCharacters, remainingCharactersForRead, attachmentContent);

    /// <summary>Builds a message whose single attachment is the given text, carried under a transfer encoding.</summary>
    private static StoredEmailContent MessageAttaching(string attachedText) => MimeFixtures.StoredMessage(
        "From: sender@example.test",
        "Content-Type: multipart/mixed; boundary=\"mix\"",
        string.Empty,
        "--mix",
        "Content-Type: text/plain; charset=utf-8",
        string.Empty,
        "See the attachment.",
        "--mix",
        "Content-Type: application/pdf",
        "Content-Disposition: attachment; filename=\"report.pdf\"",
        "Content-Transfer-Encoding: base64",
        string.Empty,
        Convert.ToBase64String(Encoding.UTF8.GetBytes(attachedText)),
        "--mix--");

    private static MimeKitEmailContentRenderer CreateRenderer(int maxPartCount = 1000) =>
        new(new EmailMimeExtractionOptions { MaxPartCount = maxPartCount });

    private static StoredEmailContent PlainTextMessage(string body) => MimeFixtures.StoredMessage(
        "From: sender@example.test",
        "Content-Type: text/plain; charset=utf-8",
        string.Empty,
        body);

    private static StoredEmailContent HtmlOnlyMessage(string markup) => MimeFixtures.StoredMessage(
        "From: sender@example.test",
        "Content-Type: text/html; charset=utf-8",
        string.Empty,
        markup);
}
