// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

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
        settings.Authentication.Add(ConfiguredAuthentication.Accepting(OwnerCredentialMethod.ApiKey));
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
        settings.Transport = EndpointTransport.HttpsOnly;
        settings.Https.Endpoints.Add(new TransportHttpsEndpointOptions
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

    /// <summary>With the proxy named, the posture stops being a guess between two deployments and becomes the one the operator described.</summary>
    [Fact]
    public async Task StartAsync_ClearTextEndpointBehindATrustedProxy_NamesTheProxiedHopRatherThanGuessing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(Enabled(), logs, TrustedProxyConfigured());

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("between that proxy and here", record.Message, StringComparison.Ordinal);
        Assert.Contains("ReverseProxy:TrustedProxies", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("McpEndpoint:Https:Endpoints", record.Message, StringComparison.Ordinal);
        Assert.Equal(1, Assert.Contains("TrustedProxyCount", record.Properties));
    }

    /// <summary>The proxy stands in front of a process, not in front of a listener it never touches, so a deployment terminating its own TLS is still silent.</summary>
    [Fact]
    public async Task StartAsync_EndpointServingItsOwnCertificateBehindATrustedProxy_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = Enabled();
        settings.Transport = EndpointTransport.HttpsOnly;
        settings.Https.Endpoints.Add(new TransportHttpsEndpointOptions
        {
            Name = "public",
            Domain = "mail.example.test",
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret { Name = "bundle", SecretReference = "file:/etc/mailfathom/tls/bundle.pfx" },
            },
        });
        var warning = WarningFor(settings, logs, TrustedProxyConfigured());

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
    };

    private static McpTransportEncryptionWarning WarningFor(
        McpEndpointOptions settings,
        RecordingLoggerProvider logs,
        ReverseProxyOptions? reverseProxySettings = null)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new McpTransportEncryptionWarning(
            Options.Create(settings),
            Options.Create(reverseProxySettings ?? new ReverseProxyOptions()),
            loggerFactory.CreateLogger<McpTransportEncryptionWarning>());
    }

    private static ReverseProxyOptions TrustedProxyConfigured()
    {
        var settings = new ReverseProxyOptions();

        settings.TrustedProxies.Add("10.0.0.5");

        return settings;
    }
}
