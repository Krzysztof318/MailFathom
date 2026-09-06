// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>Covers opening one attachment of a stored message by the position a download link names.</summary>
/// <remarks>
/// The claim that matters most is not that a file comes back but that the <em>right</em> file does: the position a link
/// carries was produced by the read's walk, so an adapter that ordered or filtered its parts differently would hand a
/// caller somebody else's attachment rather than fail. Several tests here compare the two walks directly for that
/// reason.
/// </remarks>
public sealed class MimeKitEmailAttachmentContentReaderTests
{
    /// <summary>The octets written out are the decoded ones, which is the whole point of streaming the part rather than the message.</summary>
    [Fact]
    public async Task OpenAsync_AttachmentUnderATransferEncoding_WritesTheDecodedOctetsAndNothingElse()
    {
        // Arrange
        var content = MessageAttaching(("report.pdf", "application/pdf", "%PDF-1.7 report"));

        // Act
        await using var attachment = await OpenAsync(content, attachmentPosition: 0);
        using var written = new MemoryStream();
        await attachment.WriteContentToAsync(written, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("%PDF-1.7 report"u8.ToArray(), written.ToArray());
        Assert.Equal("report.pdf", attachment.Description.FileName?.Value);
        Assert.Equal("application/pdf", attachment.Description.MediaType);
        Assert.Equal("%PDF-1.7 report".Length, attachment.Description.DecodedSizeOctets);
    }

    /// <summary>
    /// A screened deployment reads the file before it streams it, so the octets are written out twice from one opened
    /// part. A part that answered the second caller with nothing would serve an empty file to whoever asked for it, and
    /// nothing above this type could tell that from an attachment that really is empty.
    /// </summary>
    [Fact]
    public async Task OpenAsync_AttachmentWrittenOutTwice_WritesTheSameOctetsBothTimes()
    {
        // Arrange
        var content = MessageAttaching(("report.pdf", "application/pdf", "%PDF-1.7 report"));

        // Act
        await using var attachment = await OpenAsync(content, attachmentPosition: 0);
        using var screened = new MemoryStream();
        await attachment.WriteContentToAsync(screened, TestContext.Current.CancellationToken);
        using var served = new MemoryStream();
        await attachment.WriteContentToAsync(served, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("%PDF-1.7 report"u8.ToArray(), screened.ToArray());
        Assert.Equal(screened.ToArray(), served.ToArray());
    }

    /// <summary>
    /// The position a link names is a position in the read's own walk, so the two must agree part for part. Comparing
    /// them here is what stops a divergence from being discovered as a caller receiving the wrong file.
    /// </summary>
    [Fact]
    public async Task OpenAsync_EveryPositionOfAMessage_OpensThePartTheReadDescribedAtThatPosition()
    {
        // Arrange
        var content = MessageAttaching(
            ("first.txt", "text/plain", "first"),
            ("second.txt", "text/plain", "second"),
            ("third.txt", "text/plain", "third"));

        var rendering = await new MimeKitEmailContentRenderer(StructuralLimits).RenderAsync(
            content,
            new EmailContentRenderingBounds(IncludeSanitizedHtml: false, 100_000, int.MaxValue),
            TestContext.Current.CancellationToken);
        var described = rendering.Rendering!.Attachments;

        // Act
        var opened = new List<ExtractedEmailAttachment>();
        foreach (var position in Enumerable.Range(0, described.Count))
        {
            await using var attachment = await OpenAsync(content, position);
            opened.Add(attachment.Description);
        }

        // Assert
        Assert.Equal(3, described.Count);
        Assert.Equal(described, opened);
    }

    /// <summary>
    /// An embedded resource is not an attachment in the read's walk, so it must not shift the positions either: a link
    /// naming position 0 has to reach the file the read called position 0 rather than the image beside it.
    /// </summary>
    [Fact]
    public async Task OpenAsync_MessageEmbeddingAnImageBesideAFile_SkipsTheResourceExactlyAsTheReadDoes()
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
            "--rel",
            "Content-Type: application/pdf",
            "Content-Disposition: attachment; filename=\"report.pdf\"",
            string.Empty,
            "report",
            "--rel--");

        // Act
        await using var attachment = await OpenAsync(content, attachmentPosition: 0);

        // Assert
        Assert.Equal("report.pdf", attachment.Description.FileName?.Value);
    }

    /// <summary>
    /// A position the message does not carry is refused rather than clamped. A link outliving the parts it described —
    /// because the stored copy was replaced by a repair — must reach nothing rather than reach whatever is there now.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(-1)]
    public async Task OpenAsync_PositionTheMessageDoesNotCarry_OpensNothingAndIsNotAnUnreadableCopy(int attachmentPosition)
    {
        // Arrange
        var content = MessageAttaching(("only.txt", "text/plain", "only"));

        // Act
        var result = await new MimeKitEmailAttachmentContentReader(StructuralLimits).OpenAsync(
            content,
            attachmentPosition,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Attachment);
        Assert.False(result.ContentIsUnreadable);
    }

    /// <summary>
    /// Bytes that no longer parse are the damaged local copy the caller records a repair request for, which is a
    /// different finding from a position the message does not have.
    /// </summary>
    [Fact]
    public async Task OpenAsync_StoredBytesThatNoLongerParse_ReportsTheCopyAsUnreadable()
    {
        // Arrange — a header line that never terminates leaves nothing a parse can build a message from.
        var content = MimeFixtures.StoredRawContent(Encoding.UTF8.GetBytes(new string('\0', 64)));

        // Act
        var result = await new MimeKitEmailAttachmentContentReader(StructuralLimits).OpenAsync(
            content,
            attachmentPosition: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Attachment);
    }

    /// <summary>
    /// A message beyond the structural limits is refused here exactly as it is by a read, so the two doors into one
    /// mailbox cannot disagree about which messages this deployment will parse at all.
    /// </summary>
    [Fact]
    public async Task OpenAsync_MessageBeyondTheStructuralLimits_ReportsTheCopyAsUnreadableWithoutParsingIt()
    {
        // Arrange
        var content = MessageAttaching(
            ("first.txt", "text/plain", "first"),
            ("second.txt", "text/plain", "second"));
        var narrowLimits = new EmailMimeExtractionOptions
        {
            MaxPartCount = 1,
            MaxNestingDepth = 10,
            MaxExtractedTextCharacters = 10_000,
        };

        // Act
        var result = await new MimeKitEmailAttachmentContentReader(narrowLimits).OpenAsync(
            content,
            attachmentPosition: 0,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Attachment);
        Assert.True(result.ContentIsUnreadable);
    }

    /// <summary>
    /// The walk parses once and hands out views, so disposing one attachment must leave every later position readable.
    /// </summary>
    /// <remarks>
    /// This is the one ownership in the tree that is inverted — the walk releases the parse and a walked attachment
    /// releases nothing — and the deriver disposes each attachment inside its loop before opening the next position.
    /// A view that released the shared parse would make every position after the first unreadable, which reaches a
    /// mailbox as a spurious repair request and an abandoned message rather than as a failing test.
    /// </remarks>
    [Fact]
    public async Task OpenWalkAsync_APositionOpenedAfterAnEarlierOneWasDisposed_StillReadsItsOwnOctets()
    {
        // Arrange
        var content = MessageAttaching(
            ("first.txt", "text/plain", "first"),
            ("second.txt", "text/plain", "second"));

        // Act
        var walkResult = await new MimeKitEmailAttachmentContentReader(StructuralLimits).OpenWalkAsync(
            content,
            TestContext.Current.CancellationToken);

        await using var walk = walkResult.Walk!;

        await using (var first = (await walk.OpenAsync(0, TestContext.Current.CancellationToken)).Attachment!)
        {
            Assert.Equal("first.txt", first.Description.FileName?.Value);
        }

        await using var second = (await walk.OpenAsync(1, TestContext.Current.CancellationToken)).Attachment!;
        using var written = new MemoryStream();
        await second.WriteContentToAsync(written, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, walk.Count);
        Assert.Equal("second.txt", second.Description.FileName?.Value);
        Assert.Equal("second"u8.ToArray(), written.ToArray());
    }

    /// <summary>A position the message does not have is the walk disagreeing with whatever asked for it.</summary>
    [Fact]
    public async Task OpenWalkAsync_APositionPastTheCountTheWalkReports_AnswersThatThereIsNoSuchAttachment()
    {
        // Arrange
        var content = MessageAttaching(("only.txt", "text/plain", "only"));

        // Act
        var walkResult = await new MimeKitEmailAttachmentContentReader(StructuralLimits).OpenWalkAsync(
            content,
            TestContext.Current.CancellationToken);

        await using var walk = walkResult.Walk!;
        var opened = await walk.OpenAsync(1, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, walk.Count);
        Assert.Null(opened.Attachment);
        Assert.False(opened.ContentIsUnreadable);
    }

    /// <summary>
    /// A message beyond the structural limits is refused on this door as well, before anything parses it, so the two
    /// doors into one mailbox cannot disagree about which messages this deployment will parse at all.
    /// </summary>
    /// <remarks>
    /// Every message of the attachment pass goes through the walk rather than through the single-position read, so a
    /// structural pass lost here would parse stranger-composed MIME past the configured part and nesting limits on
    /// every message of every account run, with nothing reporting that it had.
    /// </remarks>
    [Fact]
    public async Task OpenWalkAsync_MessageBeyondTheStructuralLimits_ReportsTheCopyAsUnreadableWithoutParsingIt()
    {
        // Arrange
        var content = MessageAttaching(
            ("first.txt", "text/plain", "first"),
            ("second.txt", "text/plain", "second"));
        var narrowLimits = new EmailMimeExtractionOptions
        {
            MaxPartCount = 1,
            MaxNestingDepth = 10,
            MaxExtractedTextCharacters = 10_000,
        };

        // Act
        var walkResult = await new MimeKitEmailAttachmentContentReader(narrowLimits).OpenWalkAsync(
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(walkResult.Walk);
        Assert.True(walkResult.ContentIsUnreadable);
    }

    /// <summary>
    /// Bytes that no longer parse are reported as a damaged local copy, which is the repair request the deriver
    /// branches on rather than an exception leaving the pass.
    /// </summary>
    [Fact]
    public async Task OpenWalkAsync_StoredBytesThatNoLongerParse_ReportsTheCopyAsUnreadable()
    {
        // Arrange — a header line that never terminates leaves nothing a parse can build a message from.
        var content = MimeFixtures.StoredRawContent(Encoding.UTF8.GetBytes(new string('\0', 64)));

        // Act
        var walkResult = await new MimeKitEmailAttachmentContentReader(StructuralLimits).OpenWalkAsync(
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(walkResult.Walk);
        Assert.True(walkResult.ContentIsUnreadable);
    }

    private static EmailMimeExtractionOptions StructuralLimits => new()
    {
        MaxPartCount = 100,
        MaxNestingDepth = 10,
        MaxExtractedTextCharacters = 10_000,
    };

    private static async Task<IOpenedEmailAttachment> OpenAsync(StoredEmailContent content, int attachmentPosition)
    {
        var result = await new MimeKitEmailAttachmentContentReader(StructuralLimits).OpenAsync(
            content,
            attachmentPosition,
            TestContext.Current.CancellationToken);

        return result.Attachment ?? throw new InvalidOperationException("The attachment was not opened.");
    }

    private static StoredEmailContent MessageAttaching(params (string FileName, string MediaType, string Content)[] files) =>
        MimeFixtures.StoredMessage(
        [
            "From: sender@example.test",
            "Content-Type: multipart/mixed; boundary=\"mix\"",
            string.Empty,
            "--mix",
            "Content-Type: text/plain; charset=utf-8",
            string.Empty,
            "See the attachments.",
            .. files.SelectMany(file => new[]
            {
                "--mix",
                $"Content-Type: {file.MediaType}",
                $"Content-Disposition: attachment; filename=\"{file.FileName}\"",
                "Content-Transfer-Encoding: base64",
                string.Empty,
                Convert.ToBase64String(Encoding.UTF8.GetBytes(file.Content)),
            }),
            "--mix--",
        ]);
}
