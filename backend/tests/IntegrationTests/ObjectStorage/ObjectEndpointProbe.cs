// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using MailFathom.AppHost;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.IntegrationTests.Orchestration;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.IntegrationTests.ObjectStorage;

/// <summary>Reaches the orchestrated endpoint directly, for the arrangements and assertions the port cannot express.</summary>
/// <remarks>
/// <para>
/// Two kinds of thing need this. An arrangement the adapter refuses to produce — a second payload under a key already
/// taken, a digest that disagrees with its bytes, an object outside this deployment's prefix — and an assertion about
/// whether the server holds an object at all, which reading it back through the port would answer using the same code
/// path the test is checking the arrangement of.
/// </para>
/// <para>
/// The client comes from the factory the adapter itself opens, so what these requests sign, address, and send is what a
/// deployment sends. Nothing here is a second way of reaching the endpoint.
/// </para>
/// </remarks>
internal static class ObjectEndpointProbe
{
    /// <summary>Writes one object under a key of the caller's choosing, with no conditional header and no checksum.</summary>
    /// <param name="services">The composed services, which must have the object backend selected.</param>
    /// <param name="objectKey">The whole key, which need not be one the adapter would ever mint.</param>
    /// <param name="payload">The bytes to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the endpoint holds the object.</returns>
    internal static Task<bool> PutObjectAsync(
        OrchestratedMailFathomServices services,
        string objectKey,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                using var openedClient = await scope
                    .GetRequiredService<IObjectStorageClientFactory>()
                    .OpenAsync(token);

                await openedClient.Client.PutObjectAsync(
                    new PutObjectRequest
                    {
                        BucketName = OrchestrationContract.ObjectStorageBucket,
                        Key = objectKey,
                        InputStream = new MemoryStream(payload.ToArray(), writable: false),
                    },
                    token);

                return true;
            },
            cancellationToken);

    /// <summary>Asks the endpoint how large the object under one key is, answering with nothing where it holds none.</summary>
    /// <param name="services">The composed services, which must have the object backend selected.</param>
    /// <param name="objectKey">The whole key.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The object's length, or <see langword="null" /> when the endpoint holds no object under that key.</returns>
    internal static Task<long?> ReadObjectLengthAsync(
        OrchestratedMailFathomServices services,
        string objectKey,
        CancellationToken cancellationToken) => services.InScopeAsync(
            async (scope, token) =>
            {
                using var openedClient = await scope
                    .GetRequiredService<IObjectStorageClientFactory>()
                    .OpenAsync(token);

                try
                {
                    var metadata = await openedClient.Client.GetObjectMetadataAsync(
                        new GetObjectMetadataRequest
                        {
                            BucketName = OrchestrationContract.ObjectStorageBucket,
                            Key = objectKey,
                        },
                        token);

                    return (long?)metadata.ContentLength;
                }
                catch (AmazonS3Exception absent) when (absent.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }
            },
            cancellationToken);
}
