// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction.Attachments;

namespace MailFathom.Infrastructure.Documents;

/// <summary>Reads one archive part while counting what it inflates to, and stops the moment it stops being plausible.</summary>
/// <remarks>
/// <para>
/// Both bounds are checked on every read rather than once at the end, which is the whole point: an archive whose
/// declared uncompressed size is enormous has already won if the check happens after the inflation. Nothing here reads
/// the declared size at all — that number is the sender's, and a decompression bomb is exactly a file that lies about
/// it. What is measured against instead is the part's compressed length, already clamped by
/// <see cref="DecompressionBudget.HonestCompressedLength" /> to something the archive can actually back, because that
/// field is the sender's too and only the containing file's own length bounds it.
/// </para>
/// <para>
/// The ratio catches the small archive and the total catches the large one. A part inflating far past what its
/// compressed length can honestly explain is refused before the total would have been reached, and a collection of
/// individually plausible parts is refused by the total they share.
/// </para>
/// </remarks>
internal sealed class BoundedInflationStream(
    Stream inflated,
    long compressedOctets,
    DecompressionBudget budget,
    int maxRatio) : Stream
{
    private long inflatedOctets;

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => this.inflatedOctets;
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return this.Read(buffer.AsSpan(offset, count));
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        var read = inflated.Read(buffer);

        this.CountAndRefuse(read);

        return read;
    }

    /// <inheritdoc />
    public override void Flush() => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inflated.Dispose();
        }

        base.Dispose(disposing);
    }

    private void CountAndRefuse(int read)
    {
        if (read <= 0)
        {
            return;
        }

        this.inflatedOctets += read;

        budget.Consume(read);

        // A part small enough that its compressed length says nothing useful about a ratio is left to the shared total
        // instead: the smallest deflate stream is already several times its own payload, so a ratio over a handful of
        // octets refuses ordinary markup.
        if (compressedOctets > 0 && this.inflatedOctets > compressedOctets * maxRatio)
        {
            throw new AttachmentTextExtractionStoppedException(AttachmentTextExtractionOutcome.ContainerBoundExceeded);
        }
    }
}
