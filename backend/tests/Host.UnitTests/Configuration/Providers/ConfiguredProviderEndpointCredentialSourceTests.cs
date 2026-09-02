// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.Providers;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Configuration.Providers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Providers;

/// <summary>Covers the lookup that lets one credential source serve both declared sections.</summary>
/// <remarks>
/// The alias is the whole of the key, which is what the deployment-wide uniqueness rule exists to make safe. These
/// tests establish that an alias reaches the endpoint that declared it whichever section that was, and that one nothing
/// declared is refused rather than resolved against the wrong block.
/// </remarks>
public sealed class ConfiguredProviderEndpointCredentialSourceTests
{
    [Fact]
    public async Task ResolveAsync_AnEmbeddingEndpointAlias_ResolvesTheKeyThatEndpointDeclared()
    {
        // Arrange
        var source = SourceOver(
            EmbeddingsDeclaring("indexing", "env:EMBEDDING_KEY"),
            ChatDeclaring("answering", "env:CHAT_KEY"),
            ("env:EMBEDDING_KEY", "the-embedding-key"),
            ("env:CHAT_KEY", "the-chat-key"));

        // Act
        using var credential = await source.ResolveAsync("indexing", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ProviderEndpointCredentialKind.ApiKey, credential.Kind);
        Assert.Equal("the-embedding-key", credential.ApiKey);
    }

    /// <summary>The chat section is one endpoint rather than a chain, and its alias reaches it through the same lookup.</summary>
    [Fact]
    public async Task ResolveAsync_TheChatEndpointAlias_ResolvesTheKeyItDeclared()
    {
        // Arrange
        var source = SourceOver(
            EmbeddingsDeclaring("indexing", "env:EMBEDDING_KEY"),
            ChatDeclaring("answering", "env:CHAT_KEY"),
            ("env:EMBEDDING_KEY", "the-embedding-key"),
            ("env:CHAT_KEY", "the-chat-key"));

        // Act
        using var credential = await source.ResolveAsync("answering", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the-chat-key", credential.ApiKey);
    }

    /// <summary>An operator writes the alias, so the lookup matches it the way configuration is read rather than byte for byte.</summary>
    [Fact]
    public async Task ResolveAsync_AnAliasDifferingInCase_StillNamesTheEndpoint()
    {
        // Arrange
        var source = SourceOver(
            new EmbeddingOptions(),
            ChatDeclaring("  Answering  ", "env:CHAT_KEY"),
            ("env:CHAT_KEY", "the-chat-key"));

        // Act
        using var credential = await source.ResolveAsync("answering", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the-chat-key", credential.ApiKey);
    }

    /// <summary>A deployment that declared no chat provider must not have its chat block read on the strength of a stray alias.</summary>
    [Fact]
    public async Task ResolveAsync_AnAliasNoSectionDeclared_IsRefused()
    {
        // Arrange
        var source = SourceOver(
            EmbeddingsDeclaring("indexing", "env:EMBEDDING_KEY"),
            new ChatModelOptions(),
            ("env:EMBEDDING_KEY", "the-embedding-key"));

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.ResolveAsync("answering", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResolveAsync_ABlankAlias_IsRefused()
    {
        // Arrange
        var source = SourceOver(new EmbeddingOptions(), new ChatModelOptions());

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            source.ResolveAsync("   ", TestContext.Current.CancellationToken));
    }

    /// <summary>The failure names the kind of secret and the endpoint alias, and never the reference's own target.</summary>
    [Fact]
    public async Task ResolveAsync_AReferenceThatCannotBeResolved_NamesTheEndpointAndNotTheTarget()
    {
        // Arrange
        var source = SourceOver(new EmbeddingOptions(), ChatDeclaring("answering", "env:MISSING_KEY"));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            source.ResolveAsync("answering", TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("answering", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("MISSING_KEY", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A model server the operator runs themselves asks for no credential, so the request presents one that carries
    /// nothing and no reference is resolved on its behalf — an endpoint with no key must not reach the resolver at all,
    /// because a resolver asked for nothing reports a failure and would take the endpoint out of service.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_AnEndpointNeedingNoCredential_ResolvesNothingAndPresentsNothing()
    {
        // Arrange
        var embeddings = new EmbeddingOptions();
        embeddings.Endpoints.Add(new EmbeddingEndpointOptions
        {
            Alias = "local-server",
            Provider = "self-hosted",
            Model = "an-embedding-model",
            Dimension = 4,
            Address = "http://model-server:8000/v1",
            Unauthenticated = true,
        });

        var source = SourceOver(embeddings, new ChatModelOptions());

        // Act
        using var credential = await source.ResolveAsync("local-server", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ProviderEndpointCredentialKind.Unauthenticated, credential.Kind);
        Assert.Null(credential.ApiKey);
        Assert.Null(credential.Entra);
    }

    /// <summary>The chat role reaches the same shape through the same lookup, so one role cannot resolve what the other does not.</summary>
    [Fact]
    public async Task ResolveAsync_AChatEndpointNeedingNoCredential_PresentsNothing()
    {
        // Arrange
        var chat = new ChatModelOptions
        {
            Alias = "answering",
            Model = "a-chat-model",
            Address = "http://model-server:8000/v1",
            Unauthenticated = true,
        };

        var source = SourceOver(new EmbeddingOptions(), chat);

        // Act
        using var credential = await source.ResolveAsync("answering", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ProviderEndpointCredentialKind.Unauthenticated, credential.Kind);
        Assert.Null(credential.ApiKey);
    }

    private static EmbeddingOptions EmbeddingsDeclaring(string alias, string secretReference)
    {
        var settings = new EmbeddingOptions();
        settings.Endpoints.Add(new EmbeddingEndpointOptions
        {
            Alias = alias,
            Provider = "openai",
            Model = "text-embedding-3-small",
            Dimension = 4,
            ApiKey = new ConfiguredSecret { SecretReference = secretReference },
        });

        return settings;
    }

    private static ChatModelOptions ChatDeclaring(string alias, string secretReference) => new()
    {
        Alias = alias,
        Model = "a-chat-model",
        ApiKey = new ConfiguredSecret { SecretReference = secretReference },
    };

    private static ConfiguredProviderEndpointCredentialSource SourceOver(
        EmbeddingOptions embeddings,
        ChatModelOptions chat,
        params (string Reference, string Material)[] resolvable)
    {
        var resolver = Substitute.For<ISecretReferenceResolver>();
        resolver
            .ResolveAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(ResolutionOf(call.Arg<string?>(), resolvable)));

        return new ConfiguredProviderEndpointCredentialSource(
            Options.Create(embeddings),
            new StubSettingsSnapshot<ChatModelOptions>(chat),
            resolver);
    }

    private static SecretResolutionResult ResolutionOf(
        string? reference,
        IReadOnlyList<(string Reference, string Material)> resolvable)
    {
        var match = resolvable.FirstOrDefault(entry => entry.Reference == reference);

        return match.Reference is null
            ? SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound)
            : SecretResolutionResult.Resolved(
                ResolvedSecret.FromText(match.Material),
                SecretMaterialSource.SchemeAdapter);
    }
}
