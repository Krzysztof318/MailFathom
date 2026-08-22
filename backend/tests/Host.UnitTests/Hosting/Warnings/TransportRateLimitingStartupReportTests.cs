// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.Infrastructure.Security.Transport;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers what an operator is told about the bounds each enabled endpoint serves under.</summary>
/// <remarks>
/// The limits apply whether or not anyone configured them, which makes this report the only place a deployment running
/// on defaults can read what it is actually enforcing. Turning them off is the case the report exists for: from that
/// point the endpoint serves whatever it is asked for, and nothing else in the process would say so.
/// </remarks>
public sealed class TransportRateLimitingStartupReportTests
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

    [Fact]
    public async Task StartAsync_EnabledMcpEndpointWithoutRateLimits_WarnsThatNothingBoundsTheTraffic()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            EnabledMcpEndpoint(new TransportRateLimitingOptions { Enabled = false }),
            new AdminEndpointOptions(),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("rate limiting turned off", record.Message, StringComparison.Ordinal);
        Assert.Equal("MCP", Assert.Contains("EndpointName", record.Properties));
        Assert.Equal("/mcp", Assert.Contains("EndpointPath", record.Properties));

        // The section named is the one an operator edits to undo it, which is not the same key on both endpoints.
        Assert.Equal("McpEndpoint:RateLimiting", Assert.Contains("RateLimitingSection", record.Properties));
    }

    [Fact]
    public async Task StartAsync_EnabledAdminEndpointWithoutRateLimits_WarnsThatNothingBoundsTheTraffic()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            new McpEndpointOptions(),
            EnabledAdminEndpoint(new TransportRateLimitingOptions { Enabled = false }),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("administrative", Assert.Contains("EndpointName", record.Properties));
        Assert.Equal("/api/admin", Assert.Contains("EndpointPath", record.Properties));
        Assert.Equal("AdminEndpoint:RateLimiting", Assert.Contains("RateLimitingSection", record.Properties));
    }

    [Fact]
    public async Task StartAsync_EnabledClientEndpointWithoutRateLimits_WarnsThatNothingBoundsTheTraffic()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            new McpEndpointOptions(),
            new AdminEndpointOptions(),
            logs,
            EnabledClientEndpoint(new TransportRateLimitingOptions { Enabled = false }));

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("client", Assert.Contains("EndpointName", record.Properties));
        Assert.Equal("/api/client", Assert.Contains("EndpointPath", record.Properties));
        Assert.Equal("ClientEndpoint:RateLimiting", Assert.Contains("RateLimitingSection", record.Properties));
    }

    [Fact]
    public async Task StartAsync_EnabledClientEndpointWithRateLimits_StatesTheLimitsInForce()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            new McpEndpointOptions(),
            new AdminEndpointOptions(),
            logs,
            EnabledClientEndpoint(new TransportRateLimitingOptions { MaxConcurrentRequests = 7 }));

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal("client", Assert.Contains("EndpointName", record.Properties));
        Assert.Equal(7, Assert.Contains("MaxConcurrentRequests", record.Properties));
    }

    [Fact]
    public async Task StartAsync_EnabledEndpointWithRateLimits_StatesEveryLimitInForce()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            EnabledMcpEndpoint(new TransportRateLimitingOptions
            {
                MaxConcurrentRequests = 12,
                ConcurrencyQueueLimit = 3,
                TokenCapacity = 40,
                TokensPerReplenishmentPeriod = 10,
                ReplenishmentPeriod = TimeSpan.FromSeconds(30),
                RequestQueueLimit = 2,
            }),
            new AdminEndpointOptions(),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal(12, Assert.Contains("MaxConcurrentRequests", record.Properties));
        Assert.Equal(3, Assert.Contains("ConcurrencyQueueLimit", record.Properties));
        Assert.Equal(40, Assert.Contains("TokenCapacity", record.Properties));
        Assert.Equal(10, Assert.Contains("TokensPerReplenishmentPeriod", record.Properties));
        Assert.Equal(TimeSpan.FromSeconds(30), Assert.Contains("ReplenishmentPeriod", record.Properties));
        Assert.Equal(2, Assert.Contains("RequestQueueLimit", record.Properties));
    }

    /// <summary>
    /// The two endpoints carry independent numbers, so an operator who narrowed one has to be able to read back that
    /// they narrowed the one they meant. One line reporting both, or one endpoint's line standing for the other, would
    /// leave a mistyped section invisible until a caller was refused by it.
    /// </summary>
    [Fact]
    public async Task StartAsync_WithBothEndpointsEnabled_StatesEachOnesLimitsSeparately()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            EnabledMcpEndpoint(new TransportRateLimitingOptions { MaxConcurrentRequests = 12 }),
            EnabledAdminEndpoint(new TransportRateLimitingOptions { MaxConcurrentRequests = 4 }),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, logs.Records.Count);
        Assert.Equal(
            ["MCP", "administrative"],
            logs.Records.Select(record => Assert.Contains("EndpointName", record.Properties)));
        Assert.Equal(
            ["/mcp", "/api/admin"],
            logs.Records.Select(record => Assert.Contains("EndpointPath", record.Properties)));
        Assert.Equal(
            [12, 4],
            logs.Records.Select(record => Assert.Contains("MaxConcurrentRequests", record.Properties)));
    }

    [Fact]
    public async Task StartAsync_EnabledEndpointWithNothingConfigured_StatesTheDefaultsRatherThanStayingSilent()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            EnabledMcpEndpoint(new TransportRateLimitingOptions()),
            EnabledAdminEndpoint(new TransportRateLimitingOptions()),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        // A deployment that wrote no numbers is the one running on limits nobody has seen, so it is the one that most
        // needs them reported — on both endpoints, which share the product defaults.
        Assert.All(logs.Records, record =>
        {
            Assert.Equal(LogLevel.Information, record.Level);
            Assert.Equal(
                TransportRateLimits.Default.MaxConcurrentRequests,
                Assert.Contains("MaxConcurrentRequests", record.Properties));
        });
    }

    [Fact]
    public async Task StartAsync_WithApiKeysConfigured_NamesNoClient()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var mcpEndpointSettings = EnabledMcpEndpoint(new TransportRateLimitingOptions());
        mcpEndpointSettings.Authentication.Add(ConfiguredAuthentication.ApiKey("desktop-agent"));

        var adminEndpointSettings = EnabledAdminEndpoint(new TransportRateLimitingOptions());
        adminEndpointSettings.Authentication.Add(ConfiguredAuthentication.ApiKey("operator-laptop"));

        var report = ReportFor(mcpEndpointSettings, adminEndpointSettings, logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        // The report describes each deployment's own limits and never who is calling or who may call.
        Assert.All(logs.Records, record =>
        {
            Assert.DoesNotContain("desktop-agent", record.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("operator-laptop", record.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task StopAsync_AfterReporting_SaysNothingFurther()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            EnabledMcpEndpoint(new TransportRateLimitingOptions { Enabled = false }),
            new AdminEndpointOptions(),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);
        await report.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(logs.Records);
    }

    private static McpEndpointOptions EnabledMcpEndpoint(TransportRateLimitingOptions rateLimitingSettings) => new()
    {
        Enabled = true,
        RateLimiting = rateLimitingSettings,
    };

    private static AdminEndpointOptions EnabledAdminEndpoint(TransportRateLimitingOptions rateLimitingSettings) => new()
    {
        Enabled = true,
        RateLimiting = rateLimitingSettings,
    };

    private static ClientEndpointOptions EnabledClientEndpoint(TransportRateLimitingOptions rateLimitingSettings) => new()
    {
        Enabled = true,
        RateLimiting = rateLimitingSettings,
    };

    private static TransportRateLimitingStartupReport ReportFor(
        McpEndpointOptions mcpEndpointSettings,
        AdminEndpointOptions adminEndpointSettings,
        RecordingLoggerProvider logs,
        ClientEndpointOptions? clientEndpointSettings = null)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new TransportRateLimitingStartupReport(
            Options.Create(mcpEndpointSettings),
            Options.Create(adminEndpointSettings),
            Options.Create(clientEndpointSettings ?? new ClientEndpointOptions()),
            loggerFactory.CreateLogger<TransportRateLimitingStartupReport>());
    }
}
