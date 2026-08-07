// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chunking;
using MailFathom.AI.Embeddings;
using MailFathom.AI.ProviderAdapters;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using Microsoft.Extensions.DependencyInjection;

namespace MailFathom.AI;

/// <summary>Registers what the AI boundary implements for the rest of the application.</summary>
public static class AiServiceCollectionExtensions
{
    /// <summary>Bounds an embedding response by what the declared geometry could possibly fill, plus room for its envelope.</summary>
    /// <remarks>
    /// Sixteen bytes per component is generous for a JSON float and deliberately so: the number is a ceiling on a
    /// misbehaving endpoint rather than an estimate of a well-behaved one, and the point of it is that a provider that
    /// has been replaced cannot answer with an unbounded body.
    /// </remarks>
    private const int ResponseBytesPerComponent = 16;

    private const int ResponseEnvelopeBytes = 8 * 1024;

    /// <summary>Registers the derivations retrieval is built on that reach no provider and no network.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Separate from whatever registers a provider adapter, because an instance with no embedding provider configured
    /// still chunks the mail it synchronizes: the chunks are what a later activation embeds, and deriving them costs
    /// nothing an operator has to consent to. The chunker is a singleton because it holds no state at all.
    /// </remarks>
    public static IServiceCollection AddLocalTextDerivations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(EmailChunkingRules.Current);
        services.AddSingleton<IEmailTextChunker, DeterministicEmailTextChunker>();

        return services;
    }

    /// <summary>Registers the generator that derives vectors from the text alone, reaching no provider.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="dimension">The width of the space it produces vectors in.</param>
    /// <param name="inputCharacterLimit">The width a passage is cut to before it is hashed.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when a bound is not positive.</exception>
    /// <remarks>
    /// Everything downstream of the port — the schema, the worker, the backfill, the generation switch — is provable
    /// against this and a real database at zero provider cost, which is what makes it part of the shipped code rather
    /// than a test double. A deployment that registers it is embedding with a hash and its profile row says so.
    /// </remarks>
    public static IServiceCollection AddDeterministicTextEmbeddings(
        this IServiceCollection services,
        int dimension,
        int inputCharacterLimit)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ITextEmbeddingGenerator>(
            _ => new DeterministicTextEmbeddingGenerator(dimension, inputCharacterLimit));

        return services;
    }

    /// <summary>Registers the adapter that produces vectors by calling a provider, and the transport it sends over.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The caller registers the <see cref="EmbeddingGenerationPlan" /> and the
    /// <see cref="IEmbeddingCredentialSource" /> itself, because binding configuration and resolving a secret
    /// reference both belong to the composition root. Nothing here reads configuration or knows what a secret
    /// reference is.
    /// </para>
    /// <para>
    /// Registered separately from <see cref="AddLocalTextDerivations" /> and never called by a deployment that
    /// declared no chain, so an instance with no embedding provider resolves no generator at all rather than one that
    /// fails on first use. Serving lexical search alone is a supported state.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddEmbeddingProviderAdapter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<OpenAiCompatibleEmbeddingClientFactory>();
        services.AddSingleton<ITextEmbeddingGenerator, ProviderTextEmbeddingGenerator>();

        AddEmbeddingProviderTransport(services);

        return services;
    }

    /// <summary>Registers the transport an embedding request is sent over.</summary>
    /// <remarks>
    /// <para>
    /// No base address is set, because which endpoint a request goes to is a per-endpoint setting the adapter applies
    /// per call rather than something one registration could know. Redirects are refused for the reason every
    /// credential-bearing client refuses them: a moved endpoint that answered with a redirect would carry the key or
    /// the bearer token to whatever host it named.
    /// </para>
    /// <para>
    /// The client timeout is deliberately looser than the per-request deadline the adapter applies, so a slow endpoint
    /// surfaces as this deployment's own timeout — which is classified, logged, and retried under a budget — rather
    /// than as a transport exception from underneath it.
    /// </para>
    /// </remarks>
    private static void AddEmbeddingProviderTransport(IServiceCollection services)
    {
        var client = services.AddHttpClient(ProviderTextEmbeddingGenerator.TransportName)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { AllowAutoRedirect = false })
            .ConfigureHttpClient(static (provider, client) =>
            {
                var plan = provider.GetRequiredService<EmbeddingGenerationPlan>();

                client.Timeout = plan.RequestTimeout + TimeSpan.FromSeconds(30);
                client.MaxResponseContentBufferSize =
                    ((long)plan.MaximumPassagesPerCall * plan.Identity.Dimension * ResponseBytesPerComponent)
                    + ResponseEnvelopeBytes;
            });

        // The second client in this process to opt out, and for the same reason as the first: the call already runs
        // under the AiProviderInvocation pipeline, and the host's service defaults add the standard resilience handler
        // to every client the factory builds, so keeping both would multiply the two attempt counts against a provider
        // that is already refusing. It removes what is registered before it, so it depends on AddServiceDefaults having
        // run first; the host's composition root does.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental, and is how the standard handler is opted out of.
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    }
}
