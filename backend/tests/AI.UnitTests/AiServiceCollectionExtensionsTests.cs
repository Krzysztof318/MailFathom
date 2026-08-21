// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Chat;
using MailFathom.AI.Embeddings;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.Providers;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Chat;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
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

    [Fact]
    public void AddMailAnsweringAgent_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => AiServiceCollectionExtensions.AddMailAnsweringAgent(null!));
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
