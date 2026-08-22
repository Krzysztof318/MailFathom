// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers what an operator is told about the clear-text port a redirect opens on their behalf.</summary>
/// <remarks>
/// This is the one socket a deployment can end up listening on without having written a port, so it is the one an
/// operator auditing the process cannot otherwise account for. Its silence matters as much as its text: a line for a
/// deployment that opened no such port would be noise, and a line naming a port nothing bound would send somebody looking
/// for a listener that is not there.
/// </remarks>
public sealed class TransportClearTextRedirectReportTests
{
    [Fact]
    public async Task StartAsync_AnMcpEndpointRedirecting_NamesThePortAndTheDomainsItRedirectsTo()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(McpTerminatingTls(), new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal(8080, Assert.Contains("ClearTextPort", record.Properties));
        Assert.Equal("https://mail.example.test:8443", Assert.Contains("RedirectTargets", record.Properties));
        Assert.Contains("maps no route", record.Message, StringComparison.Ordinal);
        Assert.Contains("McpEndpoint:Https:Redirect:Enabled", record.Message, StringComparison.Ordinal);
    }

    /// <summary>Every domain, so an operator reads the whole set of names the one clear-text port answers for.</summary>
    [Fact]
    public async Task StartAsync_SeveralProfiles_NamesEveryDomainBesideItsOwnPort()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = McpTerminatingTls();
        settings.Https.Endpoints.Add(Profile(name: "managed", domain: "managed.example.test", port: 9443));
        var report = ReportFor(settings, new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(
            "https://mail.example.test:8443, https://managed.example.test:9443",
            Assert.Contains("RedirectTargets", record.Properties));
    }

    /// <summary>Each surface opens its own socket, so each is accounted for separately.</summary>
    [Fact]
    public async Task StartAsync_BothSurfacesRedirecting_ReportsOneLinePerSurface()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(McpTerminatingTls(), AdminTerminatingTls(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, logs.Records.Count);
        Assert.Contains(logs.Records, record => record.Message.Contains("administrative endpoint", StringComparison.Ordinal));
        Assert.Contains(logs.Records, record => record.Message.Contains("MCP endpoint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StartAsync_AnAdministrativeEndpointRedirecting_NamesItsOwnClearTextPort()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new McpEndpointOptions(), AdminTerminatingTls(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(8080, Assert.Contains("ClearTextPort", record.Properties));
        Assert.Contains("AdminEndpoint:Https:Redirect:Enabled", record.Message, StringComparison.Ordinal);
    }

    /// <summary>Nothing was bound, so naming a port would send an operator looking for a listener that is not there.</summary>
    [Fact]
    public async Task StartAsync_ARedirectTurnedOff_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = McpTerminatingTls();
        settings.Https.Redirect.Enabled = false;
        var report = ReportFor(settings, new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>The clear-text posture has its own warning, and it is not this one; nothing here fires for a surface that terminates no TLS.</summary>
    [Fact]
    public async Task StartAsync_ASurfaceTerminatingNoTls_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new McpEndpointOptions { Enabled = true }, new AdminEndpointOptions { Enabled = true }, logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>A surface nobody enabled opens no socket, whatever its profiles say.</summary>
    [Fact]
    public async Task StartAsync_ADisabledSurfaceWithProfiles_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = McpTerminatingTls();
        settings.Enabled = false;
        var report = ReportFor(settings, new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task StopAsync_AnyPosture_CompletesWithoutSayingAnythingFurther()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(McpTerminatingTls(), AdminTerminatingTls(), logs);

        // Act
        await report.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    private static McpEndpointOptions McpTerminatingTls()
    {
        var settings = new McpEndpointOptions { Enabled = true, Transport = EndpointTransport.HttpAndHttps };
        settings.Https.Endpoints.Add(Profile());

        return settings;
    }

    private static AdminEndpointOptions AdminTerminatingTls()
    {
        var settings = new AdminEndpointOptions { Enabled = true, Transport = EndpointTransport.HttpAndHttps };
        settings.Https.Endpoints.Add(Profile(domain: "admin.example.test", port: 8543));

        return settings;
    }

    private static TransportHttpsEndpointOptions Profile(
        string name = "public",
        string domain = "mail.example.test",
        int port = 8443) => new()
        {
            Name = name,
            Domain = domain,
            Port = port,
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret { Name = "bundle", SecretReference = "file:/etc/mailfathom/tls/bundle.pfx" },
            },
        };

    private static TransportClearTextRedirectReport ReportFor(
        McpEndpointOptions mcpEndpointSettings,
        AdminEndpointOptions adminEndpointSettings,
        RecordingLoggerProvider logs,
        ClientEndpointOptions? clientEndpointSettings = null)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new TransportClearTextRedirectReport(
            Options.Create(mcpEndpointSettings),
            Options.Create(adminEndpointSettings),
            Options.Create(clientEndpointSettings ?? new ClientEndpointOptions()),
            loggerFactory.CreateLogger<TransportClearTextRedirectReport>());
    }
}
