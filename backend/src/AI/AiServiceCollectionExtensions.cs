// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Chunking;
using MailFathom.AI.Embeddings;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.Retrieval;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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

    /// <summary>Bounds a chat response by what the configured output budget could possibly fill, plus room for its envelope.</summary>
    /// <remarks>
    /// Thirty-two bytes per output token is generous for text of any script and deliberately so, for the reason the
    /// per-component figure above is: the number is a ceiling on a misbehaving endpoint rather than an estimate of a
    /// well-behaved one, and a provider that has been replaced must not be able to answer with an unbounded body.
    /// </remarks>
    private const int ResponseBytesPerOutputToken = 32;

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
    /// <see cref="IProviderEndpointCredentialSource" /> itself, because binding configuration and resolving a secret
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

        services.TryAddSingleton<OpenAiCompatibleClientFactory>();
        services.AddSingleton<ITextEmbeddingGenerator, ProviderTextEmbeddingGenerator>();

        AddEmbeddingProviderTransport(services);

        return services;
    }

    /// <summary>Registers the adapter that produces answers by calling a chat provider, and the transport it sends over.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The caller registers the <see cref="IChatGenerationPlanSource" />, the scoped <see cref="ChatGenerationPlan" />
    /// it publishes, and the <see cref="IProviderEndpointCredentialSource" /> itself, because binding configuration and
    /// resolving a secret reference both belong to the composition root. Nothing here reads configuration or knows what
    /// a secret reference is.
    /// </para>
    /// <para>
    /// The client is scoped rather than a singleton because the plan it runs against is: the declaration behind it
    /// reloads, and a client built once would go on calling the model the process started with. One scope is one
    /// operation, so the plan a call uses is the one its operation began with.
    /// </para>
    /// <para>
    /// Independent of <see cref="AddEmbeddingProviderAdapter" /> in both directions, and that is the point rather than
    /// an accident of ordering. An instance may declare a chat provider and no embedding provider, or the reverse, and
    /// each of those is a working deployment with a different set of capabilities — so neither method assumes the other
    /// ran, and the client factory they share is registered by whichever one runs first.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddChatProviderAdapter(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<OpenAiCompatibleClientFactory>();
        services.AddScoped<IChatModelClient, ProviderChatModelClient>();

        AddChatProviderTransport(services);

        return services;
    }

    /// <summary>Registers the agent that answers a question about the mailbox from what it retrieves while answering.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Called only where a chat endpoint was declared, and beside <see cref="AddChatProviderAdapter" /> rather than inside
    /// it: they are two capabilities over one endpoint, and an instance that answers questions is not the same decision as
    /// one that can generate text.
    /// </para>
    /// <para>
    /// Scoped, because a run retrieves through the mailbox search, and that reads through the scoped persistence context.
    /// It adds no transport of its own — a run's requests go to the same endpoint under the same bounds as any other chat
    /// request, so it sends over the client that registration named.
    /// </para>
    /// <para>
    /// The instruction envelope every composed agent is wrapped in is registered here rather than by the composition
    /// root, because it is what this boundary composes with rather than something a deployment declares. It is added
    /// only if nothing registered one already, so an implementation supplying a person's language or a deployment's own
    /// wording replaces the empty default by registering before this runs, with whatever lifetime its answer varies
    /// over.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMailAnsweringAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<OpenAiCompatibleClientFactory>();
        services.TryAddSingleton<IAgentInstructionEnvelope, EmptyAgentInstructionEnvelope>();
        services.AddScoped<IMailQuestionAnswerer, MailAnsweringAgent>();

        return services;
    }

    /// <summary>Registers the second retrieval pass that puts the fused ranking's own candidates to the model.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Called only where a chat endpoint was declared and the deployment turned the pass on, which is why the plan and
    /// the chat client are resolved rather than checked for: an instance that declared neither registers nothing here
    /// and retrieves exactly as it did, which is the default and the cheaper path.
    /// </para>
    /// <para>
    /// It decorates rather than replaces, and must therefore be registered after the retrieval it wraps: the container
    /// resolves the last registration of a service type, and the one this wraps is added by <c>AddInfrastructure</c>.
    /// The wrapped search is resolved by its own type, so both this and anything else resolving it within one scope get
    /// the one instance reading through that scope's persistence context.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddModelJudgedRetrieval(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IEmailKnowledgeSearch>(provider => new ModelJudgedKnowledgeSearch(
            provider.GetRequiredService<MailboxKnowledgeSearch>(),
            provider.GetRequiredService<IChatModelClient>(),
            provider.GetRequiredService<IAiProviderHealthReader>(),
            provider.GetRequiredService<PassageRelevanceFilterPlan>(),
            provider.GetRequiredService<ILogger<ModelJudgedKnowledgeSearch>>()));

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

    /// <summary>Registers the transport a chat request is sent over.</summary>
    /// <remarks>
    /// <para>
    /// A registration of its own rather than a second consumer of the embedding one, because the two carry different
    /// bounds against different endpoints: an answer is a stream of prose whose size follows the configured output
    /// budget, while an embedding response is a block of numbers whose size the declared geometry fixes exactly. One
    /// client would have to take the larger of the two ceilings and would then bound neither.
    /// </para>
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
    /// <para>
    /// The bounds are read from the plan source rather than from a resolved plan, because this runs on the root
    /// provider whenever the factory builds a client and the plan itself is scoped to an operation. A client opened
    /// after a reload therefore carries the reloaded ceilings, which is the same answer the operation holding the plan
    /// gets.
    /// </para>
    /// </remarks>
    private static void AddChatProviderTransport(IServiceCollection services)
    {
        var client = services.AddHttpClient(ProviderChatModelClient.TransportName)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { AllowAutoRedirect = false })
            .ConfigureHttpClient(static (provider, client) =>
            {
                var plan = provider.GetRequiredService<IChatGenerationPlanSource>().Current;

                client.Timeout = plan.RequestTimeout + TimeSpan.FromSeconds(30);
                client.MaxResponseContentBufferSize =
                    ((long)plan.MaximumOutputTokens * ResponseBytesPerOutputToken) + ResponseEnvelopeBytes;
            });

        // The third client in this process to opt out, for the reason the embedding one does.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental, and is how the standard handler is opted out of.
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    }
}
