// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text;
using MimeKit;

namespace MailFathom.SyntheticMail.Generation;

/// <summary>Turns one generated description into the MIME a mail server actually receives.</summary>
/// <remarks>
/// Separate from the generator so that what a corpus <em>is</em> can be produced, compared, and printed without MIME
/// being involved, and so a message is only ever materialized immediately before it is delivered.
/// </remarks>
internal static class SyntheticMimeComposer
{
    /// <summary>Composes one message.</summary>
    /// <param name="email">The generated description.</param>
    /// <param name="recipient">The real address the batch is being delivered to.</param>
    /// <param name="sendingAccount">The account the run authenticates as.</param>
    /// <param name="authorIdentity">Whose address the <c>From</c> header carries.</param>
    /// <returns>The message, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Every stream and part built here is owned by the returned message, which the caller disposes.")]
    internal static MimeMessage Compose(
        SyntheticEmail email,
        MailboxAddress recipient,
        MailboxAddress sendingAccount,
        SyntheticAuthorIdentity authorIdentity)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(sendingAccount);

        var author = new MailboxAddress(email.Author.DisplayName, email.Author.Address);
        var message = ComposeUnaddressed(email);

        if (authorIdentity == SyntheticAuthorIdentity.Fabricated)
        {
            message.From.Add(author);

            // RFC 5322 names the account that actually submitted a message whose author is somebody else. A submission
            // server that checks an identity at all checks this one and the envelope sender, both of which are the
            // authenticated account here.
            message.Sender = sendingAccount;
        }
        else
        {
            message.From.Add(sendingAccount);
            message.ReplyTo.Add(author);
        }

        message.To.Add(recipient);

        foreach (var carbonCopy in email.CarbonCopies)
        {
            message.Cc.Add(new MailboxAddress(carbonCopy.DisplayName, carbonCopy.Address));
        }

        return message;
    }

    /// <summary>Composes one message the watched mailbox itself wrote, which is filed rather than submitted.</summary>
    /// <param name="email">The generated description.</param>
    /// <param name="mailbox">The address of the mailbox MailFathom synchronizes, which authors this message.</param>
    /// <param name="recipient">The address it is written to, which is the other side of the exchange.</param>
    /// <returns>The message, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Neither <c>Sender</c> nor <c>Reply-To</c> is written here, and both are written by <see cref="Compose" />:
    /// those exist because a submitted message's author and its authenticated submitter disagree, and this message is
    /// appended by the account that wrote it, so they do not.
    /// </remarks>
    internal static MimeMessage ComposeFromMailbox(
        SyntheticEmail email,
        MailboxAddress mailbox,
        MailboxAddress recipient)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(mailbox);
        ArgumentNullException.ThrowIfNull(recipient);

        var message = ComposeUnaddressed(email);

        message.From.Add(mailbox);
        message.To.Add(recipient);

        return message;
    }

    /// <summary>Builds everything a message carries that does not depend on who is submitting it.</summary>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Every stream and part built here is owned by the returned message, which the caller disposes.")]
    private static MimeMessage ComposeUnaddressed(SyntheticEmail email)
    {
        var message = new MimeMessage
        {
            Subject = email.Subject,
            Date = email.SentAt,
            MessageId = email.MessageId,
            InReplyTo = email.InReplyTo,
            Body = BuildBody(email),
        };

        foreach (var reference in email.References)
        {
            message.References.Add(reference);
        }

        return message;
    }

    private static MimeEntity BuildBody(SyntheticEmail email)
    {
        var body = BuildTextBody(email.Body);

        if (email.Attachment is not { } attachment)
        {
            return body;
        }

        return new Multipart("mixed") { body, BuildAttachmentPart(attachment) };
    }

    private static MimeEntity BuildTextBody(SyntheticEmailBody body)
    {
        var encoding = body.ResolveEncoding();

        return body.Shape switch
        {
            SyntheticBodyShape.PlainTextOnly => BuildTextPart("plain", body.PlainText, encoding),
            SyntheticBodyShape.HtmlOnly => BuildTextPart("html", body.Html, encoding),
            _ => new MultipartAlternative
            {
                BuildTextPart("plain", body.PlainText, encoding),
                BuildTextPart("html", body.Html, encoding),
            },
        };
    }

    private static TextPart BuildTextPart(string subtype, string text, Encoding encoding)
    {
        var part = new TextPart(subtype);

        // SetText rather than the Text property, because the property encodes as UTF-8 and the charset is one of the
        // axes this corpus varies deliberately.
        part.SetText(encoding, text);

        return part;
    }

    private static MimePart BuildAttachmentPart(SyntheticEmailAttachment attachment)
    {
        var content = new MemoryStream(attachment.Length);

        content.Write(attachment.MaterializeContent().Span);
        content.Position = 0;

        return new MimePart(attachment.MediaType, attachment.MediaSubtype)
        {
            Content = new MimeContent(content),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = attachment.FileName,
        };
    }
}
