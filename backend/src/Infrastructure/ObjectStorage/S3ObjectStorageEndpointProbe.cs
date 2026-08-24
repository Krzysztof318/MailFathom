// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using Amazon.S3.Model;
using MailFathom.Application.Resilience;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Resilience;
using Microsoft.Extensions.Hosting;

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
/// Each of the three runs under the <see cref="OutboundDependency.ObjectStorageInvocation" /> pipeline, so a probe is
/// bounded and retried on exactly the terms every other call to the endpoint is, and one slow endpoint sheds probes
/// rather than holding a scrape open. They run one after another rather than together for the reason the executor
/// enforces: one logical operation is retried at exactly one layer, and re-entering the class on one flow is refused.
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
    private readonly OutboundOperationExecutor operationExecutor;
    private readonly ObjectStorageTelemetry telemetry;
    private readonly IHostApplicationLifetime applicationLifetime;

    /// <summary>Initializes the probe.</summary>
    /// <param name="clientFactory">Opens the client the probe's three requests are made through.</param>
    /// <param name="operationExecutor">Runs each request under the object-storage resilience budget.</param>
    /// <param name="telemetry">Publishes what each request cost and what stopped it.</param>
    /// <param name="applicationLifetime">Supplies the stopping token that tells a shutdown from a caller giving up.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public S3ObjectStorageEndpointProbe(
        IObjectStorageClientFactory clientFactory,
        OutboundOperationExecutor operationExecutor,
        ObjectStorageTelemetry telemetry,
        IHostApplicationLifetime applicationLifetime)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(operationExecutor);
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(applicationLifetime);

        this.clientFactory = clientFactory;
        this.operationExecutor = operationExecutor;
        this.telemetry = telemetry;
        this.applicationLifetime = applicationLifetime;
    }

    /// <inheritdoc />
    public async Task VerifyAvailableAsync(CancellationToken cancellationToken)
    {
        var endpoint = this.clientFactory.Endpoint;
        var probeKey = endpoint.ComposeKey(ProbeRelativeKey);

        using var openedClient = await this.clientFactory.OpenAsync(cancellationToken);

        await this.RunAsync(
            ObjectStorageTelemetry.ListOperationName,
            payloadByteLength: null,
            attemptToken => openedClient.Client.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = endpoint.Bucket,
                    Prefix = endpoint.KeyPrefix,
                    MaxKeys = 1,
                },
                attemptToken),
            cancellationToken);

        await this.RunAsync(
            ObjectStorageTelemetry.PutOperationName,
            payloadByteLength: 0,
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
            cancellationToken);

        await this.RunAsync(
            ObjectStorageTelemetry.DeleteOperationName,
            payloadByteLength: null,
            attemptToken => openedClient.Client.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = endpoint.Bucket, Key = probeKey },
                attemptToken),
            cancellationToken);
    }

    /// <summary>Runs one request under the resilience budget, measures it, and reports what stopped it.</summary>
    /// <remarks>
    /// A caller's own cancellation is recorded and rethrown rather than translated, so a scrape the caller abandoned
    /// stays what it was: a fact about the caller, not a bucket that failed.
    /// </remarks>
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Whatever stopped the request, it is classified into what an operator acts on and rethrown; narrowing the catch would let an unrecognized failure reach a readiness scrape uncoded.")]
    private async Task RunAsync<TAnswer>(
        string operation,
        long? payloadByteLength,
        Func<CancellationToken, Task<TAnswer>> request,
        CancellationToken cancellationToken)
    {
        using var measurement = this.telemetry.Begin(operation);

        try
        {
            await this.operationExecutor.ExecuteAsync(
                OutboundDependency.ObjectStorageInvocation,
                request,
                cancellationToken);

            measurement.Succeeded(payloadByteLength);
        }
        catch (Exception failure)
        {
            var classification = ObjectStorageFailureClassifier.Classify(
                failure,
                cancellationToken,
                this.applicationLifetime.ApplicationStopping);

            measurement.Failed(classification);

            if (classification == ObjectStorageFailure.CallerCancelled)
            {
                throw;
            }

            throw ObjectStorageUnavailableException.From(classification, failure);
        }
    }
}
