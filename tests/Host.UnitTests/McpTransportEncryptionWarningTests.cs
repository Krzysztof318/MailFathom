// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Host.Hosting;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers what an operator is told when the endpoint is served without MailFathom terminating any TLS.</summary>
/// <remarks>
/// Clear text is a supported posture, so this reports rather than refuses — and its silence is as much a contract as
/// its text. A warning that also fired for a deployment presenting its own certificate would be one more line an
/// operator learns to scroll past.
/// </remarks>
public sealed class McpTransportEncryptionWarningTests
{
    [Fact]
    public async Task StartAsync_EnabledEndpointTerminatingNoTls_WarnsThatTheTransportIsClearText()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(Enabled(), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("clear text", record.Message, StringComparison.Ordinal);
        Assert.Contains("client certificate never arrives", record.Message, StringComparison.Ordinal);
        Assert.Contains("reverse proxy", record.Message, StringComparison.Ordinal);
        Assert.Contains("McpEndpoint:Https:Endpoints", record.Message, StringComparison.Ordinal);
        Assert.Equal("/mcp", Assert.Contains("McpEndpointPath", record.Properties));
    }

    /// <summary>An API key travels in a header, so the credential is as readable on that hop as the mail is.</summary>
    [Fact]
    public async Task StartAsync_AuthenticatedEndpointTerminatingNoTls_StillWarns()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = Enabled();
        settings.Authentication = McpTransportAuthenticationMethods.ApiKey;
        settings.ApiKeys.Add(new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:a-key" });
        var warning = WarningFor(settings, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(logs.Records);
    }

    [Fact]
    public async Task StartAsync_EndpointServingItsOwnCertificate_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = Enabled();
        settings.Https.Endpoints.Add(new McpHttpsEndpointOptions
        {
            Name = "public",
            Domain = "mail.example.test",
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret { Name = "bundle", SecretReference = "file:/etc/mailfathom/tls/bundle.pfx" },
            },
        });
        var warning = WarningFor(settings, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>A deployment that serves no endpoint exposes nothing over any transport.</summary>
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
        var warning = WarningFor(Enabled(), logs);

        // Act
        await warning.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    private static McpEndpointOptions Enabled() => new()
    {
        Enabled = true,
        Authentication = McpTransportAuthenticationMethods.None,
    };

    private static McpTransportEncryptionWarning WarningFor(McpEndpointOptions settings, RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new McpTransportEncryptionWarning(
            Options.Create(settings),
            loggerFactory.CreateLogger<McpTransportEncryptionWarning>());
    }
}
