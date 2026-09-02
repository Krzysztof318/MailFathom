// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Chat;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers what an operator is told about an AI endpoint this deployment reaches over an unencrypted hop.</summary>
/// <remarks>
/// Reaching a model server the operator runs themselves over a plain address is a supported posture, so this reports
/// rather than refuses — and its silence matters as much as its text, because a line that also fired for every ordinary
/// vendor endpoint is one an operator learns to scroll past.
/// </remarks>
public sealed class AiProviderTransportEncryptionWarningTests
{
    [Fact]
    public async Task StartAsync_AnEmbeddingEndpointOnAPlainAddress_NamesItAndWhatCrossesTheHop()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningOver(EmbeddingsReachedAt(("local-server", "http://model-server:8000/v1")), new ChatModelOptions(), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("plain http address", record.Message, StringComparison.Ordinal);
        Assert.Contains("passage", record.Message, StringComparison.Ordinal);
        Assert.Equal("local-server", Assert.Contains("EndpointAlias", record.Properties));
    }

    /// <summary>The chat hop carries the question and the answer beside the mail, so it says so rather than reusing the other line.</summary>
    [Fact]
    public async Task StartAsync_AChatEndpointOnAPlainAddress_NamesTheQuestionAndTheAnswerToo()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningOver(new EmbeddingOptions(), ChatReachedAt("answering", "http://127.0.0.1:11434/v1"), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("the question asked", record.Message, StringComparison.Ordinal);
        Assert.Contains("the answer it returns", record.Message, StringComparison.Ordinal);
        Assert.Equal("answering", Assert.Contains("EndpointAlias", record.Properties));
    }

    /// <summary>A chain is reported endpoint by endpoint, because a fallback reached in the clear is a hop of its own.</summary>
    [Fact]
    public async Task StartAsync_SeveralEndpointsOnPlainAddresses_ReportsEachOne()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var embeddings = EmbeddingsReachedAt(
            ("primary", "https://provider.invalid/v1/"),
            ("fallback", "http://model-server:8000/v1"));
        var warning = WarningOver(embeddings, ChatReachedAt("answering", "http://model-server:8000/v1"), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["fallback", "answering"],
            logs.Records.Select(record => Assert.Contains("EndpointAlias", record.Properties)));
    }

    /// <summary>
    /// An address is a host name and a port and says nothing about whose network it is on, so no log line may carry one:
    /// the alias is MailFathom's own name for the endpoint and is the whole of what identifies it here.
    /// </summary>
    [Fact]
    public async Task StartAsync_AnEndpointOnAPlainAddress_NeverWritesTheAddressDown()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningOver(
            EmbeddingsReachedAt(("local-server", "http://model-server.internal:8000/v1")),
            new ChatModelOptions(),
            logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.DoesNotContain("model-server.internal", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(record.Properties, property => property.Value is string value && value.Contains("8000", StringComparison.Ordinal));
    }

    /// <summary>An https endpoint and one left at the provider library's own default are both encrypted, and neither is worth a line.</summary>
    [Theory]
    [InlineData("https://provider.invalid/v1/")]
    [InlineData("")]
    public async Task StartAsync_EndpointsReachedOverAnEncryptedHop_SayNothing(string address)
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningOver(EmbeddingsReachedAt(("primary", address)), ChatReachedAt("answering", address), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>A chat section carrying an address but no alias declares no endpoint, so there is no hop to describe.</summary>
    [Fact]
    public async Task StartAsync_AChatSectionDeclaringNoEndpoint_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var chat = new ChatModelOptions { Address = "http://model-server:8000/v1" };
        var warning = WarningOver(new EmbeddingOptions(), chat, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task StopAsync_AnyPosture_CompletesWithoutSayingAnythingFurther()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningOver(
            EmbeddingsReachedAt(("local-server", "http://model-server:8000/v1")),
            new ChatModelOptions(),
            logs);

        // Act
        await warning.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    private static EmbeddingOptions EmbeddingsReachedAt(params (string Alias, string Address)[] endpoints)
    {
        var settings = new EmbeddingOptions();

        foreach (var endpoint in endpoints)
        {
            settings.Endpoints.Add(new EmbeddingEndpointOptions
            {
                Alias = endpoint.Alias,
                Provider = "self-hosted",
                Model = "an-embedding-model",
                Dimension = 4,
                Address = endpoint.Address,
                Unauthenticated = true,
            });
        }

        return settings;
    }

    private static ChatModelOptions ChatReachedAt(string alias, string address) => new()
    {
        Alias = alias,
        Model = "a-chat-model",
        Address = address,
        Unauthenticated = true,
    };

    private static AiProviderTransportEncryptionWarning WarningOver(
        EmbeddingOptions embeddings,
        ChatModelOptions chat,
        RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new AiProviderTransportEncryptionWarning(
            Options.Create(embeddings),
            new StubSettingsSnapshot<ChatModelOptions>(chat),
            loggerFactory.CreateLogger<AiProviderTransportEncryptionWarning>());
    }
}
