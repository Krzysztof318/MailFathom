// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;
using MailMcp.Host.Hosting;
using MailMcp.Infrastructure.Secrets;
using MailMcp.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers what an operator is told when the endpoint is switched on without requiring a credential.</summary>
/// <remarks>
/// The warning is the whole of that posture's visibility, so its content is a contract rather than a courtesy: it has to
/// name what is absent and what to do about it, or reading it teaches an operator nothing they can act on. Its silence
/// is equally a contract — a warning that also fires for an authenticated deployment trains everyone to ignore it.
/// </remarks>
public sealed class McpTransportAuthenticationWarningTests
{
    [Fact]
    public async Task StartAsync_EnabledEndpointWithoutAuthentication_WarnsThatNothingIdentifiesTheCaller()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(UnauthenticatedServingOneBrowserOrigin(), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("read the synchronized mailboxes", record.Message, StringComparison.Ordinal);
        Assert.Contains("API keys", record.Message, StringComparison.Ordinal);
        Assert.Equal("/mcp", Assert.Contains("McpEndpointPath", record.Properties));
    }

    /// <summary>
    /// The combination that makes DNS rebinding work, and the one a deployment behind a reverse proxy or on a trusted
    /// network legitimately runs. It is therefore reported rather than refused, and reported separately: an operator who
    /// narrowed the origins has already answered this question and must not be told the same thing twice.
    /// </summary>
    [Fact]
    public async Task StartAsync_UnauthenticatedEndpointServingEveryBrowserOrigin_AlsoWarnsAboutDnsRebinding()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(UnauthenticatedServingEveryBrowserOrigin(), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, logs.Records.Count);
        Assert.All(logs.Records, record => Assert.Equal(LogLevel.Warning, record.Level));
        var browserWarning = Assert.Single(
            logs.Records,
            record => record.Message.Contains("DNS rebinding", StringComparison.Ordinal));
        Assert.Contains("McpEndpoint:Cors:AllowedOrigins", browserWarning.Message, StringComparison.Ordinal);
    }

    /// <summary>A deployment that requires a credential has the posture the warning exists to ask for, so repeating it would be noise.</summary>
    [Fact]
    public async Task StartAsync_EnabledEndpointRequiringAnApiKey_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = new McpEndpointOptions { Enabled = true, Authentication = McpTransportAuthenticationMode.ApiKey };
        settings.ApiKeys.Add(new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:a-key" });
        var warning = WarningFor(settings, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>A deployment that serves no endpoint has no posture to warn about, whatever mode it names.</summary>
    [Fact]
    public async Task StartAsync_DisabledEndpoint_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(
            new McpEndpointOptions { Authentication = McpTransportAuthenticationMode.None },
            logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>
    /// Naming no mode is a configuration failure the composition root refuses before this ever runs. The warning stays
    /// silent rather than treating the absence as the unauthenticated posture, so the two are never confused: one is an
    /// operator's decision, the other is a host that must not have started.
    /// </summary>
    [Fact]
    public async Task StartAsync_EnabledEndpointNamingNoMode_SaysNothingRatherThanAssumingTheUnauthenticatedPosture()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(new McpEndpointOptions { Enabled = true }, logs);

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
        var warning = WarningFor(
            new McpEndpointOptions { Enabled = true, Authentication = McpTransportAuthenticationMode.None },
            logs);

        // Act
        await warning.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>The unauthenticated posture with the origin question already answered, so only the credential warning is left to fire.</summary>
    private static McpEndpointOptions UnauthenticatedServingOneBrowserOrigin()
    {
        var settings = Unauthenticated();

        settings.Cors.AllowedOrigins.Add("https://client.example.test");

        return settings;
    }

    /// <summary>The unauthenticated posture a deployment that configured no origin list receives, which is what composition applies.</summary>
    private static McpEndpointOptions UnauthenticatedServingEveryBrowserOrigin()
    {
        var settings = Unauthenticated();

        settings.Cors.ServeEveryBrowserOrigin();

        return settings;
    }

    private static McpEndpointOptions Unauthenticated() => new()
    {
        Enabled = true,
        Authentication = McpTransportAuthenticationMode.None,
    };

    private static McpTransportAuthenticationWarning WarningFor(McpEndpointOptions settings, RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new McpTransportAuthenticationWarning(
            Options.Create(settings),
            loggerFactory.CreateLogger<McpTransportAuthenticationWarning>());
    }
}
