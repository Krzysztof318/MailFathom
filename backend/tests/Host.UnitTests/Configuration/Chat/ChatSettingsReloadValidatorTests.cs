// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Answering;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Configuration.Persistence;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Secrets.Discovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration.Chat;

/// <summary>Covers what a reloaded chat declaration has to prove before it becomes the one new questions run through.</summary>
/// <remarks>
/// Everything reported here leaves the previous declaration serving, which is the behavior that makes the section
/// reloadable at all: an operator correcting a model has to be able to correct a mistake in the correction.
/// </remarks>
public sealed class ChatSettingsReloadValidatorTests
{
    [Fact]
    public async Task FindConfigurationErrorsAsync_ACorrectedModel_IsAdoptable()
    {
        // Arrange
        var candidate = Declared();
        candidate.Model = "a-corrected-model";

        // Act
        var errors = await ValidatorOver(Declared())
            .FindConfigurationErrorsAsync(candidate, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(errors);
    }

    /// <summary>The credential is proven here and resolved again per request, so a reference repointed to nothing is refused rather than published.</summary>
    [Fact]
    public async Task FindConfigurationErrorsAsync_AKeyReferenceThatResolvesToNothing_IsRefused()
    {
        // Arrange
        var candidate = Declared();
        candidate.ApiKey = new ConfiguredSecret { Name = "chat-key", SecretReference = "env:NOTHING_PROVISIONS_THIS" };

        // Act
        var errors = await ValidatorOver(Declared())
            .FindConfigurationErrorsAsync(candidate, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.Contains("Chat:ApiKey", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindConfigurationErrorsAsync_ABoundOutsideItsRange_IsRefused()
    {
        // Arrange
        var candidate = Declared();
        candidate.MaxMessagesPerRequest = 0;

        // Act
        var errors = await ValidatorOver(Declared())
            .FindConfigurationErrorsAsync(candidate, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.StartsWith("Chat:MaxMessagesPerRequest — ", StringComparison.Ordinal));
    }

    /// <summary>Which services exist was decided while the host composed itself, so removing the endpoint is a restart rather than a reload.</summary>
    [Fact]
    public async Task FindConfigurationErrorsAsync_ARemovedEndpoint_IsRefusedAsNeedingARestart()
    {
        // Act
        var errors = await ValidatorOver(Declared())
            .FindConfigurationErrorsAsync(new ChatModelOptions(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.Contains("needs a restart", StringComparison.Ordinal));
    }

    /// <summary>The alias uniqueness rule spans two sections and is enforced on every reload, not only at startup.</summary>
    [Fact]
    public async Task FindConfigurationErrorsAsync_AnAliasRenamedOntoAnEmbeddingEndpoint_IsRefused()
    {
        // Arrange
        var embeddings = new EmbeddingOptions();
        embeddings.Endpoints.Add(new EmbeddingEndpointOptions
        {
            Alias = "indexing",
            Model = "an-embedding-model",
            Dimension = 4,
        });

        var candidate = Declared();
        candidate.Alias = "indexing";

        // Act
        var errors = await ValidatorOver(Declared(), embeddings)
            .FindConfigurationErrorsAsync(candidate, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(errors, error => error.Contains("both declare the alias 'indexing'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FindConfigurationErrorsAsync_WithoutACandidate_IsRefused()
    {
        // Arrange
        var validator = ValidatorOver(Declared());

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => validator.FindConfigurationErrorsAsync(null!, TestContext.Current.CancellationToken));
    }

    private static ChatSettingsReloadValidator ValidatorOver(
        ChatModelOptions composed,
        EmbeddingOptions? embeddings = null) =>
        new(
            SecretValidator(),
            composed,
            embeddings,
            new MailAnsweringOptions());

    private static SecretConfigurationValidator SecretValidator()
    {
        var resolver = new PlaintextOnlySecretReferenceResolver();

        return new SecretConfigurationValidator(
            resolver,
            new TrustAnchorLoader(resolver),
            new DatabaseConnectionSettingsMapper(new ConfigurationBuilder().Build()),
            new StubDatabaseConnectionSettingsValidator(),
            PostgresTextSearchConfiguration.Default,
            new DatabaseCommandTimeout(TimeSpan.FromSeconds(HostApplicationBuilderExtensions.DefaultDatabaseCommandTimeoutSeconds)),
            new FakeTimeProvider(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero)),
            new RecordingLogger<SecretConfigurationValidator>());
    }

    private static ChatModelOptions Declared() => new()
    {
        Alias = "answering",
        Model = "a-chat-model",
        ApiKey = new ConfiguredSecret { Name = "chat-key", SecretReference = "plaintext:the-chat-key" },
    };
}
