// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Counts the octets written to it, and retains them only while they stay within the allowance it was given.</summary>
/// <remarks>
/// <para>
/// MIME declares no per-part length, so an attachment's decoded size can only be measured by decoding it. Writing that
/// decoded stream here is what lets one pass answer both questions a read asks about a part: how large it is, which is
/// always published, and what it contains, which is published only where a caller asked and the bounds allowed.
/// </para>
/// <para>
/// The retained buffer is released the moment the allowance is passed, so a part above the bound costs the allowance
/// rather than its own size, and a stream constructed with an allowance of zero never holds an octet. Nothing here is
/// written anywhere else: the buffer lives for the read that produced it and is released with the stream.
/// </para>
/// </remarks>
internal sealed class DecodedAttachmentContentStream : Stream
{
    private readonly int retainedOctetAllowance;
    private MemoryStream? retained;

    /// <summary>Initializes a stream that measures a part and retains what fits the allowance.</summary>
    /// <param name="retainedOctetAllowance">How many octets may be retained, where zero retains none.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="retainedOctetAllowance" /> is negative.</exception>
    public DecodedAttachmentContentStream(int retainedOctetAllowance = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(retainedOctetAllowance);

        this.retainedOctetAllowance = retainedOctetAllowance;
        this.retained = retainedOctetAllowance > 0 ? new MemoryStream() : null;
    }

    /// <summary>Gets how many octets have been written, whether or not they were retained.</summary>
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

    /// <summary>Takes the retained octets, or nothing when the allowance was passed.</summary>
    /// <returns>The octets written, or <see cref="ReadOnlyMemory{T}.Empty" /> when they were not all retained.</returns>
    /// <remarks>
    /// The copy is what makes the result independent of this stream's lifetime, which the caller needs because the
    /// stream is disposed with the part it measured while the octets travel on to the reader.
    /// </remarks>
    public ReadOnlyMemory<byte> TakeRetainedOctets() =>
        this.retained is { } buffer ? buffer.ToArray() : ReadOnlyMemory<byte>.Empty;

    /// <inheritdoc />
    public override void Flush() => this.retained?.Flush();

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
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        this.WrittenOctets += buffer.Length;

        if (this.retained is null)
        {
            return;
        }

        if (this.WrittenOctets > this.retainedOctetAllowance)
        {
            this.ReleaseRetained();

            return;
        }

        this.retained.Write(buffer);
    }

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

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.ReleaseRetained();
        }

        base.Dispose(disposing);
    }

    private void ReleaseRetained()
    {
        this.retained?.Dispose();
        this.retained = null;
    }
}
