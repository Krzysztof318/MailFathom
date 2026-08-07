// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Embeddings;
using MailFathom.AI.ProviderAdapters;
using MailFathom.AI.UnitTests.TestDoubles;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Resilience;
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
        services.AddSingleton(Substitute.For<IEmbeddingCredentialSource>());
        services.AddSingleton(Substitute.For<IOutboundOperationRunner>());

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

    [Fact]
    public void AddDeterministicTextEmbeddings_WithoutAServiceCollection_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(
            () => AiServiceCollectionExtensions.AddDeterministicTextEmbeddings(null!, 64, 4000));
    }
}
