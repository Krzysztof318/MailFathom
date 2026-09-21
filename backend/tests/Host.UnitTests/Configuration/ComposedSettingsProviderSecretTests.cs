// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Host.Configuration.Answering;
using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace MailFathom.Host.UnitTests.Configuration;

/// <summary>
/// Proves that the two provider sections are judged on their secret declarations at startup, and by the same rules
/// every other section is. Neither section's references are resolved while the host starts — an optional capability
/// must not take a deployment offline over a credential nothing has asked for yet — and a name, a lifetime, and the
/// block shape need nothing resolved, so leaving them to travel with the resolution is what left a duplicated name
/// inside <c>Chat</c> refused by every reload and accepted by every start.
/// </summary>
public sealed class ComposedSettingsProviderSecretTests
{
    private const string RepeatedSecretName = "provider-key";

    private const string SharedKeyReference = "plaintext:the-answering-key";

    /// <summary>The reload path and the start now read one rule, so the message an operator meets is the same sentence.</summary>
    [Fact]
    public async Task FindProviderRefusals_ADuplicatedChatSecretName_IsRefusedWithTheMessageAReloadGives()
    {
        // Arrange
        var configuration = Configuration(ChatDeclaring(RepeatedSecretName, RepeatedSecretName));
        var declared = configuration.GetSection(ChatModelOptions.SectionName).Get<ChatModelOptions>()!;

        // Act
        var refusals = ComposedSettings.FindProviderRefusals(configuration);
        var reloadErrors = await ReloadValidatorOver(declared)
            .FindConfigurationErrorsAsync(declared, TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.Single(refusals);
        Assert.Equal(ChatModelOptions.SectionName, refusal.SectionName);
        var reloadError = Assert.Single(
            reloadErrors,
            error => error.StartsWith("Chat:Models:1:ApiKey:Name — ", StringComparison.Ordinal));
        Assert.Contains(reloadError, refusal.Errors, StringComparer.Ordinal);
    }

    /// <summary>The embedding chain is the section nothing walked at all: it neither reloads nor resolves while the host starts.</summary>
    [Fact]
    public void FindProviderRefusals_ADuplicatedEmbeddingSecretName_IsRefused()
    {
        // Arrange
        var configuration = Configuration(EmbeddingsDeclaring(RepeatedSecretName, RepeatedSecretName));

        // Act
        var refusals = ComposedSettings.FindProviderRefusals(configuration);

        // Assert
        var refusal = Assert.Single(refusals);
        Assert.Equal(EmbeddingOptions.SectionName, refusal.SectionName);
        Assert.Contains(
            refusal.Errors,
            error => error.StartsWith("Embeddings:Endpoints:1:ApiKey:Name — ", StringComparison.Ordinal));
    }

    /// <summary>
    /// Two models against one gateway share one key, and a name identifies that credential. Refusing the pair would
    /// make an operator name one file twice, which is the ambiguity the rule exists to prevent rather than a case of it.
    /// </summary>
    [Fact]
    public void FindProviderRefusals_TwoChatModelsDeclaringOneKeyIdentically_AreNotRefused()
    {
        // Arrange
        var configuration = Configuration(
            ChatDeclaring(RepeatedSecretName, RepeatedSecretName, secondKeyReference: SharedKeyReference));

        // Act
        var refusals = ComposedSettings.FindProviderRefusals(configuration);

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>Uniqueness stops at the section boundary, so one name in each section is two secrets rather than a collision.</summary>
    [Fact]
    public void FindProviderRefusals_ProviderSectionsNamingOneSecretEach_AreNotRefused()
    {
        // Arrange
        var keys = ChatDeclaring(RepeatedSecretName, "a-second-chat-key")
            .Concat(EmbeddingsDeclaring(RepeatedSecretName, "a-second-embedding-key"))
            .ToDictionary(StringComparer.Ordinal);

        // Act
        var refusals = ComposedSettings.FindProviderRefusals(Configuration(keys));

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>A deployment declaring neither provider declares no secret either, which is a supported shape rather than a refusal.</summary>
    [Fact]
    public void FindProviderRefusals_AConfigurationDeclaringNeitherProvider_IsNotRefused()
    {
        // Act
        var refusals = ComposedSettings.FindProviderRefusals(
            Configuration(new Dictionary<string, string?>(StringComparer.Ordinal)));

        // Assert
        Assert.Empty(refusals);
    }

    /// <summary>Two declared models, whose keys differ in everything unless the caller asks for one key declared twice.</summary>
    private static Dictionary<string, string?> ChatDeclaring(
        string firstKeyName,
        string secondKeyName,
        string secondKeyReference = "plaintext:the-cheap-key") =>
        new(StringComparer.Ordinal)
        {
            ["Chat:Models:0:Alias"] = "answering",
            ["Chat:Models:0:Model"] = "a-chat-model",
            ["Chat:Models:0:Address"] = "https://provider.invalid/v1/",
            ["Chat:Models:0:ApiKey:Name"] = firstKeyName,
            ["Chat:Models:0:ApiKey:SecretReference"] = SharedKeyReference,
            ["Chat:Models:1:Alias"] = "cheap",
            ["Chat:Models:1:Model"] = "a-small-fast-model",
            ["Chat:Models:1:Address"] = "https://provider.invalid/v1/",
            ["Chat:Models:1:ApiKey:Name"] = secondKeyName,
            ["Chat:Models:1:ApiKey:SecretReference"] = secondKeyReference,
            ["Chat:MainModel:Alias"] = "answering",
        };

    private static Dictionary<string, string?> EmbeddingsDeclaring(string firstKeyName, string secondKeyName) =>
        new(StringComparer.Ordinal)
        {
            ["Embeddings:Endpoints:0:Alias"] = "indexing",
            ["Embeddings:Endpoints:0:Model"] = "an-embedding-model",
            ["Embeddings:Endpoints:0:Dimension"] = "4",
            ["Embeddings:Endpoints:0:ApiKey:Name"] = firstKeyName,
            ["Embeddings:Endpoints:0:ApiKey:SecretReference"] = "plaintext:the-indexing-key",
            ["Embeddings:Endpoints:1:Alias"] = "standby",
            ["Embeddings:Endpoints:1:Model"] = "an-embedding-model",
            ["Embeddings:Endpoints:1:Dimension"] = "4",
            ["Embeddings:Endpoints:1:ApiKey:Name"] = secondKeyName,
            ["Embeddings:Endpoints:1:ApiKey:SecretReference"] = "plaintext:the-standby-key",
        };

    private static IConfiguration Configuration(Dictionary<string, string?> keys) =>
        new ConfigurationBuilder().AddInMemoryCollection(keys).Build();

    private static ChatSettingsReloadValidator ReloadValidatorOver(ChatModelOptions composed) => new(
        SecretValidation.OverRegisteredSchemes(),
        composed,
        declaredEmbeddings: null,
        new MailAnsweringOptions());
}
