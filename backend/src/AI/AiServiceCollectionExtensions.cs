// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.BodyCleanup;
using MailFathom.AI.Chat;
using MailFathom.AI.Chunking;
using MailFathom.AI.Descriptions;
using MailFathom.AI.Discovery;
using MailFathom.AI.Embeddings;
using MailFathom.AI.Enrichment;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.ReplyDrafts;
using MailFathom.AI.Retrieval;
using MailFathom.AI.Search;
using MailFathom.AI.ThreadStates;
using MailFathom.Application.Access;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.EmailContent.Cleaning;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Emails.Extraction.Images;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.ReplyDrafts;
using MailFathom.Application.Emails.Search.Phrasing;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.SensitiveContent.Egress;
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

    /// <summary>Registers the one way an image attachment becomes text, in whichever of its two states the deployment is in.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="maximumPixelCount">The largest pixel grid an image may declare and still be sent, ignored where the deployment describes nothing.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumPixelCount" /> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// Called unconditionally and always registering something, which is the difference from every other call here. The
    /// port answers with a reason rather than with an absence, so a caller records why an attachment produced no
    /// description without knowing what a deployment declared — and the reason a deployment that turned nothing on
    /// gives is the one an operator would recognize as their own decision.
    /// </para>
    /// <para>
    /// Which of the two is registered is decided once, at composition. An instance that has not activated image
    /// description never resolves a chat client, never opens a transport, and never buffers an attachment: the
    /// activation is a registration rather than a branch inside a call, so there is no path by which a picture leaves
    /// an instance whose operator did not ask for it.
    /// </para>
    /// <para>
    /// Scoped when it is active, because it sends over the scoped chat client and a background run makes one scope per
    /// message. The inactive one is a singleton holding nothing.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddImageAttachmentDescription(
        this IServiceCollection services,
        long? maximumPixelCount)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (maximumPixelCount is not { } ceiling)
        {
            services.AddSingleton<IEmailAttachmentImageDescriber>(InactiveImageAttachmentDescriber.Instance);

            return services;
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ceiling);

        services.AddScoped<IEmailAttachmentImageDescriber>(provider => new ImageAttachmentDescriber(
            provider.GetRequiredService<IChatModelClient>(),
            provider.GetRequiredService<ChatGenerationPlan>(),
            ceiling,
            provider.GetRequiredService<ILogger<ImageAttachmentDescriber>>()));

        return services;
    }

    /// <summary>Registers the one way a message becomes the marks a list row draws, in whichever of its two states the deployment is in.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="isActivated">Whether the deployment declared a chat endpoint and asked for its mail to be enriched.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Called unconditionally and always registering something, which it shares with image description and for the same
    /// reason: the port answers with a reason rather than with an absence, so the arrival pass records why a message
    /// carries no marks without knowing what a deployment declared.
    /// </para>
    /// <para>
    /// Which of the two is registered is decided once, at composition. An instance that has not activated enrichment
    /// never resolves a chat client, never opens a transport, and never composes a turn: the activation is a
    /// registration rather than a branch inside a call, so there is no path by which a message leaves an instance whose
    /// operator did not ask for it.
    /// </para>
    /// <para>
    /// Scoped when it is active, because the account run makes one scope per pass and the agent's ledger, credential,
    /// and transport belong to that pass. The inactive one is a singleton holding nothing.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddEmailEnrichmentAgent(this IServiceCollection services, bool isActivated)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!isActivated)
        {
            services.AddSingleton<IEmailEnricher>(InactiveEmailEnricher.Instance);

            return services;
        }

        services.TryAddSingleton<OpenAiCompatibleClientFactory>();
        services.TryAddSingleton<IAgentInstructionEnvelope, EmptyAgentInstructionEnvelope>();
        services.AddScoped<IEmailEnricher, EmailEnrichmentAgent>();

        return services;
    }

    /// <summary>Registers the one way a conversation becomes the state drawn beside it, in whichever of its two states the deployment is in.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="isActivated">Whether the deployment declared a chat endpoint and asked for its conversations to be read into a state.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Registered on the same terms as enrichment beside it, and for the same reason: the port answers with a reason
    /// rather than with an absence, so the pass records why a conversation carries no state and the screen draws that
    /// absence as a state of its own instead of as a failure.
    /// </para>
    /// <para>
    /// Which of the two is registered is decided once, at composition, so an instance that did not ask for this never
    /// resolves a chat client and never composes a turn out of somebody's mail.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddThreadStateAgent(this IServiceCollection services, bool isActivated)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!isActivated)
        {
            services.AddSingleton<IThreadStateDeriver>(InactiveThreadStateDeriver.Instance);

            return services;
        }

        services.TryAddSingleton<OpenAiCompatibleClientFactory>();
        services.TryAddSingleton<IAgentInstructionEnvelope, EmptyAgentInstructionEnvelope>();
        services.AddScoped<IThreadStateDeriver, ThreadStateAgent>();

        return services;
    }

    /// <summary>Registers the one way a reduced body becomes the cleaned rendering, in whichever of its two states the deployment is in.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="isActivated">Whether the deployment declared a chat endpoint, which is the whole of what this pass needs: which readers want the rendering is their own preference rather than an operator's key.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Registered on the same terms as enrichment and a conversation's state: the port answers with a reason rather than
    /// with an absence, so the reading pane draws the uncleaned message and says why instead of drawing an empty pane.
    /// </para>
    /// <para>
    /// Which of the two is registered is decided once, at composition, so an instance that did not ask for this never
    /// resolves a chat client and never composes a turn out of somebody's mail. The use case is registered in both states
    /// rather than only the active one, because the route answers the same shape either way.
    /// </para>
    /// <para>
    /// Scoped when it is active, because one open is one proposal: the ledger the call is charged to, the credential it
    /// resolves, and the transport it opens all belong to that request. The inactive one is a singleton holding nothing.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMailBodyCleanupAgent(this IServiceCollection services, bool isActivated)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<MailBodyCleaning>();

        if (!isActivated)
        {
            services.AddSingleton<IMailBodyCleaner>(InactiveMailBodyCleaner.Instance);

            return services;
        }

        services.TryAddSingleton<OpenAiCompatibleClientFactory>();
        services.TryAddSingleton<IAgentInstructionEnvelope, EmptyAgentInstructionEnvelope>();
        services.AddScoped<IMailBodyCleaner, MailBodyCleanupAgent>();

        return services;
    }

    /// <summary>Registers the two agents a Discover run reaches the model through: the derivation that reads one question into a plan, and the composition that turns what it found into a result.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Registered beside the answering agent and behind the same declaration, because all of them share the one
    /// dependency a supported deployment may not have. An instance that declared no chat endpoint registers none of
    /// them, which is what lets the run report that it derives nothing rather than fail to resolve.
    /// <para>
    /// The two are registered together rather than separately because a run needs both: a deployment holding a plan it
    /// cannot compose an answer from would refuse in the middle of a question rather than before it.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddDiscoveryRunAgents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<OpenAiCompatibleClientFactory>();
        services.TryAddSingleton<IAgentInstructionEnvelope, EmptyAgentInstructionEnvelope>();
        services.AddScoped<IDiscoveryRunPlanner, DiscoveryPlanningAgent>();
        services.AddScoped<IDiscoveryResultComposer, DiscoveryCompositionAgent>();
        // What a run tells the person who asked it about the endpoint that answered them. Registered here rather than
        // beside the plan because only a deployment declaring a chat endpoint has one to name, and scoped off the plan
        // rather than mapped once so an operator editing the published name is obeyed by the next run rather than by
        // the next restart. Neither the address nor the routed model name crosses this boundary.
        services.AddScoped(provider =>
        {
            var endpoint = provider.GetRequiredService<ChatGenerationPlan>().Endpoint;

            return new AnsweringEndpointIdentity(endpoint.Alias, endpoint.PublishedModelName);
        });

        return services;
    }

    /// <summary>Registers the agent that drafts a reply, and the use case a composer reaches it through.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="derivesStyleFromSentMail">Whether a draft's manner is derived from the answering account's own recent sent mail.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Called only where the deployment declared a chat endpoint and turned drafting on, so the use case's absence
    /// <em>is</em> the answer a composer gets: it offers the empty page somebody writes in rather than a button that
    /// fails. That is why nothing stands in for it when this is not called — a stand-in answering "nothing drafted"
    /// would have every caller wait on a provider-shaped call that a deployment with no provider can never make.
    /// </para>
    /// <para>
    /// The use case is registered here rather than beside the other mail reads, because it is the one of them that is
    /// meaningless without the writer this call registers: the two are one switch and are turned on together.
    /// </para>
    /// <para>
    /// Both are scoped, because one request is one drafting: the ledger the call is charged to, the credential it
    /// resolves, the transport it opens, and the scope its mail is read under all belong to that request.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddReplyDraftAgent(
        this IServiceCollection services,
        bool derivesStyleFromSentMail)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<OpenAiCompatibleClientFactory>();
        services.TryAddSingleton<IAgentInstructionEnvelope, EmptyAgentInstructionEnvelope>();
        services.AddScoped<IReplyDraftWriter, ReplyDraftAgent>();
        // Composed by hand rather than resolved, because one of its arguments is a decision an operator took rather
        // than a service the container holds, and passing it here keeps every bound a drafting reads in the use case
        // that answers for the spend.
        services.AddScoped(provider => new MailReplyDrafting(
            provider.GetRequiredService<IReplyDraftSourceReader>(),
            provider.GetRequiredService<IReplyDraftWriter>(),
            provider.GetRequiredService<MailboxScopeResolver>(),
            provider.GetRequiredService<SensitiveContentEgressGuard>(),
            provider.GetRequiredService<AccessAuthorization>(),
            provider.GetRequiredService<IMailUserLanguages>(),
            derivesStyleFromSentMail));

        return services;
    }

    /// <summary>Registers the agent that reads a typed sentence into the filters and criteria a search is made of.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, so registration reads as one expression.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Called only where the deployment declared a chat endpoint and left this on, so the port's absence <em>is</em> the
    /// answer a search screen gets: it offers the plain word search rather than a field promising a description over a
    /// deployment that cannot read one. That is why nothing stands in for the reader when this is not called — a
    /// stand-in answering "not read" would have every caller ask a provider-shaped question of a deployment that has no
    /// provider.
    /// </para>
    /// <para>
    /// Scoped, because one request is one sentence: the ledger the call is charged to, the credential it resolves, and
    /// the transport it opens all belong to that request.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMailSearchPhraseAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<OpenAiCompatibleClientFactory>();
        services.TryAddSingleton<IAgentInstructionEnvelope, EmptyAgentInstructionEnvelope>();
        services.AddScoped<IMailSearchPhraseReader, MailSearchPhraseAgent>();

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
