// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using Azure.Core;
using MailFathom.AI.Embeddings;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Embeddings;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Builds the client one embedding request is sent through, for either supported provider.</summary>
/// <remarks>
/// <para>
/// One client construction serves both providers, because Azure OpenAI's v1 data plane is OpenAI-compatible: an Azure
/// deployment is this client with its endpoint pointed at the resource's <c>/openai/v1/</c> address and its model set
/// to the deployment's name. That is Microsoft's own current guidance, and it is the choice
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// makes deliberately beyond embeddings: the chat model the answering feature needs is served by this same wiring
/// rather than by a second one.
/// </para>
/// <para>
/// The client is handed the transport rather than opening its own, so the connection bounds live in one place — the
/// registration of the named client — and the handler chain rotates on the factory's schedule. Its own retry policy is
/// switched off for the reason the repository's single-layer rule gives: the call already runs under a configured
/// resilience pipeline, and a library retrying inside that would multiply the two attempt counts.
/// </para>
/// </remarks>
internal sealed class OpenAiCompatibleEmbeddingClientFactory
{
    private readonly ConcurrentDictionary<string, TokenCredential> entraCredentials = new(StringComparer.Ordinal);

    /// <summary>Opens a generator over one endpoint for the duration of one request.</summary>
    /// <param name="endpoint">Where the request goes and what it is routed to.</param>
    /// <param name="credential">What the request presents, resolved for this call.</param>
    /// <param name="transport">The transport the request is sent over, owned by the caller.</param>
    /// <returns>The generator, which the caller disposes when the request ends.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public IEmbeddingGenerator<string, Embedding<float>> Open(
        EmbeddingEndpoint endpoint,
        EmbeddingEndpointCredential credential,
        HttpClient transport)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(transport);

        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(transport),
            // Zero retries rather than a smaller number: the pipeline around this call owns repetition entirely, and a
            // library layer beneath it would be invisible to the classification that decides what may be repeated.
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        };

        if (endpoint.Address is { } address)
        {
            options.Endpoint = address;
        }

        // OPENAI001 marks the authentication-policy constructor as evaluation-only. It is nonetheless the supported
        // shape: it is what Microsoft's own Azure OpenAI guidance shows for a Microsoft Entra credential, and the
        // library offers no stable alternative that carries a bearer token. The suppression is this expression alone,
        // so nothing else in the file inherits it.
#pragma warning disable OPENAI001
        var client = credential.Kind is EmbeddingEndpointCredentialKind.ApiKey
            ? new EmbeddingClient(endpoint.RoutedModelName, new ApiKeyCredential(credential.ApiKey!), options)
            : new EmbeddingClient(endpoint.RoutedModelName, this.AuthenticationPolicyFor(endpoint, credential), options);
#pragma warning restore OPENAI001

        return client.AsIEmbeddingGenerator();
    }

    /// <summary>Resolves the bearer-token policy for an endpoint authenticated with Microsoft Entra.</summary>
    /// <remarks>
    /// The credential is built once per endpoint and kept, because fetching an access token is what it exists to do
    /// and it caches the token it fetched. Building one per request would discard that cache and turn every embedding
    /// call into a token request as well. The consequence is that rotating the client secret of a registered
    /// application takes effect at the next restart, while rotating a provider key — the shape with no token to cache —
    /// takes effect on the next call.
    /// </remarks>
    private BearerTokenPolicy AuthenticationPolicyFor(
        EmbeddingEndpoint endpoint,
        EmbeddingEndpointCredential credential)
    {
        var declaration = credential.Entra!;
        var tokenCredential = this.entraCredentials.GetOrAdd(
            endpoint.Alias,
            static (_, entra) => NonInteractiveEntraCredentials.Create(entra),
            declaration);

        return new BearerTokenPolicy(tokenCredential, declaration.TokenScope);
    }
}
