// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Configuration.Providers;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Providers;

/// <summary>Covers the rule that keeps two AI endpoints from answering to one name.</summary>
/// <remarks>
/// The alias is what a credential is resolved by, what a resilience circuit is keyed by, and what every log line naming
/// an endpoint carries. A collision would share all three, so a chat outage would open the circuit the embeddings are
/// served through — which is why the comparison is asserted rather than read.
/// </remarks>
public sealed class ProviderEndpointAliasesTests
{
    [Fact]
    public void FindReusedAlias_TwoSectionsDeclaringOneAlias_NamesIt()
    {
        // Act
        var reused = ProviderEndpointAliases.FindReusedAlias(
            EmbeddingsDeclaring("shared", "indexing"),
            ChatDeclaring("shared"));

        // Assert
        Assert.Equal("shared", reused);
    }

    /// <summary>The alias is matched without case everywhere else it is used, so a collision cannot hide behind spelling.</summary>
    [Theory]
    [InlineData("SHARED")]
    [InlineData("Shared")]
    [InlineData("  shared  ")]
    public void FindReusedAlias_AnAliasDifferingOnlyInCaseOrSpacing_StillCollides(string chatAlias)
    {
        // Act
        var reused = ProviderEndpointAliases.FindReusedAlias(
            EmbeddingsDeclaring("shared"),
            ChatDeclaring(chatAlias));

        // Assert
        Assert.Equal("shared", reused);
    }

    [Fact]
    public void FindReusedAlias_SectionsDeclaringDifferentAliases_FindsNothing()
    {
        // Act
        var reused = ProviderEndpointAliases.FindReusedAlias(
            EmbeddingsDeclaring("indexing"),
            ChatDeclaring("answering"));

        // Assert
        Assert.Null(reused);
    }

    /// <summary>An instance may declare either provider, both, or neither, so a missing section is a working deployment.</summary>
    [Fact]
    public void FindReusedAlias_WithOnlyOneSectionDeclared_FindsNothing()
    {
        // Act, Assert
        Assert.Null(ProviderEndpointAliases.FindReusedAlias(EmbeddingsDeclaring("indexing"), chat: null));
        Assert.Null(ProviderEndpointAliases.FindReusedAlias(embeddings: null, ChatDeclaring("answering")));
        Assert.Null(ProviderEndpointAliases.FindReusedAlias(embeddings: null, chat: null));
    }

    /// <summary>A chat section an operator left without an alias declares no endpoint, so it can collide with nothing.</summary>
    [Fact]
    public void FindReusedAlias_AChatSectionWithNoAlias_FindsNothing()
    {
        // Act
        var reused = ProviderEndpointAliases.FindReusedAlias(
            EmbeddingsDeclaring("indexing"),
            new ChatModelOptions { Model = "a-chat-model" });

        // Assert
        Assert.Null(reused);
    }

    /// <summary>The message reaches an operator's log, so it names the alias they wrote and what leaving it costs.</summary>
    [Fact]
    public void DescribeReusedAlias_TheCollision_NamesTheAliasAndWhyItMatters()
    {
        // Act
        var message = ProviderEndpointAliases.DescribeReusedAlias("shared");

        // Assert
        Assert.Contains("shared", message, StringComparison.Ordinal);
        Assert.Contains("circuit", message, StringComparison.Ordinal);
    }

    private static EmbeddingOptions EmbeddingsDeclaring(params string[] aliases)
    {
        var settings = new EmbeddingOptions();

        foreach (var alias in aliases)
        {
            settings.Endpoints.Add(new EmbeddingEndpointOptions { Alias = alias });
        }

        return settings;
    }

    private static ChatModelOptions ChatDeclaring(string alias) => new()
    {
        Alias = alias,
        Model = "a-chat-model",
    };
}
