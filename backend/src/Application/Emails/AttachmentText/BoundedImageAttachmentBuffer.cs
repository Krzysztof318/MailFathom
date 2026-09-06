// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Attachments;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>Collects one picture's octets and stops writing the moment they grow past the per-attachment ceiling.</summary>
/// <remarks>
/// <para>
/// The size the MIME walk measured is checked before a copy begins, and this is what checks the copy itself, because a
/// measurement taken elsewhere is a second reading of the same bytes rather than a guarantee about these. A decode that
/// yields more than the walk declared would otherwise expand unbounded in memory on a ceiling an operator may set as
/// high as half a gibibyte.
/// </para>
/// <para>
/// It is write-only because <see cref="IOpenedEmailAttachment" /> writes its content to a destination rather than
/// handing one back, and it records the refusal rather than raising one: what a picture too large for the ceiling
/// becomes is a refusal written on the attachment's own row, not an exception that would end the message.
/// </para>
/// </remarks>
internal sealed class BoundedImageAttachmentBuffer(long maxOctets) : Stream
{
    private readonly MemoryStream buffer = new();

    /// <summary>Gets whether the copy was stopped because the octets grew past the ceiling.</summary>
    public bool GrewPastCeiling { get; private set; }

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
        if (this.RefusesGrowthPast(buffer.Length))
        {
            return;
        }

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
        if (this.RefusesGrowthPast(buffer.Length))
        {
            return ValueTask.CompletedTask;
        }

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

    private bool RefusesGrowthPast(int incoming)
    {
        if (this.GrewPastCeiling || this.buffer.Length + incoming > maxOctets)
        {
            this.GrewPastCeiling = true;

            return true;
        }

        return false;
    }
}
