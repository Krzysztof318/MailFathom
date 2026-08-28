// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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

/// <summary>Covers what an operator is told when the client surface is served without a credential or without TLS.</summary>
/// <remarks>
/// Both postures are legitimate somewhere — a loopback bind, a private network, a reverse proxy terminating TLS — so
/// neither is refused and both are announced. What the announcement has to carry is which surface it is about and which
/// section undoes it, because an operator who enabled one endpoint and not another reads three warnings that would
/// otherwise be indistinguishable.
/// </remarks>
public sealed class ClientTransportSecurityWarningTests
{
    [Fact]
    public async Task StartAsync_AnEnabledEndpointRequiringNoCredential_WarnsThatTheMailboxIsServedToAnything()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(TlsTerminating(), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("ClientEndpoint:Authentication", record.Message, StringComparison.Ordinal);
        Assert.Equal(ClientEndpointOptions.RoutePrefix, Assert.Contains("ClientRoutePrefix", record.Properties));
    }

    [Fact]
    public async Task StartAsync_AnEnabledEndpointServedInClearText_NamesThePortAndTheSectionThatChangesIt()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = Authenticated();
        var warning = WarningFor(settings, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("ClientEndpoint:Https:Endpoints", record.Message, StringComparison.Ordinal);
        Assert.Equal(settings.Port, Assert.Contains("ClientPort", record.Properties));
    }

    /// <summary>The two are separate postures, so a deployment missing both reads both rather than whichever was checked first.</summary>
    [Fact]
    public async Task StartAsync_AnEndpointMissingBoth_ReportsEachPostureSeparately()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(new ClientEndpointOptions { Enabled = true }, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, logs.Records.Count);
        Assert.All(logs.Records, record => Assert.Equal(LogLevel.Warning, record.Level));
    }

    /// <summary>The posture the warnings exist to ask for, so repeating either would be noise.</summary>
    [Fact]
    public async Task StartAsync_AnEndpointRequiringACredentialOverTls_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = TlsTerminating();
        settings.Authentication.Add(ConfiguredAuthentication.Accepting(OwnerCredentialMethod.ApiKey));
        var warning = WarningFor(settings, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>A surface nobody serves has no posture to warn about, which is the default every deployment starts from.</summary>
    [Fact]
    public async Task StartAsync_ADisabledEndpoint_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var warning = WarningFor(new ClientEndpointOptions(), logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>A permission an operator gave once is still a page crossing the network in the clear, so it is reported at every startup rather than assumed to still be true.</summary>
    [Fact]
    public async Task StartAsync_TheClientServedOverPermittedClearText_ReportsThePageSeparatelyFromTheCredential()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = Authenticated();
        settings.Application.Enabled = true;
        settings.Application.AllowClearText = true;
        var warning = WarningFor(settings, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, logs.Records.Count);
        Assert.Contains(
            logs.Records,
            record => record.Message.Contains("ClientEndpoint:Application:AllowClearText", StringComparison.Ordinal));
    }

    /// <summary>Serving the page over TLS is the posture the report exists to ask for, so it has nothing to say about one.</summary>
    [Fact]
    public async Task StartAsync_TheClientServedOverTls_SaysNothingAboutThePage()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = TlsTerminating();
        settings.Authentication.Add(ConfiguredAuthentication.Accepting(OwnerCredentialMethod.ApiKey));
        settings.Application.Enabled = true;
        var warning = WarningFor(settings, logs);

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
        var warning = WarningFor(new ClientEndpointOptions { Enabled = true }, logs);

        // Act
        await warning.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>The clear-text posture with the credential question already answered, so only the transport warning is left to fire.</summary>
    private static ClientEndpointOptions Authenticated()
    {
        var settings = new ClientEndpointOptions { Enabled = true };

        settings.Authentication.Add(ConfiguredAuthentication.Accepting(OwnerCredentialMethod.ApiKey));

        return settings;
    }

    /// <summary>The TLS posture with the transport question already answered, so only the credential warning is left to fire.</summary>
    private static ClientEndpointOptions TlsTerminating()
    {
        var settings = new ClientEndpointOptions { Enabled = true, Transport = EndpointTransport.HttpsOnly };

        settings.Https.Endpoints.Add(new TransportHttpsEndpointOptions
        {
            Name = "client",
            Domain = "client.example.test",
            Port = 8643,
            ServerCertificate = new TlsServerCertificateOptions
            {
                Bundle = new ConfiguredSecret { Name = "bundle", SecretReference = "file:/etc/mailfathom/tls/client.pfx" },
            },
        });

        return settings;
    }

    private static ClientTransportSecurityWarning WarningFor(ClientEndpointOptions settings, RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new ClientTransportSecurityWarning(
            Options.Create(settings),
            loggerFactory.CreateLogger<ClientTransportSecurityWarning>());
    }
}
