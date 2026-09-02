// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Counts the octets written to it and keeps none of them.</summary>
/// <remarks>
/// <para>
/// MIME declares no per-part length, so an attachment's decoded size can only be measured by decoding it. Writing that
/// decoded stream here answers the size without the octets going anywhere: a message describing ten files costs the
/// decode and nothing else, whatever those files weigh.
/// </para>
/// <para>
/// Retaining what it measured is deliberately not a capability. Nothing in a read returns an attachment's octets — a
/// caller receives a link to fetch the file instead — so a stream that could hold a file would be a place for one to
/// end up in a response by accident.
/// </para>
/// </remarks>
internal sealed class AttachmentContentMeasuringStream : Stream
{
    /// <summary>Gets how many octets have been written.</summary>
    public long WrittenOctets { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => this.WrittenOctets;

    /// <inheritdoc />
    public override long Position
    {
        get => this.WrittenOctets;
        set => throw new NotSupportedException("A measuring stream holds no content to seek within.");
    }

    /// <inheritdoc />
    public override void Flush()
    {
        // Nothing is buffered, so there is nothing to flush.
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("A measuring stream is written to rather than read back.");

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("A measuring stream holds no content to seek within.");

    /// <inheritdoc />
    public override void SetLength(long value) =>
        throw new NotSupportedException("A measuring stream's length is the number of octets written to it.");

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        this.Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer) => this.WrittenOctets += buffer.Length;

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return this.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        this.Write(buffer.Span);

        return ValueTask.CompletedTask;
    }
}
