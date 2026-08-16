// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Infrastructure.Secrets.Discovery;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers what an operator is told about how much each configured credential may do.</summary>
/// <remarks>
/// A grant nobody wrote down reaches the whole of its surface, and this report is the only place a deployment running
/// on that default can read it. What matters most here is that an entry which never narrowed says so in its own line
/// rather than being reported as though somebody had chosen the permissions it holds.
/// </remarks>
public sealed class TransportGrantStartupReportTests
{
    [Fact]
    public async Task StartAsync_WithNeitherEndpointEnabled_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new McpEndpointOptions(), new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    /// <summary>There is no entry for a grant to hang on, so a caller admitted here holds everything the surface publishes.</summary>
    [Fact]
    public async Task StartAsync_AnEnabledEndpointWithNoEntry_SaysEveryCallerHoldsTheWholeSurface()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new McpEndpointOptions { Enabled = true }, new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("MCP", Assert.Contains("EndpointName", record.Properties));
        Assert.Equal("/mcp", Assert.Contains("EndpointPath", record.Properties));
        Assert.Equal(
            "mailfathom.mail.read, mailfathom.mail.ask",
            Assert.Contains("GrantedPermissions", record.Properties));
        Assert.Equal("McpEndpoint:Authentication", Assert.Contains("AuthenticationSettingPath", record.Properties));
    }

    /// <summary>The line an operator meets on a first run: they wrote a credential and no grant, and this is what it turned out to hold.</summary>
    [Fact]
    public async Task StartAsync_AnEntryThatWroteNoGrant_NamesItAndWhatItThereforeHolds()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(McpEndpointWith(AnApiKeyEntry()), new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Contains("writes down no grant", record.Message, StringComparison.Ordinal);
        Assert.Equal("McpEndpoint:Authentication:0", Assert.Contains("EntrySettingPath", record.Properties));
        Assert.Equal(
            "mailfathom.mail.read, mailfathom.mail.ask",
            Assert.Contains("GrantedPermissions", record.Properties));
    }

    [Fact]
    public async Task StartAsync_AnEntryThatNarrowedItsGrant_StatesWhatItResolvedTo()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var entry = AnApiKeyEntry();
        entry.Permissions.Add(MailFathomPermission.MailRead.Name);
        entry.MarkGrantWritten();

        var report = ReportFor(McpEndpointWith(entry), new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal("mailfathom.mail.read", Assert.Contains("GrantedPermissions", record.Properties));
        Assert.DoesNotContain("writes down no grant", record.Message, StringComparison.Ordinal);
    }

    /// <summary>An empty grant would otherwise read as a message that lost its argument, and it is the value worth being unambiguous about.</summary>
    [Fact]
    public async Task StartAsync_AnEntryGrantedNothing_NamesTheEmptinessRatherThanPrintingNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var entry = AnApiKeyEntry();
        entry.MarkGrantWritten();

        var report = ReportFor(McpEndpointWith(entry), new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal("nothing", Assert.Contains("GrantedPermissions", record.Properties));
    }

    /// <summary>Such an entry states a ceiling rather than what each caller holds, and an operator reading the two the same way would over-read the grant.</summary>
    [Fact]
    public async Task StartAsync_AnEntryNarrowedByTokenScopes_SaysTheGrantIsACeiling()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var entry = new TransportAuthenticationOptions
        {
            OAuth = new OAuthValidationOptions { Resource = "https://mail.example.test/mcp" },
            PermissionsFromTokenScopes = true,
        };

        entry.Permissions.Add(MailFathomPermission.MailAsk.Name);
        entry.MarkGrantWritten();

        var report = ReportFor(McpEndpointWith(entry), new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Contains("at most", record.Message, StringComparison.Ordinal);
        Assert.Equal("mailfathom.mail.ask", Assert.Contains("GrantedPermissions", record.Properties));
    }

    /// <summary>The two surfaces draw from disjoint halves, so an operator has to be able to read back that they narrowed the one they meant.</summary>
    [Fact]
    public async Task StartAsync_BothEndpointsEnabled_ReportsEachEntryAgainstItsOwnSurface()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var adminEndpoint = new AdminEndpointOptions { Enabled = true };
        adminEndpoint.Authentication.Add(AnApiKeyEntry());

        var report = ReportFor(McpEndpointWith(AnApiKeyEntry()), adminEndpoint, logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["MCP", "administrative"],
            logs.Records.Select(record => Assert.Contains("EndpointName", record.Properties)));
        Assert.Equal(
            [
                "mailfathom.mail.read, mailfathom.mail.ask",
                string.Join(
                    ", ",
                    MailFathomPermission.PublishedFor(ProtectedSurface.Administration).Select(permission => permission.Name)),
            ],
            logs.Records.Select(record => Assert.Contains("GrantedPermissions", record.Properties)));
    }

    /// <summary>Every line names a configuration position and a published capability, and never the credential that sits there.</summary>
    [Fact]
    public async Task StartAsync_AnEntryCarryingACredential_NamesNeitherTheKeyNorItsReference()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(McpEndpointWith(AnApiKeyEntry()), new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.DoesNotContain("workstation", record.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("plaintext:", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopAsync_AfterStarting_SaysNothingFurther()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(McpEndpointWith(AnApiKeyEntry()), new AdminEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);
        await report.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(logs.Records);
    }

    private static TransportAuthenticationOptions AnApiKeyEntry() => new()
    {
        ApiKey = new ConfiguredSecret { Name = "workstation", SecretReference = "plaintext:a-key" },
    };

    private static McpEndpointOptions McpEndpointWith(TransportAuthenticationOptions entry)
    {
        var endpointSettings = new McpEndpointOptions { Enabled = true };
        endpointSettings.Authentication.Add(entry);

        return endpointSettings;
    }

    private static TransportGrantStartupReport ReportFor(
        McpEndpointOptions mcpEndpointSettings,
        AdminEndpointOptions adminEndpointSettings,
        RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new TransportGrantStartupReport(
            Options.Create(mcpEndpointSettings),
            Options.Create(adminEndpointSettings),
            loggerFactory.CreateLogger<TransportGrantStartupReport>());
    }
}
