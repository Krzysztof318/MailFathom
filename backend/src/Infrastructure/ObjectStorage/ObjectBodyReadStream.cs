// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Amazon.S3.Model;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>One object's body, read straight from the endpoint, holding the response and the client open while it is.</summary>
/// <remarks>
/// An archive is read by whoever serves the download rather than by the call that opened it, so the client the body
/// arrives over cannot be released when that call returns — it is released here, when the reader is finished. This is
/// the only read in this adapter shaped that way: every mail payload is small enough to be answered whole.
/// </remarks>
internal sealed class ObjectBodyReadStream(GetObjectResponse response, OpenedObjectStorageClient openedClient) : Stream
{
    private readonly Stream body = response.ResponseStream;

    private bool released;

    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => response.ContentLength;

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException("An object's body arrives as a stream and is never positioned.");
        set => throw new NotSupportedException("An object's body arrives as a stream and is never positioned.");
    }

    /// <inheritdoc />
    public override void Flush()
    {
        // Nothing is written through this stream, so there is nothing to flush.
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return this.body.Read(buffer, offset, count);
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer) => this.body.Read(buffer);

    /// <inheritdoc />
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        return this.body.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    /// <inheritdoc />
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken) =>
        this.body.ReadAsync(buffer, cancellationToken);

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("An object's body arrives as a stream and is never positioned.");

    /// <inheritdoc />
    public override void SetLength(long value) =>
        throw new NotSupportedException("An object's body is as long as the endpoint holds it.");

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("An object's body is read rather than written through.");

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        if (!this.released)
        {
            this.released = true;

            await this.body.DisposeAsync();
            response.Dispose();
            openedClient.Dispose();
        }

        await base.DisposeAsync();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing && !this.released)
        {
            this.released = true;

            this.body.Dispose();
            response.Dispose();
            openedClient.Dispose();
        }

        base.Dispose(disposing);
    }
}
