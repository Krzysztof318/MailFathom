// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using MailFathom.Application.Mail.Export;
using MailFathom.Domain.Exports;
using MailFathom.Infrastructure.Observability;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Keeps an export's archive as one object in the configured S3-compatible bucket, written as it is produced.</summary>
/// <remarks>
/// <para>
/// The one streamed write in this system. Every mail payload is put whole, because a message arrives whole and its
/// digest is computed over all of it; an archive is produced a message at a time and is as large as a mailbox, so it is
/// written as a multipart upload whose parts leave this process as they fill.
/// </para>
/// <para>
/// A multipart upload is invisible until it is completed, which is what gives the export the property it needs most: a
/// job that stopped leaves no object anybody could download, and the abort that follows it leaves the endpoint holding
/// nothing at all. An abort that never ran leaves parts the endpoint charges for until a lifecycle rule removes them,
/// which is the one thing an operator configures for this feature beyond the bucket itself.
/// </para>
/// <para>
/// Nothing here logs a key or any part of an archive. A key names one person's whole mailbox.
/// </para>
/// <para>
/// Both of its dependencies are registered by the object backend and by nothing else, so a deployment that kept its
/// content in the database resolves this type with neither. That is what <see cref="IsAvailable" /> reports, and it is
/// why this store is registered unconditionally where the mail payload store is not: the export job handler and the
/// expiry sweep are composed whatever the deployment stores content in, and each of them takes this port.
/// </para>
/// </remarks>
internal sealed class S3MailboxExportArchiveStore : IMailboxExportArchiveStore
{
    /// <summary>The group of keys an export archive is written under, which is a readability property and never derived from.</summary>
    private const string KeySegment = "mailbox-exports";

    private readonly IObjectStorageClientFactory? clientFactory;
    private readonly ObjectStorageOperationRunner? operationRunner;

    /// <summary>Initializes the store from what the deployment's content backend registered.</summary>
    /// <param name="clientFactory">Opens the client each request is made through, absent where no object backend was selected.</param>
    /// <param name="operationRunner">Runs each request under the object-storage budget and classifies what stopped it, absent on the same terms.</param>
    public S3MailboxExportArchiveStore(
        IObjectStorageClientFactory? clientFactory = null,
        ObjectStorageOperationRunner? operationRunner = null)
    {
        this.clientFactory = clientFactory;
        this.operationRunner = operationRunner;
    }

    /// <inheritdoc />
    public bool IsAvailable => this.clientFactory is not null && this.operationRunner is not null;

    /// <inheritdoc />
    public async Task<MailboxExportArchiveWrite> BeginWriteAsync(
        MailboxExportId exportId,
        CancellationToken cancellationToken)
    {
        var (factory, runner) = this.RequireObjectBackend();
        var endpoint = factory.Endpoint;
        var objectKey = endpoint.ComposeKey($"{KeySegment}/{exportId.Value}");

        var openedClient = await factory.OpenAsync(cancellationToken);

        try
        {
            var initiated = await runner.RunAsync(
                ObjectStorageTelemetry.PutOperationName,
                attemptToken => openedClient.Client.InitiateMultipartUploadAsync(
                    new InitiateMultipartUploadRequest
                    {
                        BucketName = endpoint.Bucket,
                        Key = objectKey,
                        ContentType = "application/zip",
                    },
                    attemptToken),
                _ => null,
                cancellationToken);

            return new S3MailboxExportArchiveWrite(
                openedClient,
                runner,
                endpoint.Bucket,
                objectKey,
                initiated.UploadId);
        }
        catch
        {
            openedClient.Dispose();

            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string objectLocator, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectLocator);

        var (factory, runner) = this.RequireObjectBackend();
        var endpoint = factory.Endpoint;
        var openedClient = await factory.OpenAsync(cancellationToken);

        try
        {
            var answer = await runner.RunAsync(
                ObjectStorageTelemetry.GetOperationName,
                attemptToken => OpenObjectAsync(openedClient.Client, endpoint.Bucket, objectLocator, attemptToken),
                response => response?.ContentLength,
                cancellationToken);

            if (answer is null)
            {
                openedClient.Dispose();

                return null;
            }

            // The client outlives this call because the body is read by whoever serves the download, so the two are
            // released together by the stream rather than here. An archive is far too large to be read into this
            // process first, which is the whole reason this port hands back a stream at all.
            return new ObjectBodyReadStream(answer, openedClient);
        }
        catch
        {
            openedClient.Dispose();

            throw;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string objectLocator, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectLocator);

        var (factory, runner) = this.RequireObjectBackend();
        var endpoint = factory.Endpoint;

        using var openedClient = await factory.OpenAsync(cancellationToken);

        await runner.RunAsync(
            ObjectStorageTelemetry.DeleteOperationName,
            attemptToken => openedClient.Client.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = endpoint.Bucket, Key = objectLocator },
                attemptToken),
            _ => null,
            cancellationToken);
    }

    /// <summary>Resolves the two dependencies an endpoint request needs, which a caller reading availability first always has.</summary>
    /// <exception cref="InvalidOperationException">Thrown when this deployment selected no object backend, which every caller refuses before reaching here.</exception>
    private (IObjectStorageClientFactory Factory, ObjectStorageOperationRunner Runner) RequireObjectBackend() =>
        this.clientFactory is { } factory && this.operationRunner is { } runner
            ? (factory, runner)
            : throw new InvalidOperationException(
                "This deployment keeps content in its database, so it has nowhere to write an export archive.");

    /// <summary>Opens one object, answering with nothing when the endpoint holds none under that key.</summary>
    /// <remarks>Resolved inside the attempt for the reason the content store's read resolves it there: a key nothing holds will not be held by the attempt after it, so the absence must not be retried.</remarks>
    private static async Task<GetObjectResponse?> OpenObjectAsync(
        IAmazonS3 client,
        string bucket,
        string objectKey,
        CancellationToken cancellationToken)
    {
        try
        {
            return await client.GetObjectAsync(
                new GetObjectRequest { BucketName = bucket, Key = objectKey },
                cancellationToken);
        }
        catch (AmazonS3Exception absent) when (absent.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
}
