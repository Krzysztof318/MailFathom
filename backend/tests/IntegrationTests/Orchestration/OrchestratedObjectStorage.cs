// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.AppHost;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Secrets.Resolution;

namespace MailFathom.IntegrationTests.Orchestration;

/// <summary>Describes the orchestrated S3-compatible endpoint to the services a test composes over it.</summary>
/// <remarks>
/// The endpoint's address is allocated when its container starts, so it is read from the fixture rather than declared.
/// Everything else is a constant both sides of the run already agree on, which is what
/// <see cref="OrchestrationContract" /> exists for.
/// </remarks>
internal static class OrchestratedObjectStorage
{
    /// <summary>How long a request to the orchestrated endpoint may take to connect.</summary>
    /// <remarks>Generous, because the endpoint is a container on this machine and nothing here is a test about a timeout.</remarks>
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How long one request to the orchestrated endpoint may take in total.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The bounds the sweep runs under in this suite, which are the shipped defaults.</summary>
    /// <remarks>
    /// The age floor above all: a suite that lowered it would be exercising a sweep that can race the write ordering
    /// this whole backend rests on, and every object a test writes is younger than any floor worth configuring.
    /// </remarks>
    internal static readonly ContentObjectReclamationBounds ReclamationBounds =
        ContentObjectReclamationBounds.Create(TimeSpan.FromHours(24), maximumObjectsPerRun: 100_000);

    /// <summary>Describes the endpoint the fixture started.</summary>
    /// <param name="published">The address the orchestration allocated for it.</param>
    /// <returns>The endpoint, addressed path-style because a container answers no bucket subdomain.</returns>
    internal static ObjectStorageEndpoint EndpointAt(Uri published) => ObjectStorageEndpoint.Create(
        published,
        OrchestrationContract.ObjectStorageBucket,
        OrchestrationContract.ObjectStorageKeyPrefix,
        OrchestrationContract.ObjectStorageRegion,
        usePathStyleAddressing: true,
        ConnectTimeout,
        RequestTimeout);

    /// <summary>Supplies the credential the orchestrated endpoint admits.</summary>
    /// <remarks>
    /// The composition root resolves a configured reference here, and this suite starts no composition root. What it
    /// stands in for is a deployment whose reference resolved: the material is what the app model initialized the
    /// container with, and it names nothing outside this run.
    /// </remarks>
    [SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "Resolved by the container the harness composes rather than constructed by name.")]
    internal sealed class StatedCredentialSource : IObjectStorageCredentialSource
    {
        /// <inheritdoc />
        public Task<ObjectStorageCredential> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(ObjectStorageCredential.Create(
                ResolvedSecret.FromText(OrchestrationContract.ObjectStorageAccessKey),
                ResolvedSecret.FromText(OrchestrationContract.ObjectStorageSecretKey)));
    }
}
