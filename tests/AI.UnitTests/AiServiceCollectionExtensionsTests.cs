// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using Microsoft.Extensions.DependencyInjection;
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
}
