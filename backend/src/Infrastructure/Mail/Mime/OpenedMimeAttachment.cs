// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.Extraction;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>One attachment of a parsed message, held open until its octets have been written out.</summary>
/// <remarks>
/// <para>
/// The parse is kept alive because the part is a view over it: MimeKit reads a persistent message's content straight
/// from the stream it was parsed from, so disposing either before the octets are written would leave nothing to write.
/// Both are disposed here, in the order that releases the part's owner last.
/// </para>
/// <para>
/// This is the one type that puts MimeKit behind an application contract without copying anything: what crosses the
/// boundary is a description and a write, never a parsed message and never a buffer holding a file.
/// </para>
/// </remarks>
internal sealed class OpenedMimeAttachment : IOpenedEmailAttachment
{
    private readonly MimeMessage message;
    private readonly Stream parsedFrom;

    [SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The part belongs to the message that parsed it and is disposed with it; disposing it here would release it twice.")]
    private readonly MimeEntity part;

    /// <summary>Takes ownership of a parsed message and one of its parts.</summary>
    /// <param name="message">The parsed message, which this instance disposes.</param>
    /// <param name="parsedFrom">The stream the message was parsed from, which this instance disposes.</param>
    /// <param name="part">The attachment part, which the message owns.</param>
    /// <param name="description">What the part is, measured from this same parse.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public OpenedMimeAttachment(
        MimeMessage message,
        Stream parsedFrom,
        MimeEntity part,
        ExtractedEmailAttachment description)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(parsedFrom);
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(description);

        this.message = message;
        this.parsedFrom = parsedFrom;
        this.part = part;
        this.Description = description;
    }

    /// <inheritdoc />
    public ExtractedEmailAttachment Description { get; }

    /// <inheritdoc />
    public Task WriteContentToAsync(Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        return MimeAttachmentClassifier.DecodeToAsync(this.part, destination, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        this.message.Dispose();

        await this.parsedFrom.DisposeAsync();
    }
}
