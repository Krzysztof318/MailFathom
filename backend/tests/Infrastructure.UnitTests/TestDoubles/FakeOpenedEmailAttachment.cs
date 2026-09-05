// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Domain.Emails;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Stands in for one attachment already opened from stored content.</summary>
/// <remarks>
/// The description and the octets are supplied separately on purpose. A sender writes the media type, the file name,
/// and — for the reader that measures it — nothing about how many octets really follow, so a test that wants to prove
/// a copy-time bound hands over a description that understates what it is about to write.
/// </remarks>
internal sealed class FakeOpenedEmailAttachment : IOpenedEmailAttachment
{
    private readonly byte[] content;
    private readonly Action? beforeWriting;

    public FakeOpenedEmailAttachment(
        string mediaType,
        string? fileName,
        byte[] content,
        long? declaredSizeOctets = null,
        Action? beforeWriting = null)
    {
        this.content = content;
        this.beforeWriting = beforeWriting;
        this.Description = new ExtractedEmailAttachment(
            AttachmentFileName.TryNormalize(fileName, out var normalized) ? normalized : null,
            mediaType,
            declaredSizeOctets ?? content.Length);
    }

    /// <inheritdoc />
    public ExtractedEmailAttachment Description { get; }

    /// <inheritdoc />
    public async Task WriteContentToAsync(Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);

        this.beforeWriting?.Invoke();

        await destination.WriteAsync(this.content, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
