// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Mail.Mime;
using MimeKit;
using MimeKit.Text;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Mail.Mime;

/// <summary>Covers reading a composed outgoing message back into the words a screen is asked about.</summary>
public sealed class MimeKitOutgoingMailTextReaderTests
{
    private readonly MimeKitOutgoingMailTextReader reader = new();

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

    /// <summary>An attachment is not read, so a message carrying one costs the same to screen as one carrying none.</summary>
    [Fact]
    public async Task ReadAsync_AMessageCarryingAnAttachment_ReadsTheBodyAndNotTheAttachment()
    {
        // Arrange
        var body = new TextPart(TextFormat.Plain) { Text = "the plain text" };
        var attachment = new MimePart("application", "octet-stream")
        {
            Content = new MimeContent(new MemoryStream("AKIAEXAMPLEKEY"u8.ToArray())),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            FileName = "keys.txt",
        };

        using var message = new MimeMessage { Subject = "Quarterly figures" };
        message.From.Add(new MailboxAddress("Anna", "anna@example.test"));
        message.To.Add(new MailboxAddress("Bruno", "bruno@example.test"));
        message.Body = new Multipart("mixed") { body, attachment };

        // Act
        var text = await this.reader.ReadAsync(Serialize(message), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, text.ScreenedValues.Count);
        Assert.Equal("Quarterly figures", text.Subject);
        Assert.DoesNotContain("AKIAEXAMPLEKEY", text.PlainTextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_NoMimeAtAll_Refuses()
    {
        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => this.reader.ReadAsync(ReadOnlyMemory<byte>.Empty, TestContext.Current.CancellationToken));
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
}
