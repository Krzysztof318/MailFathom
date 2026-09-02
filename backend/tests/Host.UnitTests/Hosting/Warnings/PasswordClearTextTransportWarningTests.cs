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

/// <summary>Covers what an operator is told when a surface accepting a password answers on a socket nothing encrypts.</summary>
/// <remarks>
/// This is the whole of what replaced a startup refusal, so its text is the contract: a deployment the previous release
/// would not start now starts, and what stops the hop being invisible is this line. Its silence is as much a contract
/// as its text — a warning that also fired for a surface accepting no password, or for one whose clear-text socket
/// answers no route because it serves HTTPS only or redirects away from them, would be one more line an operator
/// learns to scroll past. Presenting a certificate is not itself the silencing condition: a surface that terminates
/// TLS on one port while its clear-text port still answers the routes reads every password in the clear, and the two
/// facts below pin both halves of that.
/// </remarks>
public sealed class PasswordClearTextTransportWarningTests
{
    /// <summary>The arrangement the withdrawn refusal used to stop: a password read on a clear-text socket with nothing declared in front.</summary>
    [Fact]
    public async Task StartAsync_ClientEndpointAcceptingAPasswordOverClearText_WarnsNamingTheSurfaceAndItsPort()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(logs, client: AcceptingAPassword(new ClientEndpointOptions { Port = 8080 }));

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("readable", record.Message, StringComparison.Ordinal);
        Assert.Contains("ClientEndpoint:Https:Endpoints", record.Message, StringComparison.Ordinal);
        Assert.Equal("ClientEndpoint", Assert.Contains("EndpointSectionName", record.Properties));
        Assert.Equal(8080, Assert.Contains("ClearTextPort", record.Properties));
    }

    /// <summary>Both request-serving surfaces accept the method, so both are read; the administrative one refuses a password outright and has no arrangement to describe.</summary>
    [Fact]
    public async Task StartAsync_McpEndpointAcceptingAPasswordOverClearText_WarnsNamingThatSurface()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(logs, mcp: AcceptingAPassword(new McpEndpointOptions { Port = 8080 }));

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal("McpEndpoint", Assert.Contains("EndpointSectionName", record.Properties));
    }

    /// <summary>An operator who named what stands in front described their deployment, so the message describes that hop rather than listing the postures it would otherwise guess between.</summary>
    [Fact]
    public async Task StartAsync_APasswordCrossingTheHopBehindATrustedProxy_NamesTheProxiedHop()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(
            logs,
            client: AcceptingAPassword(new ClientEndpointOptions()),
            reverseProxySettings: TrustedProxyConfigured());

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Contains("between that proxy and here", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ClientEndpoint:Https:Endpoints", record.Message, StringComparison.Ordinal);
        Assert.Equal(1, Assert.Contains("TrustedProxyCount", record.Properties));
    }

    /// <summary>The credential is what this warning is about, so a surface guarded by a key alone has nothing here to report — that hop is the encryption warnings' subject.</summary>
    [Fact]
    public async Task StartAsync_ClearTextSurfaceAcceptingNoPassword_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = new ClientEndpointOptions { Enabled = true };
        settings.Authentication.Add(ConfiguredAuthentication.Accepting(OwnerCredentialMethod.ApiKey));
        var warning = WarningFor(logs, client: settings);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>A surface presenting its own certificate answers no route in the clear, so the password crosses nothing to report.</summary>
    [Fact]
    public async Task StartAsync_SurfaceTerminatingItsOwnTls_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = PresentingACertificate(AcceptingAPassword(new ClientEndpointOptions()));
        settings.Transport = EndpointTransport.HttpsOnly;
        var warning = WarningFor(logs, client: settings);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>Terminating TLS is not the question this warning asks. A surface presenting a certificate on one port while its clear-text port answers the routes rather than redirecting away from them still reads every password in the clear.</summary>
    [Fact]
    public async Task StartAsync_SurfaceTerminatingTlsWhoseClearTextPortStillAnswersRoutes_Warns()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = PresentingACertificate(AcceptingAPassword(new ClientEndpointOptions { Port = 8080 }));
        settings.Transport = EndpointTransport.HttpAndHttps;
        settings.Https.Redirect.Enabled = false;
        var warning = WarningFor(logs, client: settings);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal("ClientEndpoint", Assert.Contains("EndpointSectionName", record.Properties));
        Assert.Equal(8080, Assert.Contains("ClearTextPort", record.Properties));
    }

    /// <summary>The same arrangement with the redirect left on serves no route in the clear, which is the difference between the two and the whole reason the guard reads what the socket answers rather than whether a certificate exists.</summary>
    [Fact]
    public async Task StartAsync_SurfaceWhoseClearTextPortRedirectsToItsOwn_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = PresentingACertificate(AcceptingAPassword(new ClientEndpointOptions { Port = 8080 }));
        settings.Transport = EndpointTransport.HttpAndHttps;
        settings.Https.Redirect.Enabled = true;
        var warning = WarningFor(logs, client: settings);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>A surface nobody serves accepts no credential over any transport.</summary>
    [Fact]
    public async Task StartAsync_DisabledSurfaces_SayNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = new ClientEndpointOptions();
        settings.Authentication.Add(ConfiguredAuthentication.Accepting(OwnerCredentialMethod.Password));
        var warning = WarningFor(logs, client: settings);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>Two surfaces accepting a password on one clear-text socket are two decisions an operator took separately, so each is reported against the section it was written in.</summary>
    [Fact]
    public async Task StartAsync_BothSurfacesAcceptingAPasswordOverClearText_ReportsEachAgainstItsOwnSection()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(
            logs,
            mcp: AcceptingAPassword(new McpEndpointOptions()),
            client: AcceptingAPassword(new ClientEndpointOptions()));

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["McpEndpoint", "ClientEndpoint"],
            logs.Records.Select(record => Assert.Contains("EndpointSectionName", record.Properties) as string));
    }

    [Fact]
    public async Task StopAsync_AnyPosture_CompletesWithoutSayingAnythingFurther()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(logs, client: AcceptingAPassword(new ClientEndpointOptions()));

        // Act
        await warning.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    private static ClientEndpointOptions PresentingACertificate(ClientEndpointOptions settings)
    {
        settings.Https.Endpoints.Add(new TransportHttpsEndpointOptions
        {
            Name = "public",
            Domain = "mail.example.test",
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret { Name = "bundle", SecretReference = "file:/etc/mailfathom/tls/bundle.pfx" },
            },
        });

        return settings;
    }

    private static McpEndpointOptions AcceptingAPassword(McpEndpointOptions settings)
    {
        settings.Enabled = true;
        settings.Authentication.Add(ConfiguredAuthentication.Accepting(OwnerCredentialMethod.Password));

        return settings;
    }

    private static ClientEndpointOptions AcceptingAPassword(ClientEndpointOptions settings)
    {
        settings.Enabled = true;
        settings.Authentication.Add(ConfiguredAuthentication.Accepting(OwnerCredentialMethod.Password));

        return settings;
    }

    private static PasswordClearTextTransportWarning WarningFor(
        RecordingLoggerProvider logs,
        McpEndpointOptions? mcp = null,
        ClientEndpointOptions? client = null,
        ReverseProxyOptions? reverseProxySettings = null)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new PasswordClearTextTransportWarning(
            Options.Create(mcp ?? new McpEndpointOptions()),
            Options.Create(client ?? new ClientEndpointOptions()),
            Options.Create(reverseProxySettings ?? new ReverseProxyOptions()),
            loggerFactory.CreateLogger<PasswordClearTextTransportWarning>());
    }

    private static ReverseProxyOptions TrustedProxyConfigured()
    {
        var settings = new ReverseProxyOptions();

        settings.TrustedProxies.Add("10.0.0.5");

        return settings;
    }
}
