// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.Mime.Rendering;

/// <summary>Keeps the octets written to it up to a bound, and reports that the bound was passed.</summary>
/// <remarks>
/// <para>
/// The one place a message's own octets are held in memory here, and it is held to a bound for that reason. A decode
/// has to run to know how large a part is, so the choice is between measuring first and decoding twice or decoding once
/// into something that cannot grow past what the reader may be handed. This is the second, and what it costs is that a
/// part past the bound is decoded and discarded rather than skipped.
/// </para>
/// <para>
/// Writing past the bound is not an error. A picture too large to inline is reported as one the message carries and the
/// pane does not draw, which is a fact the reader is owed rather than a failure to raise.
/// </para>
/// </remarks>
internal sealed class BoundedContentBuffer(int maximumOctets) : Stream
{
    private readonly MemoryStream kept = new();

    /// <summary>Gets whether more octets were written than the bound admits.</summary>
    public bool ExceededBound { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => false;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => true;

    /// <inheritdoc />
    public override long Length => this.kept.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => this.kept.Length;
        set => throw new NotSupportedException("A bounded buffer holds no position to move.");
    }

    /// <summary>Reads back what was kept, which is nothing once the bound was passed.</summary>
    /// <returns>The octets.</returns>
    public ReadOnlyMemory<byte> Kept() => this.ExceededBound
        ? ReadOnlyMemory<byte>.Empty
        : this.kept.GetBuffer().AsMemory(0, (int)this.kept.Length);

    /// <inheritdoc />
    public override void Flush() => this.kept.Flush();

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("A bounded buffer is read back through Kept.");

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("A bounded buffer holds no position to move.");

    /// <inheritdoc />
    public override void SetLength(long value) =>
        throw new NotSupportedException("A bounded buffer's length is what has been written to it.");

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        this.Write(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (this.ExceededBound)
        {
            return;
        }

        if (this.kept.Length + buffer.Length > maximumOctets)
        {
            this.ExceededBound = true;
            this.kept.SetLength(0);

            return;
        }

        this.kept.Write(buffer);
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
            this.kept.Dispose();
        }

        base.Dispose(disposing);
    }
}
