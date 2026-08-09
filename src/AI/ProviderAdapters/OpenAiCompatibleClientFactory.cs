// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Collections.Concurrent;
using Azure.Core;
using MailFathom.AI.Chat;
using MailFathom.AI.Embeddings;
using MailFathom.AI.Providers;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using OpenAI.Responses;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Builds the client one provider request is sent through, for either supported provider and either role.</summary>
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
/// The two roles share this type rather than each holding a copy, because everything that differs between them is the
/// last expression of each method. The transport, the retry opt-out, the endpoint override, and the two authentication
/// shapes are one decision, and the Microsoft Entra credentials are one cache — which matters, because a credential
/// built per call would discard the access token it exists to hold.
/// </para>
/// <para>
/// A client is handed the transport rather than opening its own, so the connection bounds live in one place — the
/// registration of the named client — and the handler chain rotates on the factory's schedule. Its own retry policy is
/// switched off for the reason the repository's single-layer rule gives: the call already runs under a configured
/// resilience pipeline, and a library retrying inside that would multiply the two attempt counts.
/// </para>
/// </remarks>
internal sealed class OpenAiCompatibleClientFactory
{
    private readonly ConcurrentDictionary<string, TokenCredential> entraCredentials = new(StringComparer.Ordinal);

    /// <summary>Opens an embedding generator over one endpoint for the duration of one request.</summary>
    /// <param name="endpoint">Where the request goes and what it is routed to.</param>
    /// <param name="credential">What the request presents, resolved for this call.</param>
    /// <param name="transport">The transport the request is sent over, owned by the caller.</param>
    /// <returns>The generator, which the caller disposes when the request ends.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public IEmbeddingGenerator<string, Embedding<float>> OpenEmbeddingGenerator(
        EmbeddingEndpoint endpoint,
        ProviderEndpointCredential credential,
        HttpClient transport)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(transport);

        var options = BuildClientOptions(endpoint.Address, transport);

        // OPENAI001 marks the authentication-policy constructor as evaluation-only. It is nonetheless the supported
        // shape: it is what Microsoft's own Azure OpenAI guidance shows for a Microsoft Entra credential, and the
        // library offers no stable alternative that carries a bearer token. The suppression is this expression alone,
        // so nothing else in the file inherits it.
#pragma warning disable OPENAI001
        var client = credential.Kind is ProviderEndpointCredentialKind.ApiKey
            ? new EmbeddingClient(endpoint.RoutedModelName, new ApiKeyCredential(credential.ApiKey!), options)
            : new EmbeddingClient(
                endpoint.RoutedModelName,
                this.AuthenticationPolicyFor(endpoint.Alias, credential),
                options);
#pragma warning restore OPENAI001

        return client.AsIEmbeddingGenerator();
    }

    /// <summary>Opens a chat client over the declared endpoint for the duration of one request.</summary>
    /// <param name="endpoint">Where the request goes, what it is routed to, and which API it is conducted through.</param>
    /// <param name="credential">What the request presents, resolved for this call.</param>
    /// <param name="transport">The transport the request is sent over, owned by the caller.</param>
    /// <returns>The client, which the caller disposes when the request ends.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the endpoint names an API this factory cannot reach.</exception>
    /// <remarks>
    /// Both APIs are opened here rather than by two factories, because everything around the choice is one decision: the
    /// address, the credential, the transport, and the retry opt-out are identical, and what the returned client
    /// publishes is the same provider-neutral interface either way. The caller therefore never learns which surface it
    /// is speaking to, which is what keeps the resilience decorator, the budget decorator, and the composed agent
    /// unchanged by the choice.
    /// </remarks>
    public IChatClient OpenChatClient(
        ChatEndpoint endpoint,
        ProviderEndpointCredential credential,
        HttpClient transport)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(transport);

        return endpoint.Api switch
        {
            ChatProviderApi.ChatCompletions => this.OpenChatCompletionsClient(endpoint, credential, transport),
            ChatProviderApi.Responses => this.OpenResponsesClient(endpoint, credential, transport),
            _ => throw new ArgumentOutOfRangeException(
                nameof(endpoint),
                endpoint.Api,
                "The endpoint names no API this factory can reach."),
        };
    }

    private IChatClient OpenChatCompletionsClient(
        ChatEndpoint endpoint,
        ProviderEndpointCredential credential,
        HttpClient transport)
    {
        var options = BuildClientOptions(endpoint.Address, transport);

#pragma warning disable OPENAI001
        var client = credential.Kind is ProviderEndpointCredentialKind.ApiKey
            ? new ChatClient(endpoint.RoutedModelName, new ApiKeyCredential(credential.ApiKey!), options)
            : new ChatClient(
                endpoint.RoutedModelName,
                this.AuthenticationPolicyFor(endpoint.Alias, credential),
                options);
#pragma warning restore OPENAI001

        return client.AsIChatClient();
    }

    /// <summary>Opens the same endpoint through the responses API instead.</summary>
    /// <remarks>
    /// The routed model reaches the request rather than the client, which is the one structural difference between the
    /// two surfaces: a responses client is opened over an endpoint and told what to route to per call, while a chat
    /// completions client is constructed around its model. Nothing above this sees the difference.
    /// </remarks>
    private IChatClient OpenResponsesClient(
        ChatEndpoint endpoint,
        ProviderEndpointCredential credential,
        HttpClient transport)
    {
        var options = BuildResponsesClientOptions(endpoint.Address, transport);

        // The whole responses surface carries the evaluation-only marker in this release of the client library, the
        // adapter that publishes it as a chat client included, so the suppression covers the construction and the
        // conversion rather than one expression. It is still confined to the two members named here.
#pragma warning disable OPENAI001
        var client = credential.Kind is ProviderEndpointCredentialKind.ApiKey
            ? new ResponsesClient(new ApiKeyCredential(credential.ApiKey!), options)
            : new ResponsesClient(this.AuthenticationPolicyFor(endpoint.Alias, credential), options);

        return client.AsIChatClient(endpoint.RoutedModelName);
#pragma warning restore OPENAI001
    }

    /// <summary>Builds the options a chat completions or embedding client is constructed with.</summary>
    private static OpenAIClientOptions BuildClientOptions(Uri? address, HttpClient transport)
    {
        var options = new OpenAIClientOptions();

        ApplyPipeline(options, transport);

        if (address is not null)
        {
            options.Endpoint = address;
        }

        return options;
    }

    /// <summary>Builds the options a responses client is constructed with.</summary>
    /// <remarks>
    /// A second builder rather than a shared one, because the client library declares the two option types as siblings
    /// with an <c>Endpoint</c> of their own each rather than as one type with a common base carrying it.
    /// </remarks>
#pragma warning disable OPENAI001 // The responses option type is evaluation-only in this release of the client library.
    private static ResponsesClientOptions BuildResponsesClientOptions(Uri? address, HttpClient transport)
    {
        var options = new ResponsesClientOptions();

        ApplyPipeline(options, transport);

        if (address is not null)
        {
            options.Endpoint = address;
        }

        return options;
    }
#pragma warning restore OPENAI001

    /// <summary>Puts every client of either role and either API on this deployment's transport, and takes its own retries away.</summary>
    /// <remarks>
    /// Zero retries rather than a smaller number: the pipeline around the call owns repetition entirely, and a library
    /// layer beneath it would be invisible to the classification that decides what may be repeated.
    /// </remarks>
    private static void ApplyPipeline(ClientPipelineOptions options, HttpClient transport)
    {
        options.Transport = new HttpClientPipelineTransport(transport);
        options.RetryPolicy = new ClientRetryPolicy(maxRetries: 0);
    }

    /// <summary>Resolves the bearer-token policy for an endpoint authenticated with Microsoft Entra.</summary>
    /// <remarks>
    /// The credential is built once per endpoint and kept, because fetching an access token is what it exists to do
    /// and it caches the token it fetched. Building one per request would discard that cache and turn every provider
    /// call into a token request as well. The consequence is that rotating the client secret of a registered
    /// application takes effect at the next restart, while rotating a provider key — the shape with no token to cache —
    /// takes effect on the next call.
    /// </remarks>
    private BearerTokenPolicy AuthenticationPolicyFor(string endpointAlias, ProviderEndpointCredential credential)
    {
        var declaration = credential.Entra!;
        var tokenCredential = this.entraCredentials.GetOrAdd(
            endpointAlias,
            static (_, entra) => NonInteractiveEntraCredentials.Create(entra),
            declaration);

        return new BearerTokenPolicy(tokenCredential, declaration.TokenScope);
    }
}
