// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Amazon.Runtime;

namespace MailFathom.Infrastructure.ObjectStorage;

/// <summary>Hands the AWS client the outbound transport this repository registered instead of one it builds itself.</summary>
/// <remarks>
/// <para>
/// Every outbound <see cref="HttpClient" /> in this process comes from <see cref="IHttpClientFactory" /> under a named
/// registration, which is where the bounds live: the timeouts, the redirect policy, the TLS trust, and whether the
/// standard resilience handler is on. Left to itself the SDK constructs its own client and none of that would apply, so
/// the one seam it offers for this is taken rather than its defaults being re-stated in two places.
/// </para>
/// <para>
/// It caches nothing and lets the SDK cache nothing. The client factory owns the handler chain and rotates it, so a
/// client opened per request keeps costing one small object while still picking up a rotated chain — which is exactly
/// the lifetime rule <c>backend/src/AGENTS.md</c> § <i>Outbound HTTP clients</i> states, and the reason a client held
/// across a process goes on resolving the endpoint to whatever it resolved to when it was made.
/// </para>
/// </remarks>
internal sealed class ObjectStorageTransportClientFactory : HttpClientFactory
{
    private readonly IHttpClientFactory httpClientFactory;

    /// <summary>Initializes a factory over the named outbound registration.</summary>
    /// <param name="httpClientFactory">The client factory the named registration's bounds and handler chain come from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="httpClientFactory" /> is <see langword="null" />.</exception>
    internal ObjectStorageTransportClientFactory(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        this.httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public override HttpClient CreateHttpClient(IClientConfig clientConfig) =>
        this.httpClientFactory.CreateClient(ObjectStorageEndpoint.TransportName);

    /// <inheritdoc />
    /// <remarks>The client factory already pools the handler chain, and a second cache in front of it would hold a chain past the point the factory retired it.</remarks>
    public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;

    /// <inheritdoc />
    /// <remarks>
    /// Disposing a client the factory handed out is the supported thing to do and releases nothing the next one needs:
    /// the handler chain is the factory's and outlives the client wrapped around it.
    /// </remarks>
    public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => true;
}
