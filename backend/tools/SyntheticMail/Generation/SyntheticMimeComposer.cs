// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text;
using MimeKit;

namespace MailFathom.SyntheticMail.Generation;

/// <summary>Turns one generated description into the MIME a mail server actually receives.</summary>
/// <remarks>
/// <para>
/// Separate from the generator so that what a corpus <em>is</em> can be produced, compared, and printed without MIME
/// being involved, and so a message is only ever materialized immediately before it is delivered.
/// </para>
/// <para>
/// Composing and addressing are two steps rather than one, because a message can outlive the run that composed it. An
/// exported corpus is composed once and delivered later, to a mailbox and under an account neither of which existed
/// when it was written, so what the run supplies at delivery is exactly the addressing — and a message a generator
/// produced and one a corpus was read from reach delivery in the same shape.
/// </para>
/// </remarks>
internal static class SyntheticMimeComposer
{
    /// <summary>Composes one message as the person who wrote it, addressed to nobody yet.</summary>
    /// <param name="email">The generated description.</param>
    /// <returns>The message, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="email" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The author and the carbon copies are the corpus's own and travel with it; who the message is written to depends
    /// on the run delivering it, which is what the two addressing methods below supply.
    /// </remarks>
    internal static MimeMessage ComposeAuthored(SyntheticEmail email)
    {
        ArgumentNullException.ThrowIfNull(email);

        var message = ComposeUnaddressed(email);

        message.From.Add(new MailboxAddress(email.Author.DisplayName, email.Author.Address));

        foreach (var carbonCopy in email.CarbonCopies)
        {
            message.Cc.Add(new MailboxAddress(carbonCopy.DisplayName, carbonCopy.Address));
        }

        return message;
    }

    /// <summary>Composes one message and addresses it for submission by the account this run authenticates as.</summary>
    /// <param name="email">The generated description.</param>
    /// <param name="recipient">The real address the batch is being delivered to.</param>
    /// <param name="sendingAccount">The account the run authenticates as.</param>
    /// <param name="authorIdentity">Whose address the <c>From</c> header carries.</param>
    /// <returns>The message, which the caller disposes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal static MimeMessage Compose(
        SyntheticEmail email,
        MailboxAddress recipient,
        MailboxAddress sendingAccount,
        SyntheticAuthorIdentity authorIdentity)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(sendingAccount);

        var message = ComposeAuthored(email);

        AddressAsSubmission(
            message,
            new MailboxAddress(email.Author.DisplayName, email.Author.Address),
            recipient,
            sendingAccount,
            authorIdentity);

        return message;
    }

    /// <summary>Addresses a composed message as one the sending account submits on an invented author's behalf.</summary>
    /// <param name="message">The composed message, which the caller still owns.</param>
    /// <param name="author">The invented participant who wrote it.</param>
    /// <param name="recipient">The one real address it is delivered to.</param>
    /// <param name="sendingAccount">The account the run authenticates as.</param>
    /// <param name="authorIdentity">Whose address the <c>From</c> header carries.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// RFC 5322 names the account that actually submitted a message whose author is somebody else, so a fabricated
    /// author leaves the authenticated account in <c>Sender</c>. A submission server that checks an identity at all
    /// checks that one and the envelope sender, both of which are the authenticated account here. The headers are
    /// cleared before they are written, because a message read back from an exported corpus already carries the
    /// addressing of the run that composed it.
    /// </remarks>
    internal static void AddressAsSubmission(
        MimeMessage message,
        MailboxAddress author,
        MailboxAddress recipient,
        MailboxAddress sendingAccount,
        SyntheticAuthorIdentity authorIdentity)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(recipient);
        ArgumentNullException.ThrowIfNull(sendingAccount);

        message.From.Clear();
        message.ReplyTo.Clear();

        if (authorIdentity == SyntheticAuthorIdentity.Fabricated)
        {
            message.From.Add(author);
            message.Sender = sendingAccount;
        }
        else
        {
            message.From.Add(sendingAccount);
            message.ReplyTo.Add(author);
            message.Sender = null;
        }

        message.To.Clear();
        message.To.Add(recipient);
    }

    /// <summary>Addresses a composed message as one person's letter to another.</summary>
    /// <param name="message">The composed message, which the caller still owns.</param>
    /// <param name="author">Who the message is from.</param>
    /// <param name="writtenTo">Who it is written to.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Neither <c>Sender</c> nor <c>Reply-To</c> is written here, and both are written by
    /// <see cref="AddressAsSubmission" />: those exist because a submitted message's author and its authenticated
    /// submitter disagree, and a message filed by the account that wrote it has no such disagreement. This is what a
    /// turn an exchange's own mailbox appends to its Sent folder carries, and what an exported corpus is written with
    /// on both sides.
    /// </remarks>
    internal static void AddressAsCorrespondence(MimeMessage message, MailboxAddress author, MailboxAddress writtenTo)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(writtenTo);

        message.From.Clear();
        message.ReplyTo.Clear();
        message.Sender = null;

        message.From.Add(author);

        message.To.Clear();
        message.To.Add(writtenTo);
    }

    /// <summary>Writes the ancestry a message answers, replacing whatever it was composed with.</summary>
    /// <param name="message">The composed message, which the caller still owns.</param>
    /// <param name="ancestry">Every identifier the mailbox has assigned in this exchange, oldest first, and empty for a message that opens one.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// Replaced rather than composed, because the identifiers a delivered reply answers come from the mailbox rather
    /// than from the seed: a corpus carries the ancestry it was generated with so that it reads as threads wherever it
    /// is opened, and delivery rewrites it from what the server actually assigned.
    /// </remarks>
    internal static void Thread(MimeMessage message, IReadOnlyList<string> ancestry)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(ancestry);

        message.InReplyTo = ancestry.Count == 0 ? null : ancestry[^1];
        message.References.Clear();

        foreach (var reference in ancestry)
        {
            message.References.Add(reference);
        }
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

        var part = new MimePart(attachment.MediaType, attachment.MediaSubtype)
        {
            Content = new MimeContent(content),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment),
            ContentTransferEncoding = ContentEncoding.Base64,
            FileName = attachment.FileName,
        };

        // A written file is encoded as UTF-8, and a text part carrying no charset is read as us-ascii by whoever
        // opens it — which would leave every accented character in a file this run asked a model for mangled.
        if (attachment.Text is not null)
        {
            part.ContentType.Charset = "utf-8";
        }

        return part;
    }
}
