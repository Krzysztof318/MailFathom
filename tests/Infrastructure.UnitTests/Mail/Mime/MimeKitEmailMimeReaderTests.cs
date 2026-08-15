// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Mail;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

public sealed class MimeKitEmailMimeReaderTests
{
    /// <summary>Every address the message wrote reaches the record under the header it was written in.</summary>
    [Fact]
    public async Task ReadMetadataAsync_PlainMessage_RecordsParticipantsSubjectAndDates()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: Anna Kowalska <Anna.Kowalska@Example.Test>",
            "Sender: submitter@example.test",
            "Reply-To: replies@example.test",
            "To: Bob <bob@example.test>, carol@example.test",
            "Cc: dave@example.test",
            "Received: from relay.example.test by mail.example.test; Tue, 28 Jul 2026 09:15:00 +0200",
            "Date: Tue, 28 Jul 2026 09:00:00 +0200",
            "Subject: =?utf-8?Q?Faktura_wrzesie=C5=84?=",
            "Message-Id: <message-1@example.test>",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "Dzień dobry.");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var metadata = AssertExtracted(result);
        Assert.Equal("Faktura wrzesień", metadata.Subject);
        Assert.Equal(new DateTimeOffset(2026, 7, 28, 7, 0, 0, TimeSpan.Zero), metadata.SentAt);
        Assert.Equal(new DateTimeOffset(2026, 7, 28, 7, 15, 0, TimeSpan.Zero), metadata.ReceivedAt);
        Assert.Equal("message-1@example.test", metadata.ThreadReferences.MessageId);
        Assert.Equal(
            [
                (EmailAddressRole.Sender, "SUBMITTER@EXAMPLE.TEST"),
                (EmailAddressRole.From, "ANNA.KOWALSKA@EXAMPLE.TEST"),
                (EmailAddressRole.ReplyTo, "REPLIES@EXAMPLE.TEST"),
                (EmailAddressRole.To, "BOB@EXAMPLE.TEST"),
                (EmailAddressRole.To, "CAROL@EXAMPLE.TEST"),
                (EmailAddressRole.Cc, "DAVE@EXAMPLE.TEST"),
            ],
            metadata.Participants.Select(participant => (participant.Role, participant.Address.NormalizedAddress)));
        Assert.False(metadata.Attachments.HasAttachments);
    }

    /// <summary>A message that wrote no <c>Date</c> header has no sent timestamp rather than a guessed one.</summary>
    [Fact]
    public async Task ReadMetadataAsync_MessageWithoutDateHeader_RecordsNoSentTimestamp()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: No date",
            "Content-Type: text/plain",
            string.Empty,
            "Body");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var metadata = AssertExtracted(result);
        Assert.Null(metadata.SentAt);
        Assert.Null(metadata.ReceivedAt);
        Assert.Same(EmailThreadReferences.None, metadata.ThreadReferences);
    }

    /// <summary>The body branch is resolved recursively, so the most ordinary message there is reports one attachment rather than two.</summary>
    [Fact]
    public async Task ReadMetadataAsync_MixedMessageWithABodyAndOneFile_ReportsOneAttachment()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Subject: Invoice",
            "Content-Type: multipart/mixed; boundary=\"mixed\"",
            string.Empty,
            "--mixed",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "The invoice is attached.",
            "--mixed",
            "Content-Type: application/pdf; name=\"invoice.pdf\"",
            "Content-Disposition: attachment; filename=\"invoice.pdf\"",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            "SGVsbG8sIHdvcmxkIQ==",
            "--mixed--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        var attachment = Assert.Single(attachments.Attachments);
        Assert.Equal("invoice.pdf", attachment.FileName?.Value);
        Assert.Equal("application/pdf", attachment.MediaType);
        Assert.True(attachments.HasAttachments);
    }

    /// <summary>Size is the decoded octet count, which a part's encoded length is not.</summary>
    [Fact]
    public async Task ReadMetadataAsync_Base64Attachment_MeasuresItsDecodedLength()
    {
        // Arrange
        const string decodedPayload = "Hello, world!";
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/mixed; boundary=\"mixed\"",
            string.Empty,
            "--mixed",
            "Content-Type: text/plain",
            string.Empty,
            "Body",
            "--mixed",
            "Content-Type: application/octet-stream; name=\"payload.bin\"",
            "Content-Disposition: attachment; filename=\"payload.bin\"",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            Convert.ToBase64String(Encoding.UTF8.GetBytes(decodedPayload)),
            "--mixed--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.Equal(decodedPayload.Length, Assert.Single(attachments.Attachments).DecodedSizeOctets);
        Assert.Equal(decodedPayload.Length, attachments.TotalSizeOctets);
    }

    /// <summary>Cryptographic classification precedes disposition, which is the whole point of the ordering.</summary>
    [Fact]
    public async Task ReadMetadataAsync_SignedMessageWhoseSignatureDeclaresItselfAnAttachment_ReportsNoAttachment()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/signed; protocol=\"application/pkcs7-signature\"; micalg=sha-256; boundary=\"signed\"",
            string.Empty,
            "--signed",
            "Content-Type: text/plain",
            string.Empty,
            "Signed body",
            "--signed",
            "Content-Type: application/pkcs7-signature; name=\"smime.p7s\"",
            "Content-Disposition: attachment; filename=\"smime.p7s\"",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            "SGVsbG8sIHdvcmxkIQ==",
            "--signed--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.Empty(attachments.Attachments);
        Assert.False(attachments.HasAttachments);
        Assert.True(attachments.CarriesUnverifiedSignature);
        Assert.False(attachments.IsEncrypted);
    }

    /// <summary>The envelope is recognized from the container, so PGP ciphertext typed as an octet stream is not reported as a file.</summary>
    [Fact]
    public async Task ReadMetadataAsync_PgpEncryptedEnvelope_ReportsNoAttachmentAndMarksItEncrypted()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/encrypted; protocol=\"application/pgp-encrypted\"; boundary=\"encrypted\"",
            string.Empty,
            "--encrypted",
            "Content-Type: application/pgp-encrypted",
            string.Empty,
            "Version: 1",
            "--encrypted",
            "Content-Type: application/octet-stream; name=\"encrypted.asc\"",
            "Content-Disposition: inline; filename=\"encrypted.asc\"",
            string.Empty,
            "-----BEGIN PGP MESSAGE-----",
            "ciphertext",
            "-----END PGP MESSAGE-----",
            "--encrypted--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.Empty(attachments.Attachments);
        Assert.Equal(0, attachments.InlineResourceCount);
        Assert.True(attachments.IsEncrypted);
        Assert.False(attachments.CarriesUnverifiedSignature);
    }

    /// <summary>An envelope is what its protocol parameter says it is, so a container declaring none must not hide a file.</summary>
    [Fact]
    public async Task ReadMetadataAsync_EncryptedContainerDeclaringNoProtocol_KeepsItsChildrenInTheSummary()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/encrypted; boundary=\"encrypted\"",
            string.Empty,
            "--encrypted",
            "Content-Type: text/plain",
            string.Empty,
            "Body",
            "--encrypted",
            "Content-Type: application/pdf; name=\"report.pdf\"",
            "Content-Disposition: attachment; filename=\"report.pdf\"",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            "SGVsbG8sIHdvcmxkIQ==",
            "--encrypted--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.False(attachments.IsEncrypted);
        var attachment = Assert.Single(attachments.Attachments);
        Assert.Equal("report.pdf", attachment.FileName?.Value);
    }

    /// <summary>An opaque S/MIME part replaces the body, so the record must say what happened to it rather than look empty.</summary>
    [Theory]
    [InlineData("enveloped-data", true, false)]
    [InlineData("authEnveloped-data", true, false)]
    [InlineData("signed-data", false, true)]
    public async Task ReadMetadataAsync_OpaqueSmimeMessage_MarksWhatReplacedTheBody(
        string smimeType,
        bool expectedEncrypted,
        bool expectedUnverifiedSignature)
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            $"Content-Type: application/pkcs7-mime; smime-type={smimeType}; name=\"smime.p7m\"",
            "Content-Disposition: attachment; filename=\"smime.p7m\"",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            "SGVsbG8sIHdvcmxkIQ==");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.Empty(attachments.Attachments);
        Assert.Equal(expectedEncrypted, attachments.IsEncrypted);
        Assert.Equal(expectedUnverifiedSignature, attachments.CarriesUnverifiedSignature);
    }

    /// <summary>A signed container carrying more than its two defined children must not lose the extra ones.</summary>
    [Fact]
    public async Task ReadMetadataAsync_SignedContainerWithAnExtraChild_KeepsThatChildInTheSummary()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/signed; protocol=\"application/pkcs7-signature\"; micalg=sha-256; boundary=\"signed\"",
            string.Empty,
            "--signed",
            "Content-Type: text/plain",
            string.Empty,
            "Signed body",
            "--signed",
            "Content-Type: application/pkcs7-signature; name=\"smime.p7s\"",
            "Content-Disposition: attachment; filename=\"smime.p7s\"",
            string.Empty,
            "signature",
            "--signed",
            "Content-Type: application/pdf; name=\"smuggled.pdf\"",
            "Content-Disposition: attachment; filename=\"smuggled.pdf\"",
            string.Empty,
            "payload",
            "--signed--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.Equal("smuggled.pdf", Assert.Single(attachments.Attachments).FileName?.Value);
        Assert.True(attachments.CarriesUnverifiedSignature);
    }

    /// <summary>A CID URL percent-encodes what cannot appear in a URL, so the comparison has to decode it first.</summary>
    [Fact]
    public async Task ReadMetadataAsync_PercentEncodedContentIdReference_MatchesTheEmbeddedResource()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/related; type=\"text/html\"; boundary=\"related\"",
            string.Empty,
            "--related",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            "<html><body><img src=\"cid:logo%2Fdark@example.test\"></body></html>",
            "--related",
            "Content-Type: image/png; name=\"logo.png\"",
            "Content-Id: <logo/dark@example.test>",
            string.Empty,
            "image",
            "--related--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.Empty(attachments.Attachments);
        Assert.Equal(1, attachments.InlineResourceCount);
    }

    /// <summary>A style sheet points at a resource the same way an attribute does, so its <c>url()</c> counts.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ContentIdReferencedByAStyleSheet_ReportsAnEmbeddedResource()
    {
        // Arrange
        var content = CreateRelatedMessageWithHtmlBody(
            "<html><head><style>.header{background:url(cid:logo@example.test)}</style></head><body></body></html>");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.Empty(attachments.Attachments);
        Assert.Equal(1, attachments.InlineResourceCount);
    }

    /// <summary>Text and comments render nothing, so naming a Content-ID there cannot take a file out of the summary.</summary>
    [Theory]
    [InlineData("<html><body>Write to cid:logo@example.test for a copy.</body></html>")]
    [InlineData("<html><body><!-- cid:logo@example.test --></body></html>")]
    [InlineData("<html><body><script>var source = 'cid:logo@example.test';</script></body></html>")]
    public async Task ReadMetadataAsync_ContentIdNamedOutsideAReferenceContext_StillReportsTheAttachment(string htmlBody)
    {
        // Arrange
        var content = CreateRelatedMessageWithHtmlBody(htmlBody);

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.Equal(0, attachments.InlineResourceCount);
        Assert.Equal("logo.png", Assert.Single(attachments.Attachments).FileName?.Value);
    }

    /// <summary>A quoted local part reaches the record with its space intact, because the space is part of the mailbox.</summary>
    [Fact]
    public async Task ReadMetadataAsync_QuotedLocalPart_KeepsTheAddressTheSenderWrote()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: \"John Smith\"@example.test",
            "Content-Type: text/plain",
            string.Empty,
            "Body");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var participant = Assert.Single(AssertExtracted(result).Participants);
        Assert.Equal("\"John Smith\"@example.test", participant.Address.Address);
    }

    /// <summary>An embedded image with no disposition header at all is a resource, because that is how senders write one.</summary>
    [Fact]
    public async Task ReadMetadataAsync_HtmlBodyEmbeddingAnImageWithoutDisposition_ReportsAnInlineResource()
    {
        // Act
        var result = await CreateReader().ReadMetadataAsync(
            CreateRelatedMessageWithEmbeddedImage(imageDispositionHeader: null),
            CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.Empty(attachments.Attachments);
        Assert.Equal(1, attachments.InlineResourceCount);
    }

    /// <summary>An explicit attachment disposition wins, because there the sender has said what the part is.</summary>
    [Fact]
    public async Task ReadMetadataAsync_SameImageDeclaredAnAttachment_ReportsAnAttachment()
    {
        // Act
        var result = await CreateReader().ReadMetadataAsync(
            CreateRelatedMessageWithEmbeddedImage("Content-Disposition: attachment; filename=\"logo.png\""),
            CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.Equal(0, attachments.InlineResourceCount);
        Assert.Equal("logo.png", Assert.Single(attachments.Attachments).FileName?.Value);
    }

    /// <summary>A forwarded message is one attachment, not the attachment count of everything inside it.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ForwardedMessage_ReportsOneAttachmentForTheWholeNestedMessage()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/mixed; boundary=\"outer\"",
            string.Empty,
            "--outer",
            "Content-Type: text/plain",
            string.Empty,
            "Forwarding this.",
            "--outer",
            "Content-Type: message/rfc822",
            "Content-Disposition: attachment; filename=\"forwarded.eml\"",
            string.Empty,
            "From: bob@example.test",
            "Subject: Inner",
            "Content-Type: multipart/mixed; boundary=\"inner\"",
            string.Empty,
            "--inner",
            "Content-Type: text/plain",
            string.Empty,
            "Inner body",
            "--inner",
            "Content-Type: application/pdf; name=\"inner.pdf\"",
            "Content-Disposition: attachment; filename=\"inner.pdf\"",
            string.Empty,
            "inner payload",
            "--inner--",
            "--outer--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.Equal("forwarded.eml", Assert.Single(attachments.Attachments).FileName?.Value);
    }

    /// <summary>An encoded forwarded message is decoded like any other part, so its own formatting is what gets measured.</summary>
    [Fact]
    public async Task ReadMetadataAsync_TransferEncodedForwardedMessage_MeasuresTheOctetsItArrivedAs()
    {
        // Arrange
        var forwardedMessage = Encoding.UTF8.GetBytes("From: bob@example.test\nSubject: Inner\n\nInner body\n");
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/mixed; boundary=\"outer\"",
            string.Empty,
            "--outer",
            "Content-Type: text/plain",
            string.Empty,
            "Forwarding this.",
            "--outer",
            "Content-Type: message/rfc822",
            "Content-Disposition: attachment; filename=\"forwarded.eml\"",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            Convert.ToBase64String(forwardedMessage),
            "--outer--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.Equal(forwardedMessage.Length, Assert.Single(attachments.Attachments).DecodedSizeOctets);
    }

    /// <summary>A TNEF part is recorded as one attachment and marked unexpanded, because expanding it is a separate decision.</summary>
    [Fact]
    public async Task ReadMetadataAsync_TnefMessage_RecordsOneAttachmentAndMarksItUnexpanded()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/mixed; boundary=\"mixed\"",
            string.Empty,
            "--mixed",
            "Content-Type: text/plain",
            string.Empty,
            "Body",
            "--mixed",
            "Content-Type: application/vnd.ms-tnef; name=\"winmail.dat\"",
            "Content-Disposition: attachment; filename=\"winmail.dat\"",
            string.Empty,
            "tnef payload",
            "--mixed--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachments = AssertExtracted(result).Attachments;
        Assert.Equal("winmail.dat", Assert.Single(attachments.Attachments).FileName?.Value);
        Assert.True(attachments.ContainsUnexpandedTnefPart);
    }

    /// <summary>A calendar part is what its placement makes it: a body alternative in one message and a file in the other.</summary>
    [Theory]
    [InlineData("alternative", 0)]
    [InlineData("mixed", 1)]
    public async Task ReadMetadataAsync_CalendarInvitation_IsClassifiedByWhereItSits(
        string multipartSubtype,
        int expectedAttachmentCount)
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            $"Content-Type: multipart/{multipartSubtype}; boundary=\"parts\"",
            string.Empty,
            "--parts",
            "Content-Type: text/plain",
            string.Empty,
            "You are invited.",
            "--parts",
            "Content-Type: text/calendar; method=REQUEST; name=\"invite.ics\"",
            string.Empty,
            "BEGIN:VCALENDAR",
            "END:VCALENDAR",
            "--parts--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        Assert.Equal(expectedAttachmentCount, AssertExtracted(result).Attachments.AttachmentCount);
    }

    /// <summary>A file name arrives decoded from its transport encoding, whichever encoding the sender used.</summary>
    [Theory]
    [InlineData("Content-Disposition: attachment; filename=\"=?utf-8?Q?faktura_wrzesie=C5=84.pdf?=\"")]
    [InlineData("Content-Disposition: attachment;\r\n filename*0*=utf-8''faktura%20;\r\n filename*1*=wrzesie%C5%84.pdf")]
    public async Task ReadMetadataAsync_EncodedFileName_DecodesIt(string dispositionHeader)
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/mixed; boundary=\"mixed\"",
            string.Empty,
            "--mixed",
            "Content-Type: text/plain",
            string.Empty,
            "Body",
            "--mixed",
            "Content-Type: application/pdf",
            dispositionHeader,
            string.Empty,
            "payload",
            "--mixed--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachment = Assert.Single(AssertExtracted(result).Attachments.Attachments);
        Assert.Equal("faktura wrzesień.pdf", attachment.FileName?.Value);
        Assert.False(attachment.FileName?.WasNormalized);
    }

    /// <summary>A file name that carried path structure reaches the record repaired and says so.</summary>
    [Fact]
    public async Task ReadMetadataAsync_FileNameCarryingPathStructure_RecordsTheRepairedName()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/mixed; boundary=\"mixed\"",
            string.Empty,
            "--mixed",
            "Content-Type: text/plain",
            string.Empty,
            "Body",
            "--mixed",
            "Content-Type: application/pdf",
            "Content-Disposition: attachment; filename=\"../../etc/passwd\"",
            string.Empty,
            "payload",
            "--mixed--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var attachment = Assert.Single(AssertExtracted(result).Attachments.Attachments);
        Assert.Equal("passwd", attachment.FileName?.Value);
        Assert.True(attachment.FileName?.WasNormalized);
    }

    /// <summary>An attachment with no usable name is unnamed rather than given one nobody wrote.</summary>
    [Fact]
    public async Task ReadMetadataAsync_AttachmentWithoutAName_RecordsItUnnamed()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/mixed; boundary=\"mixed\"",
            string.Empty,
            "--mixed",
            "Content-Type: text/plain",
            string.Empty,
            "Body",
            "--mixed",
            "Content-Type: application/octet-stream",
            "Content-Disposition: attachment",
            string.Empty,
            "payload",
            "--mixed--");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        Assert.Null(Assert.Single(AssertExtracted(result).Attachments.Attachments).FileName);
    }

    /// <summary>A message declaring more parts than the limit is abandoned before an object tree is ever built.</summary>
    [Fact]
    public async Task ReadMetadataAsync_MessageOverThePartCountLimit_FailsWithoutConstructingTheTree()
    {
        // Arrange
        var treeWasConstructed = false;
        var reader = new MimeKitEmailMimeReader(
            new EmailMimeExtractionOptions { MaxPartCount = 3, MaxNestingDepth = 30 },
            TrustedAuthorities(identifier: null),
            (rawMime, cancellationToken) =>
            {
                treeWasConstructed = true;

                return MimeKit.MimeMessage.LoadAsync(rawMime, cancellationToken);
            });
        var content = MimeFixtures.Message(
            [
                "From: anna@example.test",
                "Content-Type: multipart/mixed; boundary=\"mixed\"",
                string.Empty,
                .. Enumerable.Range(0, 10).SelectMany(index => new[]
                {
                    "--mixed",
                    "Content-Type: text/plain",
                    string.Empty,
                    $"Part {index}",
                }),
                "--mixed--",
            ]);

        // Act
        var result = await reader.ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        Assert.Equal(EmailMimeExtractionOutcome.PartCountLimitExceeded, result.Outcome);
        Assert.Null(result.Metadata);
        Assert.False(treeWasConstructed);
    }

    /// <summary>A message nesting deeper than the limit is abandoned the same way, and says which limit it crossed.</summary>
    [Fact]
    public async Task ReadMetadataAsync_MessageOverTheNestingDepthLimit_FailsWithoutConstructingTheTree()
    {
        // Arrange
        var treeWasConstructed = false;
        var reader = new MimeKitEmailMimeReader(
            new EmailMimeExtractionOptions { MaxPartCount = 1000, MaxNestingDepth = 2 },
            TrustedAuthorities(identifier: null),
            (rawMime, cancellationToken) =>
            {
                treeWasConstructed = true;

                return MimeKit.MimeMessage.LoadAsync(rawMime, cancellationToken);
            });

        // Act
        var result = await reader.ReadMetadataAsync(CreateDeeplyNestedMessage(nestingDepth: 4), CancellationToken.None);

        // Assert
        Assert.Equal(EmailMimeExtractionOutcome.NestingDepthLimitExceeded, result.Outcome);
        Assert.Null(result.Metadata);
        Assert.False(treeWasConstructed);
    }

    /// <summary>A message within both limits is read normally, so the bound refuses only what it is meant to.</summary>
    [Fact]
    public async Task ReadMetadataAsync_MessageWithinTheStructuralLimits_IsRead()
    {
        // Arrange
        var reader = new MimeKitEmailMimeReader(
            new EmailMimeExtractionOptions { MaxPartCount = 10, MaxNestingDepth = 4 },
            TrustedAuthorities(identifier: null));

        // Act
        var result = await reader.ReadMetadataAsync(CreateDeeplyNestedMessage(nestingDepth: 3), CancellationToken.None);

        // Assert
        Assert.Equal(EmailMimeExtractionOutcome.Extracted, result.Outcome);
    }

    /// <summary>Badly formed mail produces a failure result rather than an exception, so one message never stops a batch.</summary>
    [Theory]
    [InlineData(new byte[0])]
    [InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03 })]
    public async Task ReadMetadataAsync_ContentThatIsNotAMessage_ReportsAFailureWithoutThrowing(byte[] rawMime)
    {
        // Act
        var result = await CreateReader().ReadMetadataAsync(MimeFixtures.RawContent(rawMime), CancellationToken.None);

        // Assert
        Assert.NotEqual(EmailMimeExtractionOutcome.Extracted, result.Outcome);
        Assert.Null(result.Metadata);
    }

    /// <summary>A truncated body is ordinary damaged mail: whatever the parser makes of it, nothing may escape as an exception.</summary>
    [Fact]
    public async Task ReadMetadataAsync_TruncatedMultipart_ReturnsAResultWithoutThrowing()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/mixed; boundary=\"mixed\"",
            string.Empty,
            "--mixed",
            "Content-Type: application/pdf; name=\"invoice.pdf\"",
            "Content-Disposition: attachment; filename=\"invoice.pdf\"",
            "Content-Transfer-Encoding: base64",
            string.Empty,
            "SGVsbG8sIHdvcmxk");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
    }

    /// <summary>The occurrence identity travels with the metadata, because that is what a later write joins it to.</summary>
    [Fact]
    public async Task ReadMetadataAsync_AnyMessage_CarriesTheOccurrenceItWasFetchedUnder()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: text/plain",
            string.Empty,
            "Body");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        Assert.Equal(MimeFixtures.OccurrenceId, AssertExtracted(result).OccurrenceId);
    }

    /// <summary>The header the trusted server wrote is the one that decides, and the forged one below it is ignored.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ForgedAuthenticationResultsBelowTheTrustedOne_ReadsTheTrustedOne()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "Authentication-Results: mx.example.test; dkim=fail header.d=bank.test",
            "Authentication-Results: attacker.test; dkim=pass header.d=bank.test",
            "From: alerts@bank.test",
            "Content-Type: text/plain",
            string.Empty,
            "Body");

        // Act
        var result = await CreateReader("mx.example.test").ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var authentication = AssertExtracted(result).SenderAuthentication;
        Assert.Equal(SenderAuthenticationOutcome.Failed, authentication.Outcome);
        Assert.Null(authentication.AuthenticatedDomain);
    }

    /// <summary>What a real provider writes is read whole: an identity, the envelope domain, and the DMARC result.</summary>
    [Fact]
    public async Task ReadMetadataAsync_TrustedHeaderStatingEveryMethod_RecordsTheWholeVerdict()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "Authentication-Results: mx.example.test; spf=pass smtp.mailfrom=bounce@bank.test; "
            + "dkim=pass header.d=bank.test; dmarc=pass header.from=bank.test",
            "From: Bank <alerts@bank.test>",
            "Content-Type: text/plain",
            string.Empty,
            "Body");

        // Act
        var result = await CreateReader("mx.example.test").ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var authentication = AssertExtracted(result).SenderAuthentication;
        Assert.Equal(SenderAuthenticationOutcome.Authenticated, authentication.Outcome);
        Assert.Equal(SenderAuthenticationMethod.DomainKeysIdentifiedMail, authentication.AuthenticatedBy);
        Assert.Equal("BANK.TEST", authentication.AuthenticatedDomain?.NormalizedValue);
        Assert.Equal("BANK.TEST", authentication.SpfDomain?.NormalizedValue);
        Assert.Equal(DmarcOutcome.Pass, authentication.Dmarc);
        Assert.Equal(SenderDomainAlignment.Aligned, authentication.Alignment);
    }

    /// <summary>An account naming no trusted server reads no header, which is what an unconfigured deployment gets.</summary>
    [Fact]
    public async Task ReadMetadataAsync_AccountTrustsNoServer_LeavesTheVerdictNotEstablished()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "Authentication-Results: mx.example.test; dkim=pass header.d=bank.test",
            "From: alerts@bank.test",
            "Content-Type: text/plain",
            string.Empty,
            "Body");

        // Act
        var result = await CreateReader().ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var authentication = AssertExtracted(result).SenderAuthentication;
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, authentication.Outcome);
        Assert.Equal("BANK.TEST", authentication.FromDomain?.NormalizedValue);
    }

    /// <summary>The ARC form preserves an upstream hop's claim rather than what this mailbox's server saw, so it decides nothing.</summary>
    [Fact]
    public async Task ReadMetadataAsync_ArcAuthenticationResultsOnly_LeavesTheVerdictNotEstablished()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "ARC-Authentication-Results: i=1; mx.example.test; dkim=pass header.d=bank.test",
            "From: alerts@bank.test",
            "Content-Type: text/plain",
            string.Empty,
            "Body");

        // Act
        var result = await CreateReader("mx.example.test").ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        Assert.Equal(
            SenderAuthenticationOutcome.NotEstablished,
            AssertExtracted(result).SenderAuthentication.Outcome);
    }

    /// <summary>A header of pathological length is passed over unread rather than parsed, and the message is still extracted.</summary>
    [Fact]
    public async Task ReadMetadataAsync_AuthenticationResultsPastTheLengthBound_IsNotRead()
    {
        // Arrange
        var padding = string.Join(' ', Enumerable.Repeat("dkim=none", 600));
        var content = MimeFixtures.Message(
            $"Authentication-Results: mx.example.test; dkim=pass header.d=bank.test; {padding}",
            "From: alerts@bank.test",
            "Content-Type: text/plain",
            string.Empty,
            "Body");

        // Act
        var result = await CreateReader("mx.example.test").ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var metadata = AssertExtracted(result);
        Assert.True(padding.Length > AuthenticationResultsHeader.MaximumHeaderValueLength);
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, metadata.SenderAuthentication.Outcome);
    }

    /// <summary>A header nothing can parse contributes nothing, even when it claims the trusted server's own identifier.</summary>
    /// <remarks>
    /// The forgery is written above a well-formed header of the same identifier, so the two outcomes are distinguishable:
    /// a malformed header that had been parsed would be the topmost trusted one and would establish nothing.
    /// </remarks>
    [Fact]
    public async Task ReadMetadataAsync_MalformedAuthenticationResults_FallsThroughToTheHeaderThatParses()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "Authentication-Results: mx.example.test; dkim=pass header.d=",
            "Authentication-Results: mx.example.test; dkim=pass header.d=bank.test",
            "From: alerts@bank.test",
            "Content-Type: text/plain",
            string.Empty,
            "Body");

        // Act
        var result = await CreateReader("mx.example.test").ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        Assert.Equal(
            SenderAuthenticationOutcome.Authenticated,
            AssertExtracted(result).SenderAuthentication.Outcome);
    }

    /// <summary>A message carrying no such header at all is the ordinary case, and it establishes nothing.</summary>
    [Fact]
    public async Task ReadMetadataAsync_MessageWithoutAuthenticationResults_LeavesTheVerdictNotEstablished()
    {
        // Arrange
        var content = MimeFixtures.Message(
            "From: alerts@bank.test",
            "Content-Type: text/plain",
            string.Empty,
            "Body");

        // Act
        var result = await CreateReader("mx.example.test").ReadMetadataAsync(content, CancellationToken.None);

        // Assert
        var authentication = AssertExtracted(result).SenderAuthentication;
        Assert.Equal(SenderAuthenticationOutcome.NotEstablished, authentication.Outcome);
        Assert.Equal(SenderDomainAlignment.NotAssessed, authentication.Alignment);
    }

    private static MimeKitEmailMimeReader CreateReader(string? trustedAuthorityIdentifier = null) =>
        new(new EmailMimeExtractionOptions(), TrustedAuthorities(trustedAuthorityIdentifier));

    /// <summary>Answers every account with the one authority a test configured, or with none.</summary>
    private static ITrustedAuthenticationAuthorityReader TrustedAuthorities(string? identifier)
    {
        TrustedAuthenticationAuthority.TryCreate(identifier, out var authority);

        var authorities = Substitute.For<ITrustedAuthenticationAuthorityReader>();
        authorities.GetTrustedAuthority(Arg.Any<MailAccountId>()).Returns(authority);

        return authorities;
    }

    private static ExtractedEmailMetadata AssertExtracted(EmailMimeExtractionResult result)
    {
        Assert.Equal(EmailMimeExtractionOutcome.Extracted, result.Outcome);

        return Assert.IsType<ExtractedEmailMetadata>(result.Metadata);
    }

    private static Application.EmailContent.Storage.RemoteEmailContent CreateRelatedMessageWithEmbeddedImage(string? imageDispositionHeader) =>
        MimeFixtures.Message(
            [
                "From: anna@example.test",
                "Content-Type: multipart/related; type=\"text/html\"; boundary=\"related\"",
                string.Empty,
                "--related",
                "Content-Type: text/html; charset=utf-8",
                string.Empty,
                "<html><body><img src=\"cid:logo@example.test\"></body></html>",
                "--related",
                "Content-Type: image/png; name=\"logo.png\"",
                "Content-Id: <logo@example.test>",
                .. imageDispositionHeader is null ? Array.Empty<string>() : [imageDispositionHeader],
                "Content-Transfer-Encoding: base64",
                string.Empty,
                "SGVsbG8sIHdvcmxkIQ==",
                "--related--",
            ]);

    private static Application.EmailContent.Storage.RemoteEmailContent CreateRelatedMessageWithHtmlBody(string htmlBody) =>
        MimeFixtures.Message(
            "From: anna@example.test",
            "Content-Type: multipart/related; type=\"text/html\"; boundary=\"related\"",
            string.Empty,
            "--related",
            "Content-Type: text/html; charset=utf-8",
            string.Empty,
            htmlBody,
            "--related",
            "Content-Type: image/png; name=\"logo.png\"",
            "Content-Id: <logo@example.test>",
            string.Empty,
            "image",
            "--related--");

    private static Application.EmailContent.Storage.RemoteEmailContent CreateDeeplyNestedMessage(int nestingDepth) =>
        MimeFixtures.Message(
            [
                "From: anna@example.test",
                $"Content-Type: multipart/mixed; boundary=\"level0\"",
                string.Empty,
                .. Enumerable.Range(0, nestingDepth - 1).SelectMany(level => new[]
                {
                    $"--level{level}",
                    $"Content-Type: multipart/mixed; boundary=\"level{level + 1}\"",
                    string.Empty,
                }),
                $"--level{nestingDepth - 1}",
                "Content-Type: text/plain",
                string.Empty,
                "Body",
                .. Enumerable.Range(0, nestingDepth).Reverse().Select(level => $"--level{level}--"),
            ]);
}
