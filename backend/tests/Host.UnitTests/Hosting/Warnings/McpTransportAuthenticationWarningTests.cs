// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

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
        Assert.Contains("McpEndpoint:Authentication", record.Message, StringComparison.Ordinal);
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
        var settings = new McpEndpointOptions { Enabled = true };
        settings.Authentication.Add(ConfiguredAuthentication.Accepting(OwnerCredentialMethod.ApiKey));
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
            new McpEndpointOptions(),
            logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>
    /// An empty list is the unauthenticated posture rather than an unfinished configuration, so this warning is the
    /// whole mechanism that keeps it from being silent. An operator who enabled the endpoint and configured no method
    /// beside it reads the same message as one who wrote an empty list, because they have the same deployment.
    /// </summary>
    [Fact]
    public async Task StartAsync_EnabledEndpointConfiguringNoMethod_WarnsThatNothingIdentifiesTheCaller()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = new McpEndpointOptions { Enabled = true };
        settings.Cors.AllowedOrigins.Add("https://client.example.test");
        var warning = WarningFor(settings, logs);

        // Act
        await warning.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("read the synchronized mailboxes", record.Message, StringComparison.Ordinal);
    }

    /// <summary>An access token identifies the person whose mail is served, which is the whole thing the warning asks for.</summary>
    [Fact]
    public async Task StartAsync_EnabledEndpointRequiringAnAccessToken_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var settings = new McpEndpointOptions { Enabled = true };
        settings.Authentication.Add(ConfiguredAuthentication.AcceptingSubjectsFrom("https://mail.example.test/mcp"));
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
        var warning = WarningFor(
            new McpEndpointOptions { Enabled = true },
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
    };

    private static McpTransportAuthenticationWarning WarningFor(McpEndpointOptions settings, RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new McpTransportAuthenticationWarning(
            Options.Create(settings),
            loggerFactory.CreateLogger<McpTransportAuthenticationWarning>());
    }
}
