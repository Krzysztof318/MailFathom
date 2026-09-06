// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.Extraction;
using MimeKit;

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>One attachment of a message a walk holds open, owning nothing of its own.</summary>
/// <remarks>
/// The difference from <see cref="OpenedMimeAttachment" /> is ownership and nothing else. That one is handed a parse of
/// its own and releases it; this one is a view over a parse <see cref="MimeAttachmentWalk" /> keeps alive for every
/// position, so releasing anything here would leave the positions after it with nothing to read.
/// </remarks>
internal sealed class WalkedMimeAttachment : IOpenedEmailAttachment
{
    private readonly MimeEntity part;

    /// <summary>Views one part of a message the walk owns.</summary>
    /// <param name="part">The attachment part, which the walk's message owns.</param>
    /// <param name="description">What the part is, measured from that same parse.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public WalkedMimeAttachment(MimeEntity part, ExtractedEmailAttachment description)
    {
        ArgumentNullException.ThrowIfNull(part);
        ArgumentNullException.ThrowIfNull(description);

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
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
