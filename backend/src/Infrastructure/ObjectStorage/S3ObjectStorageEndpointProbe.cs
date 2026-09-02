// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Amazon.S3.Model;
using MailFathom.Infrastructure.Observability;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Asks the configured bucket for a listing, a write, and the removal of what was written.</summary>
/// <remarks>
/// <para>
/// The three run in order and stop at the first that fails, because each is a precondition of the next being meaningful:
/// a bucket that cannot be listed says nothing about whether it could be written, and reporting three failures for one
/// unreachable endpoint would tell an operator that three things are wrong.
/// </para>
/// <para>
/// The written object is zero-length and keyed under this deployment's own prefix, so a probe costs a request rather
/// than a payload and nothing about a message ever reaches the bucket to find out whether the bucket works. It is never
/// read back: what is being established is that the endpoint accepts a write, and reading it again would only establish
/// that a replica's own object had not yet been removed by another replica running the same probe.
/// </para>
/// <para>
/// Each of the three goes through <see cref="ObjectStorageOperationRunner" />, so a probe is bounded, retried, and
/// classified on exactly the terms every other call to the endpoint is, and one slow endpoint sheds probes rather than
/// holding a scrape open. They run one after another rather than together for the reason the executor enforces: one
/// logical operation is retried at exactly one layer, and re-entering the class on one flow is refused.
/// </para>
/// </remarks>
internal sealed class S3ObjectStorageEndpointProbe : IObjectStorageEndpointProbe
{
    /// <summary>The key, beneath this deployment's prefix, the probe writes and removes.</summary>
    /// <remarks>
    /// One key for the whole deployment rather than one per replica or per scrape. Two replicas probing at once write
    /// and remove the same zero-length object, which is safe because nothing reads it back, and it is what keeps a
    /// bucket from accumulating one probe object per replica that has ever run.
    /// </remarks>
    private const string ProbeRelativeKey = ".mailfathom/readiness-probe";

    private readonly IObjectStorageClientFactory clientFactory;
    private readonly ObjectStorageOperationRunner operationRunner;

    /// <summary>Initializes the probe.</summary>
    /// <param name="clientFactory">Opens the client the probe's three requests are made through.</param>
    /// <param name="operationRunner">Runs each request under the object-storage budget and classifies what stopped it.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public S3ObjectStorageEndpointProbe(
        IObjectStorageClientFactory clientFactory,
        ObjectStorageOperationRunner operationRunner)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(operationRunner);

        this.clientFactory = clientFactory;
        this.operationRunner = operationRunner;
    }

    /// <inheritdoc />
    public async Task VerifyAvailableAsync(CancellationToken cancellationToken)
    {
        var endpoint = this.clientFactory.Endpoint;
        var probeKey = endpoint.ComposeKey(ProbeRelativeKey);

        using var openedClient = await this.clientFactory.OpenAsync(cancellationToken);

        await this.operationRunner.RunAsync(
            ObjectStorageTelemetry.ListOperationName,
            attemptToken => openedClient.Client.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = endpoint.Bucket,
                    Prefix = endpoint.KeyPrefix,
                    MaxKeys = 1,
                },
                attemptToken),
            _ => null,
            cancellationToken);

        await this.operationRunner.RunAsync(
            ObjectStorageTelemetry.PutOperationName,
            attemptToken => openedClient.Client.PutObjectAsync(
                new PutObjectRequest
                {
                    BucketName = endpoint.Bucket,
                    Key = probeKey,

                    // The empty stream rather than one allocated per attempt. It owns nothing to release and never
                    // advances, so a retry sends the same zero bytes the first attempt did — where a stream read to its
                    // end would upload nothing the second time round and report success for it.
                    InputStream = Stream.Null,
                },
                attemptToken),
            _ => 0,
            cancellationToken);

        await this.operationRunner.RunAsync(
            ObjectStorageTelemetry.DeleteOperationName,
            attemptToken => openedClient.Client.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = endpoint.Bucket, Key = probeKey },
                attemptToken),
            _ => null,
            cancellationToken);
    }
}
