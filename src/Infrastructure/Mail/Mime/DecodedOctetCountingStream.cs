// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.Mime;

/// <summary>Counts the octets written to it and keeps none of them.</summary>
/// <remarks>
/// MIME declares no per-part length, so an attachment's decoded size can only be measured by decoding it. Writing that
/// decoded stream here is what makes the measurement compatible with never materializing attachment content: the bytes
/// pass through and are discarded, and only the total survives.
/// </remarks>
internal sealed class DecodedOctetCountingStream : Stream
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
        set => throw new NotSupportedException("A counting stream holds no content to seek within.");
    }

    /// <inheritdoc />
    public override void Flush()
    {
        // Nothing is buffered, because nothing is kept.
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("A counting stream keeps no content to read back.");

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("A counting stream holds no content to seek within.");

    /// <inheritdoc />
    public override void SetLength(long value) =>
        throw new NotSupportedException("A counting stream's length is the number of octets written to it.");

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        this.WrittenOctets += count;
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer) => this.WrittenOctets += buffer.Length;

    /// <inheritdoc />
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        this.WrittenOctets += count;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        this.WrittenOctets += buffer.Length;

        return ValueTask.CompletedTask;
    }
}
