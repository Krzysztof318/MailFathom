// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Amazon.S3.Model;
using MailFathom.Application.Mail.Export;
using MailFathom.Infrastructure.Observability;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>One archive being produced into a multipart upload, part by part, as the zip is written.</summary>
/// <remarks>
/// <para>
/// The stream this exposes accepts both a synchronous and an asynchronous write, because the archive above it needs
/// both: a message's bytes are written asynchronously, while the framework's zip writer emits every local header and
/// the whole central directory synchronously. Only an asynchronous write sends a part, and a synchronous one appends to
/// the buffer however large it has grown — so no write here ever blocks a thread on the network, and the buffer's
/// overshoot is bounded by the archive's own bookkeeping rather than by a mailbox's size.
/// </para>
/// <para>
/// An endpoint accepts ten thousand parts, which at this part size is far past the largest archive the settings permit.
/// A deployment that raised that ceiling past what the parts cover meets the endpoint's refusal as an ordinary export
/// failure rather than as a partial archive.
/// </para>
/// </remarks>
internal sealed class S3MailboxExportArchiveWrite : MailboxExportArchiveWrite
{
    /// <summary>How much is gathered before a part is sent, which is above the five-mebibyte floor every part but the last has.</summary>
    private const int PartByteLength = 8 * 1024 * 1024;

    private readonly OpenedObjectStorageClient openedClient;
    private readonly ObjectStorageOperationRunner operationRunner;
    private readonly string bucket;
    private readonly string uploadId;
    private readonly List<PartETag> parts = [];
    private readonly MemoryStream pending = new();
    private readonly PartBufferingStream content;

    private long writtenByteLength;
    private bool completed;

    /// <summary>Initializes the write over an upload the endpoint has already accepted.</summary>
    /// <param name="openedClient">The client every part is sent through, released when this write ends.</param>
    /// <param name="operationRunner">Runs each request under the object-storage budget.</param>
    /// <param name="bucket">The bucket the upload was initiated in.</param>
    /// <param name="objectKey">The whole key the finished archive takes.</param>
    /// <param name="uploadId">What the endpoint answered the initiation with.</param>
    internal S3MailboxExportArchiveWrite(
        OpenedObjectStorageClient openedClient,
        ObjectStorageOperationRunner operationRunner,
        string bucket,
        string objectKey,
        string uploadId)
    {
        this.openedClient = openedClient;
        this.operationRunner = operationRunner;
        this.bucket = bucket;
        this.uploadId = uploadId;
        this.ObjectLocator = objectKey;
        this.content = new PartBufferingStream(this);
    }

    /// <inheritdoc />
    public override string ObjectLocator { get; }

    /// <inheritdoc />
    public override Stream Content => this.content;

    /// <inheritdoc />
    public override async Task<long> CompleteAsync(CancellationToken cancellationToken)
    {
        // Whatever is left is the last part, and the last part alone may be under the floor. An archive small enough to
        // have produced no part at all still sends one, because an upload completed with no part is not an object.
        await this.SendPartAsync(this.pending.Length, cancellationToken);

        await this.operationRunner.RunAsync(
            ObjectStorageTelemetry.PutOperationName,
            attemptToken => this.openedClient.Client.CompleteMultipartUploadAsync(
                new CompleteMultipartUploadRequest
                {
                    BucketName = this.bucket,
                    Key = this.ObjectLocator,
                    UploadId = this.uploadId,
                    PartETags = [.. this.parts],
                },
                attemptToken),
            _ => null,
            cancellationToken);

        this.completed = true;

        return this.writtenByteLength;
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        // An upload nobody completed is aborted rather than left, so the endpoint holds neither an object nor the parts
        // it would go on charging for. Abandoning it is the ordinary end of a failed or cancelled export.
        if (!this.completed)
        {
            await this.AbortAsync();
        }

        await this.content.DisposeAsync();
        await this.pending.DisposeAsync();
        this.openedClient.Dispose();
    }

    /// <summary>Takes what a write produced, and sends a part for every whole part the buffer now holds.</summary>
    private async ValueTask AppendAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        await this.pending.WriteAsync(buffer, cancellationToken);

        while (this.pending.Length >= PartByteLength)
        {
            await this.SendPartAsync(PartByteLength, cancellationToken);
        }
    }

    /// <summary>Sends the first <paramref name="partByteLength" /> buffered bytes as one part, and keeps the rest.</summary>
    private async Task SendPartAsync(long partByteLength, CancellationToken cancellationToken)
    {
        if (partByteLength == 0 && this.parts.Count > 0)
        {
            return;
        }

        var buffered = this.pending.GetBuffer();
        var partNumber = this.parts.Count + 1;

        var sent = await this.operationRunner.RunAsync(
            ObjectStorageTelemetry.PutOperationName,
            attemptToken => this.openedClient.Client.UploadPartAsync(
                new UploadPartRequest
                {
                    BucketName = this.bucket,
                    Key = this.ObjectLocator,
                    UploadId = this.uploadId,
                    PartNumber = partNumber,
                    PartSize = partByteLength,

                    // Rebuilt per attempt so a retry reads the part from its start rather than from wherever the
                    // previous attempt left the position.
                    InputStream = new MemoryStream(buffered, 0, (int)partByteLength, writable: false),
                },
                attemptToken),
            _ => partByteLength,
            cancellationToken);

        this.parts.Add(new PartETag(partNumber, sent.ETag));
        this.writtenByteLength += partByteLength;

        var remaining = this.pending.Length - partByteLength;
        Array.Copy(buffered, partByteLength, buffered, 0, remaining);
        this.pending.SetLength(remaining);
        this.pending.Position = remaining;
    }

    /// <summary>Abandons the upload, leaving the endpoint holding nothing under the key.</summary>
    /// <remarks>
    /// Runs without the caller's cancellation, because this is the path a cancelled export takes and a token already
    /// cancelled would leave the parts behind. It is the endpoint's own budget that bounds it.
    /// </remarks>
    private async Task AbortAsync() => await this.operationRunner.RunAsync(
        ObjectStorageTelemetry.DeleteOperationName,
        attemptToken => this.openedClient.Client.AbortMultipartUploadAsync(
            new AbortMultipartUploadRequest
            {
                BucketName = this.bucket,
                Key = this.ObjectLocator,
                UploadId = this.uploadId,
            },
            attemptToken),
        _ => null,
        CancellationToken.None);

    /// <summary>The stream the archive is produced into, which gathers bytes and sends each part as it fills.</summary>
    private sealed class PartBufferingStream(S3MailboxExportArchiveWrite write) : Stream
    {
        /// <inheritdoc />
        public override bool CanRead => false;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => true;

        /// <inheritdoc />
        public override long Length =>
            throw new NotSupportedException("An archive being produced has no length until it is completed.");

        /// <inheritdoc />
        public override long Position
        {
            get => throw new NotSupportedException("An archive being produced is written forward and never positioned.");
            set => throw new NotSupportedException("An archive being produced is written forward and never positioned.");
        }

        /// <inheritdoc />
        public override void Flush()
        {
            // A part leaves only on an asynchronous write, so there is nothing a synchronous flush could send.
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException("An archive being produced is written rather than read back.");

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException("An archive being produced is written forward and never positioned.");

        /// <inheritdoc />
        public override void SetLength(long value) =>
            throw new NotSupportedException("An archive being produced ends where its writer stops.");

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            this.Write(buffer.AsSpan(offset, count));
        }

        /// <inheritdoc />
        public override void Write(ReadOnlySpan<byte> buffer) => write.pending.Write(buffer);

        /// <inheritdoc />
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(buffer);

            return this.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        /// <inheritdoc />
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) =>
            write.AppendAsync(buffer, cancellationToken);
    }
}
