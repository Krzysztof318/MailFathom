// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Configuration;
using MailMcp.Host.Hosting;
using MailMcp.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailMcp.Host.UnitTests;

/// <summary>Covers what an operator is told when the endpoint is switched on before authentication exists.</summary>
/// <remarks>
/// The warning is the whole of the interim posture's visibility, so its content is a contract rather than a courtesy: it
/// has to name the controls that are absent and the work that adds them, or reading it teaches an operator nothing they
/// can act on.
/// </remarks>
public sealed class McpTransportAuthenticationWarningTests
{
    [Fact]
    public async Task StartAsync_EnabledEndpoint_WarnsThatNothingAuthenticatesTheTransport()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(new McpEndpointOptions { Enabled = true }, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("OAuth 2.1", record.Message, StringComparison.Ordinal);
        Assert.Contains("mutual TLS", record.Message, StringComparison.Ordinal);
        Assert.Contains("stage 9", record.Message, StringComparison.Ordinal);
        Assert.Equal("/mcp", Assert.Contains("McpEndpointPath", record.Properties));
    }

    /// <summary>A deployment that serves no endpoint has no posture to warn about, and a warning it cannot act on trains it to ignore warnings.</summary>
    [Fact]
    public async Task StartAsync_DisabledEndpoint_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(new McpEndpointOptions(), logs);

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
        var warning = WarningFor(new McpEndpointOptions { Enabled = true }, logs);

        // Act
        await warning.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    private static McpTransportAuthenticationWarning WarningFor(McpEndpointOptions settings, RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new McpTransportAuthenticationWarning(
            Options.Create(settings),
            loggerFactory.CreateLogger<McpTransportAuthenticationWarning>());
    }
}
