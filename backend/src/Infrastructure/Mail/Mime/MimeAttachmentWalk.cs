// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.EmailContent.Attachments;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>One parsed message held open so every attachment on it can be read without parsing it again.</summary>
/// <remarks>
/// <para>
/// The parse is kept alive for the same reason <see cref="OpenedMimeAttachment" /> keeps one: a part is a view over the
/// stream it was parsed from, so nothing may be released while an attachment taken from this walk is still being read.
/// What differs is how many parts share it — all of them, which is the whole point.
/// </para>
/// <para>
/// An attachment handed out is a view rather than a user, so its own disposal releases nothing and this type releases
/// the message and the stream once. That inversion is stated on <see cref="IOpenedEmailAttachment" /> as well, because
/// it is the one thing a caller holding both could get wrong.
/// </para>
/// </remarks>
internal sealed class MimeAttachmentWalk : IOpenedEmailAttachmentWalk
{
    private readonly MimeMessage message;
    private readonly Stream parsedFrom;
    private readonly IReadOnlyList<MimeEntity> parts;

    /// <summary>Takes ownership of a parsed message and the walk over its attachment parts.</summary>
    /// <param name="message">The parsed message, which this instance disposes.</param>
    /// <param name="parsedFrom">The stream the message was parsed from, which this instance disposes.</param>
    /// <param name="parts">The attachment parts, in walk order, each owned by the message.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MimeAttachmentWalk(MimeMessage message, Stream parsedFrom, IReadOnlyList<MimeEntity> parts)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(parsedFrom);
        ArgumentNullException.ThrowIfNull(parts);

        this.message = message;
        this.parsedFrom = parsedFrom;
        this.parts = parts;
    }

    /// <inheritdoc />
    public int Count => this.parts.Count;

    /// <inheritdoc />
    /// <remarks>
    /// Measuring a part decodes it, which is where a damaged local copy is met — so the same two refusals the
    /// single-position read reports are reported here, and for the same reasons it records against each.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The view is the returned value and owns nothing; the walk owns the parse behind it and releases it once, which is what IOpenedEmailAttachment states about an attachment taken from a walk.")]
    public async Task<OpenedEmailAttachmentResult> OpenAsync(
        int attachmentPosition,
        CancellationToken cancellationToken)
    {
        if (attachmentPosition < 0 || attachmentPosition >= this.parts.Count)
        {
            return OpenedEmailAttachmentResult.NoSuchAttachment();
        }

        var part = this.parts[attachmentPosition];

        try
        {
            var description = await MimeAttachmentClassifier.DescribeAttachmentAsync(part, cancellationToken);

            return OpenedEmailAttachmentResult.Opened(new WalkedMimeAttachment(part, description));
        }
        catch (FormatException)
        {
            return OpenedEmailAttachmentResult.Unreadable();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        this.message.Dispose();

        await this.parsedFrom.DisposeAsync();
    }
}
