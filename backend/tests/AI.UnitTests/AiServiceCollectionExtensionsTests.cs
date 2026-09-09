// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Descriptions;
using MailFathom.AI.Embeddings;
using MailFathom.AI.Enrichment;
using MailFathom.AI.Orchestration;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.ThreadStates;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Discovery.Runs;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Enrichment;
using MailFathom.Application.Emails.Extraction.Images;
using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace MailFathom.AI.UnitTests;

public sealed class AiServiceCollectionExtensionsTests
{
    /// <summary>
    /// This method is the only place the chunker and its rules are wired in, and persistence resolves both while
    /// writing a message's passages. A registration dropped here would leave every composition root failing to resolve
    /// the chunk writer, and no other unit test builds a container from it — so the break would surface first in the
    /// integration suite, which runs only when somebody dispatches it.
    /// </summary>
    [Fact]
    public void AddLocalTextDerivations_OnAServiceCollection_ResolvesTheChunkerAndTheRulesItCutsTo()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddLocalTextDerivations();

        // Assert
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IEmailTextChunker>());
        Assert.Same(EmailChunkingRules.Current, provider.GetRequiredService<EmailChunkingRules>());
    }

    /// <summary>
    /// The chunker holds no state, so a second resolution must be the same instance rather than a second object built
    /// per scope: registering it per scope would allocate one for every message synchronization writes.
    /// </summary>
    [Fact]
    public void AddLocalTextDerivations_ResolvedTwice_HandsBackOneChunker()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLocalTextDerivations();

        // Act
        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IEmailTextChunker>();
        var second = provider.GetRequiredService<IEmailTextChunker>();

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>Nothing can be registered on a collection that is not there.</summary>
    [Fact]
    public void AddLocalTextDerivations_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => AiServiceCollectionExtensions.AddLocalTextDerivations(null!));
    }

    /// <summary>A deployment that registers this is embedding with a hash, and its profile row is where that is visible.</summary>
    [Fact]
    public void AddDeterministicTextEmbeddings_ResolvesAGeneratorOfTheDeclaredWidth()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddDeterministicTextEmbeddings(dimension: 64, inputCharacterLimit: 4000);

        // Assert
        using var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<ITextEmbeddingGenerator>();

        Assert.Equal(64, generator.Identity.Dimension);
        Assert.Equal(DeterministicTextEmbeddingGenerator.ProviderName, generator.Identity.Provider);
    }

    /// <summary>
    /// The adapter, its client construction, and the transport it sends over are wired in one place, and a caller
    /// supplies the plan and the credential source because binding configuration and resolving a secret reference both
    /// belong to the composition root.
    /// </summary>
    [Fact]
    public void AddEmbeddingProviderAdapter_ResolvesTheAdapterOverItsOwnTransport()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddLogging();
        services.AddSingleton(EmbeddingDeclarations.Plan());
        services.AddSingleton(Substitute.For<IProviderEndpointCredentialSource>());
        services.AddSingleton(Substitute.For<IOutboundOperationRunner>());
        services.AddSingleton(Substitute.For<IAiProviderHealthRecorder>());
        services.AddSingleton(SensitiveContentEgressGuards.Inactive());

        // Act
        services.AddEmbeddingProviderAdapter();

        // Assert
        using var provider = services.BuildServiceProvider();
        var generator = provider.GetRequiredService<ITextEmbeddingGenerator>();
        using var transport = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(ProviderTextEmbeddingGenerator.TransportName);

        Assert.Equal(EmbeddingDeclarations.Dimension, generator.Identity.Dimension);

        // The bounds live in the registration rather than at a call site, so a client asked for by name carries them.
        Assert.Equal(TimeSpan.FromSeconds(35), transport.Timeout);
    }

    [Fact]
    public void AddEmbeddingProviderAdapter_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => AiServiceCollectionExtensions.AddEmbeddingProviderAdapter(null!));
    }

    /// <summary>
    /// The chat adapter, its client construction, and the transport it sends over are wired in one place, and its
    /// transport carries bounds of its own rather than the embedding client's.
    /// </summary>
    [Fact]
    public void AddChatProviderAdapter_ResolvesTheAdapterOverItsOwnTransport()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddLogging();
        services.AddSingleton(ChatDeclarations.PlanSource());
        services.AddScoped(provider => provider.GetRequiredService<IChatGenerationPlanSource>().Current);
        services.AddSingleton(Substitute.For<IProviderEndpointCredentialSource>());
        services.AddSingleton(Substitute.For<IOutboundOperationRunner>());
        services.AddSingleton(Substitute.For<IAiProviderHealthRecorder>());
        services.AddSingleton(SensitiveContentEgressGuards.Inactive());

        // Act
        services.AddChatProviderAdapter();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IChatModelClient>();
        using var transport = provider
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(ProviderChatModelClient.TransportName);

        Assert.NotNull(client);

        // The bounds live in the registration rather than at a call site, so a client asked for by name carries them.
        // They are read from the plan source, because the factory builds a client on the root provider while the plan
        // itself belongs to an operation's scope.
        Assert.Equal(ChatDeclarations.RequestTimeout + TimeSpan.FromSeconds(30), transport.Timeout);
    }

    /// <summary>
    /// The port is registered whichever decision a deployment took, because a caller needs a reason it can record
    /// against the attachment rather than an absence it has to interpret. An instance that has not activated
    /// description resolves the describer that reads nothing.
    /// </summary>
    [Fact]
    public void AddImageAttachmentDescription_WithoutAGridCeiling_ResolvesTheDescriberThatSendsNothing()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddImageAttachmentDescription(maximumPixelCount: null);

        // Assert
        using var provider = services.BuildServiceProvider();

        Assert.IsType<InactiveImageAttachmentDescriber>(
            provider.GetRequiredService<IEmailAttachmentImageDescriber>());
    }

    /// <summary>Scoped where it is active, because it sends over the scoped chat client that a run's own scope resolves.</summary>
    [Fact]
    public void AddImageAttachmentDescription_WithAGridCeiling_ResolvesTheDescriberOncePerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => ChatDeclarations.Plan());
        services.AddScoped(_ => Substitute.For<IChatModelClient>());

        // Act
        services.AddImageAttachmentDescription(maximumPixelCount: 40_000_000);

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var describer = scope.ServiceProvider.GetRequiredService<IEmailAttachmentImageDescriber>();

        Assert.IsType<ImageAttachmentDescriber>(describer);
        Assert.Same(describer, scope.ServiceProvider.GetRequiredService<IEmailAttachmentImageDescriber>());

        using var second = provider.CreateScope();

        Assert.NotSame(describer, second.ServiceProvider.GetRequiredService<IEmailAttachmentImageDescriber>());
    }

    /// <summary>
    /// The port is registered whichever decision a deployment took, for the reason description's is: the pass needs a
    /// reason it can report and a message it can leave outstanding, rather than an absent service it would have to
    /// interpret. An instance that has not turned enrichment on resolves the enricher that reads nothing.
    /// </summary>
    [Fact]
    public void AddEmailEnrichmentAgent_NotActivated_ResolvesTheEnricherThatSendsNothing()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddEmailEnrichmentAgent(isActivated: false);

        // Assert
        using var provider = services.BuildServiceProvider();

        Assert.IsType<InactiveEmailEnricher>(provider.GetRequiredService<IEmailEnricher>());
    }

    /// <summary>Scoped where it is active, because each derivation opens its own credential, transport, and client.</summary>
    [Fact]
    public void AddEmailEnrichmentAgent_Activated_ResolvesTheAgentOncePerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => ChatDeclarations.Plan());
        services.AddScoped(_ => MailAnsweringRunBounds.Default);
        services.AddScoped(_ => Substitute.For<IMailAnsweringSpendLedger>());
        services.AddScoped(_ => Substitute.For<IProviderEndpointCredentialSource>());
        services.AddScoped(_ => Substitute.For<IHttpClientFactory>());
        services.AddScoped(_ => Substitute.For<IOutboundOperationRunner>());
        services.AddScoped(_ => Substitute.For<IAiProviderHealthRecorder>());
        services.AddScoped(_ => SensitiveContentEgressGuards.Inactive());

        // Act
        services.AddEmailEnrichmentAgent(isActivated: true);

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var enricher = scope.ServiceProvider.GetRequiredService<IEmailEnricher>();

        Assert.IsType<EmailEnrichmentAgent>(enricher);
        Assert.Same(enricher, scope.ServiceProvider.GetRequiredService<IEmailEnricher>());

        using var second = provider.CreateScope();

        Assert.NotSame(enricher, second.ServiceProvider.GetRequiredService<IEmailEnricher>());
    }

    [Fact]
    public void AddEmailEnrichmentAgent_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => AiServiceCollectionExtensions.AddEmailEnrichmentAgent(null!, isActivated: false));
    }

    /// <summary>
    /// The same arrangement one conversation's state is registered under, and for the same reason: the pass resolves a
    /// deriver whichever decision a deployment took, so an instance that never turned this on is told what activated
    /// nothing rather than meeting a service nobody registered.
    /// </summary>
    [Fact]
    public void AddThreadStateAgent_NotActivated_ResolvesTheDeriverThatSendsNothing()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddThreadStateAgent(isActivated: false);

        // Assert
        using var provider = services.BuildServiceProvider();

        Assert.IsType<InactiveThreadStateDeriver>(provider.GetRequiredService<IThreadStateDeriver>());
    }

    /// <summary>Scoped where it is active, because each derivation opens its own credential, transport, and client.</summary>
    [Fact]
    public void AddThreadStateAgent_Activated_ResolvesTheAgentOncePerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => ChatDeclarations.Plan());
        services.AddScoped(_ => MailAnsweringRunBounds.Default);
        services.AddScoped(_ => Substitute.For<IMailAnsweringSpendLedger>());
        services.AddScoped(_ => Substitute.For<IProviderEndpointCredentialSource>());
        services.AddScoped(_ => Substitute.For<IHttpClientFactory>());
        services.AddScoped(_ => Substitute.For<IOutboundOperationRunner>());
        services.AddScoped(_ => Substitute.For<IAiProviderHealthRecorder>());
        services.AddScoped(_ => SensitiveContentEgressGuards.Inactive());

        // Act
        services.AddThreadStateAgent(isActivated: true);

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var deriver = scope.ServiceProvider.GetRequiredService<IThreadStateDeriver>();

        Assert.IsType<ThreadStateAgent>(deriver);
        Assert.Same(deriver, scope.ServiceProvider.GetRequiredService<IThreadStateDeriver>());

        using var second = provider.CreateScope();

        Assert.NotSame(deriver, second.ServiceProvider.GetRequiredService<IThreadStateDeriver>());
    }

    [Fact]
    public void AddThreadStateAgent_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => AiServiceCollectionExtensions.AddThreadStateAgent(null!, isActivated: false));
    }

    /// <summary>A ceiling that admits no image would refuse every one of them while reading as a bound somebody chose.</summary>
    [Fact]
    public void AddImageAttachmentDescription_WithAGridCeilingThatIsNotPositive_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ServiceCollection().AddImageAttachmentDescription(maximumPixelCount: 0));
    }

    [Fact]
    public void AddImageAttachmentDescription_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => AiServiceCollectionExtensions.AddImageAttachmentDescription(null!, maximumPixelCount: null));
    }

    /// <summary>
    /// The declaration behind the plan reloads, so a client built once for the process would go on calling the model
    /// the process started with. One scope is one operation, and the client belongs to it.
    /// </summary>
    [Fact]
    public void AddChatProviderAdapter_ResolvesTheChatClientOncePerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddLogging();
        services.AddSingleton(ChatDeclarations.PlanSource());
        services.AddScoped(provider => provider.GetRequiredService<IChatGenerationPlanSource>().Current);
        services.AddSingleton(Substitute.For<IProviderEndpointCredentialSource>());
        services.AddSingleton(Substitute.For<IOutboundOperationRunner>());
        services.AddSingleton(Substitute.For<IAiProviderHealthRecorder>());
        services.AddSingleton(SensitiveContentEgressGuards.Inactive());

        // Act
        services.AddChatProviderAdapter();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();

        Assert.NotSame(
            scope.ServiceProvider.GetRequiredService<IChatModelClient>(),
            otherScope.ServiceProvider.GetRequiredService<IChatModelClient>());
    }

    /// <summary>
    /// Either adapter may be the only one a deployment registers, so neither may assume the other ran — including for
    /// the client construction the two share.
    /// </summary>
    [Fact]
    public void AddChatProviderAdapter_BesideTheEmbeddingAdapter_SharesOneClientFactory()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddLogging();
        services.AddSingleton(EmbeddingDeclarations.Plan());
        services.AddSingleton(ChatDeclarations.PlanSource());
        services.AddScoped(provider => provider.GetRequiredService<IChatGenerationPlanSource>().Current);
        services.AddSingleton(Substitute.For<IProviderEndpointCredentialSource>());
        services.AddSingleton(Substitute.For<IOutboundOperationRunner>());
        services.AddSingleton(Substitute.For<IAiProviderHealthRecorder>());
        services.AddSingleton(SensitiveContentEgressGuards.Inactive());

        // Act
        services.AddEmbeddingProviderAdapter();
        services.AddChatProviderAdapter();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(provider.GetRequiredService<ITextEmbeddingGenerator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IChatModelClient>());
        Assert.Single(services, service => service.ServiceType == typeof(OpenAiCompatibleClientFactory));
    }

    [Fact]
    public void AddChatProviderAdapter_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => AiServiceCollectionExtensions.AddChatProviderAdapter(null!));
    }

    /// <summary>
    /// A run retrieves through the mailbox search, which reads through the scoped persistence context, so the agent is
    /// scoped too: a singleton would capture one scope's reader and answer every later question through it.
    /// </summary>
    [Fact]
    public void AddMailAnsweringAgent_ResolvesTheAnsweringPortOncePerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddLogging();
        services.AddSingleton(ChatDeclarations.PlanSource());
        services.AddScoped(provider => provider.GetRequiredService<IChatGenerationPlanSource>().Current);
        services.AddSingleton(MailAnsweringRunBounds.Default);
        services.AddSingleton(Substitute.For<IProviderEndpointCredentialSource>());
        services.AddSingleton(Substitute.For<IOutboundOperationRunner>());
        services.AddSingleton(Substitute.For<IAiProviderHealthRecorder>());
        services.AddSingleton(SensitiveContentEgressGuards.Inactive());
        services.AddSingleton(Substitute.For<IMailAnsweringSpendLedger>());
        services.AddScoped<IEmailKnowledgeSearch, RecordingEmailKnowledgeSearch>();

        // Beside the adapter, as the composition root registers them: a run sends over the transport that call names.
        services.AddChatProviderAdapter();

        // Act
        services.AddMailAnsweringAgent();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();

        Assert.NotSame(
            scope.ServiceProvider.GetRequiredService<IMailQuestionAnswerer>(),
            otherScope.ServiceProvider.GetRequiredService<IMailQuestionAnswerer>());
    }

    /// <summary>
    /// The empty envelope is a default rather than a fixture: a deployment supplying a person's language registers its
    /// own before this runs and keeps it, which is the whole of what makes the seam worth having.
    /// </summary>
    [Fact]
    public void AddMailAnsweringAgent_WhereAnInstructionEnvelopeIsAlreadyRegistered_KeepsIt()
    {
        // Arrange
        var services = new ServiceCollection();
        var declared = Substitute.For<IAgentInstructionEnvelope>();
        services.AddSingleton(declared);

        // Act
        services.AddMailAnsweringAgent();

        // Assert
        using var provider = services.BuildServiceProvider();

        Assert.Same(declared, provider.GetRequiredService<IAgentInstructionEnvelope>());
    }

    [Fact]
    public void AddMailAnsweringAgent_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => AiServiceCollectionExtensions.AddMailAnsweringAgent(null!));
    }

    /// <summary>
    /// Scoped for the reason the answering agent is: one scope is one question, and the generation plan it derives with
    /// is read once per scope so a run stays on the plan it began with.
    /// </summary>
    [Fact]
    public void AddDiscoveryRunAgents_ResolvesThePlanningPortOncePerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddLogging();
        services.AddSingleton(ChatDeclarations.PlanSource());
        services.AddScoped(provider => provider.GetRequiredService<IChatGenerationPlanSource>().Current);
        services.AddSingleton(EmailKnowledgeBounds.Default);
        services.AddSingleton(Substitute.For<IProviderEndpointCredentialSource>());
        services.AddSingleton(Substitute.For<IOutboundOperationRunner>());
        services.AddSingleton(Substitute.For<IAiProviderHealthRecorder>());
        services.AddSingleton(SensitiveContentEgressGuards.Inactive());
        services.AddSingleton(Substitute.For<IMailAnsweringSpendLedger>());
        services.AddScoped(_ => new MailAnsweringRunLedger(MailAnsweringRunBounds.Default));

        // Beside the adapter, as the composition root registers them: a derivation sends over the transport that call names.
        services.AddChatProviderAdapter();

        // Act
        services.AddDiscoveryRunAgents();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();

        Assert.NotSame(
            scope.ServiceProvider.GetRequiredService<IDiscoveryRunPlanner>(),
            otherScope.ServiceProvider.GetRequiredService<IDiscoveryRunPlanner>());
    }

    /// <summary>Both halves of a run's chat configuration arrive together, so a deployment that derives a plan composes a result from it.</summary>
    [Fact]
    public void AddDiscoveryRunAgents_ADeploymentThatDerivesAPlan_AlsoResolvesTheCompositionOncePerScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddLogging();
        services.AddSingleton(ChatDeclarations.PlanSource());
        services.AddScoped(provider => provider.GetRequiredService<IChatGenerationPlanSource>().Current);
        services.AddSingleton(EmailKnowledgeBounds.Default);
        services.AddSingleton(Substitute.For<IProviderEndpointCredentialSource>());
        services.AddSingleton(Substitute.For<IOutboundOperationRunner>());
        services.AddSingleton(Substitute.For<IAiProviderHealthRecorder>());
        services.AddSingleton(SensitiveContentEgressGuards.Inactive());
        services.AddSingleton(Substitute.For<IMailAnsweringSpendLedger>());
        services.AddScoped(_ => new MailAnsweringRunLedger(MailAnsweringRunBounds.Default));
        services.AddChatProviderAdapter();

        // Act
        services.AddDiscoveryRunAgents();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();

        Assert.NotSame(
            scope.ServiceProvider.GetRequiredService<IDiscoveryResultComposer>(),
            otherScope.ServiceProvider.GetRequiredService<IDiscoveryResultComposer>());
    }

    /// <summary>What a run tells the person who asked which endpoint answered them, and never how it is reached.</summary>
    /// <remarks>
    /// The one mapping in this call that decides what leaves the deployment rather than which port resolves: a swap of
    /// the two names, or a reading of the routed name in place of the published one, would compile and would publish an
    /// operator's own resource name to every client.
    /// </remarks>
    [Fact]
    public void AddDiscoveryRunAgents_ADeclaredChatEndpoint_ResolvesTheNamesARunPublishesAndNeitherOfTheOthers()
    {
        // Arrange
        var declared = ChatDeclarations.Endpoint(
            alias: "answering",
            routedModelName: "prod-eu-4o-2",
            publishedModelName: "gpt-4o");
        var services = new ServiceCollection();
        services.AddHttpClient();
        services.AddLogging();
        services.AddSingleton(ChatDeclarations.PlanSource(ChatDeclarations.Plan(declared)));
        services.AddScoped(provider => provider.GetRequiredService<IChatGenerationPlanSource>().Current);
        services.AddSingleton(EmailKnowledgeBounds.Default);
        services.AddSingleton(Substitute.For<IProviderEndpointCredentialSource>());
        services.AddSingleton(Substitute.For<IOutboundOperationRunner>());
        services.AddSingleton(Substitute.For<IAiProviderHealthRecorder>());
        services.AddSingleton(SensitiveContentEgressGuards.Inactive());
        services.AddSingleton(Substitute.For<IMailAnsweringSpendLedger>());
        services.AddScoped(_ => new MailAnsweringRunLedger(MailAnsweringRunBounds.Default));
        services.AddChatProviderAdapter();

        // Act
        services.AddDiscoveryRunAgents();

        // Assert
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var identity = scope.ServiceProvider.GetRequiredService<AnsweringEndpointIdentity>();
        Assert.Equal(declared.Alias, identity.Alias);
        Assert.Equal(declared.PublishedModelName, identity.PublishedModel);
        Assert.DoesNotContain(declared.RoutedModelName, $"{identity.Alias} {identity.PublishedModel}", StringComparison.Ordinal);
    }

    /// <summary>The envelope is a seam a deployment fills, so the planner keeps one that is already registered.</summary>
    [Fact]
    public void AddDiscoveryRunAgents_WhereAnInstructionEnvelopeIsAlreadyRegistered_KeepsIt()
    {
        // Arrange
        var services = new ServiceCollection();
        var declared = Substitute.For<IAgentInstructionEnvelope>();
        services.AddSingleton(declared);

        // Act
        services.AddDiscoveryRunAgents();

        // Assert
        using var provider = services.BuildServiceProvider();

        Assert.Same(declared, provider.GetRequiredService<IAgentInstructionEnvelope>());
    }

    [Fact]
    public void AddDiscoveryRunAgents_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => AiServiceCollectionExtensions.AddDiscoveryRunAgents(null!));
    }

    /// <summary>
    /// The pass decorates the retrieval rather than replacing it, so it has to be the last registration of the port and
    /// has to share the scope the retrieval it wraps reads through. Asserted against the descriptor rather than by
    /// resolving it, because the wrapped retrieval reaches a search reader that opens a database this suite has none of.
    /// </summary>
    [Fact]
    public void AddModelJudgedRetrieval_OnAServiceCollection_TakesOverTheRetrievalPortForTheScope()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddScoped<IEmailKnowledgeSearch, RecordingEmailKnowledgeSearch>();

        // Act
        services.AddModelJudgedRetrieval();

        // Assert
        ServiceDescriptor[] registered =
        [
            .. services.Where(descriptor => descriptor.ServiceType == typeof(IEmailKnowledgeSearch)),
        ];

        Assert.Equal(2, registered.Length);
        Assert.Equal(ServiceLifetime.Scoped, registered[^1].Lifetime);
        Assert.NotNull(registered[^1].ImplementationFactory);
    }

    [Fact]
    public void AddModelJudgedRetrieval_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => AiServiceCollectionExtensions.AddModelJudgedRetrieval(null!));
    }

    [Fact]
    public void AddDeterministicTextEmbeddings_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => AiServiceCollectionExtensions.AddDeterministicTextEmbeddings(null!, 64, 4000));
    }
}
