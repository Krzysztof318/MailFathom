// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Collects an attachment's octets in memory and refuses the one that grows past the input ceiling.</summary>
/// <remarks>
/// <para>
/// Every parser here seeks, so the content has to be held rather than streamed, and this is the one place that decides
/// how much of it may be. The size the MIME walk measured is checked first and this is checked while the copy runs,
/// because a measurement taken elsewhere is a second reading of the same bytes rather than a guarantee about these.
/// </para>
/// <para>
/// It is a write-only stream because <see cref="Application.EmailContent.Attachments.IOpenedEmailAttachment" /> writes
/// its content to a destination rather than handing one back. <see cref="ToReadableStream" /> is what the parsers read.
/// </para>
/// </remarks>
internal sealed class BoundedAttachmentBuffer(long maxOctets) : Stream
{
    private readonly MemoryStream buffer = new();

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => this.buffer.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => this.buffer.Position;
        set => throw new NotSupportedException();
    }

    /// <summary>Rewinds what was collected and hands it back for reading.</summary>
    /// <returns>The collected octets, positioned at the start. The buffer keeps ownership of it.</returns>
    public Stream ToReadableStream()
    {
        this.buffer.Position = 0;

        return this.buffer;
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        this.Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        this.RefuseGrowthPast(buffer.Length);

        this.buffer.Write(buffer);
    }

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return this.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        this.RefuseGrowthPast(buffer.Length);

        return this.buffer.WriteAsync(buffer, cancellationToken);
    }

    /// <inheritdoc />
    public override void Flush() => this.buffer.Flush();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.buffer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void RefuseGrowthPast(int incoming)
    {
        if (this.buffer.Length + incoming > maxOctets)
        {
            throw new AttachmentTextExtractionStoppedException(AttachmentTextExtractionOutcome.InputTooLarge);
        }
    }
}
