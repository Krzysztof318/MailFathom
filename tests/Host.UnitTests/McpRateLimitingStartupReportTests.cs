// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration;
using MailFathom.Host.Hosting;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Security;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests;

/// <summary>Covers what an operator is told about the bounds an enabled endpoint serves under.</summary>
/// <remarks>
/// The limits apply whether or not anyone configured them, which makes this report the only place a deployment running
/// on defaults can read what it is actually enforcing. Turning them off is the case the report exists for: from that
/// point the endpoint serves whatever it is asked for, and nothing else in the process would say so.
/// </remarks>
public sealed class McpRateLimitingStartupReportTests
{
    [Fact]
    public async Task StartAsync_DisabledEndpoint_SaysNothing()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new McpEndpointOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(logs.Records);
    }

    [Fact]
    public async Task StartAsync_EnabledEndpointWithoutRateLimits_WarnsThatNothingBoundsTheTraffic()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(EnabledEndpoint(new McpRateLimitingOptions { Enabled = false }), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains("rate limiting turned off", record.Message, StringComparison.Ordinal);
        Assert.Equal("/mcp", Assert.Contains("McpEndpointPath", record.Properties));
    }

    [Fact]
    public async Task StartAsync_EnabledEndpointWithRateLimits_StatesEveryLimitInForce()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            EnabledEndpoint(new McpRateLimitingOptions
            {
                MaxConcurrentRequests = 12,
                ConcurrencyQueueLimit = 3,
                TokenCapacity = 40,
                TokensPerReplenishmentPeriod = 10,
                ReplenishmentPeriod = TimeSpan.FromSeconds(30),
                RequestQueueLimit = 2,
            }),
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

    [Fact]
    public async Task StartAsync_EnabledEndpointWithNothingConfigured_StatesTheDefaultsRatherThanStayingSilent()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(EnabledEndpoint(new McpRateLimitingOptions()), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        // A deployment that wrote no numbers is the one running on limits nobody has seen, so it is the one that most
        // needs them reported.
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal(
            McpRateLimits.Default.MaxConcurrentRequests,
            Assert.Contains("MaxConcurrentRequests", record.Properties));
    }

    [Fact]
    public async Task StartAsync_WithApiKeysConfigured_NamesNoClient()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var endpointSettings = EnabledEndpoint(new McpRateLimitingOptions());
        endpointSettings.Authentication = McpTransportAuthenticationMethods.ApiKey;
        endpointSettings.ApiKeys.Add(new ConfiguredSecret { Name = "desktop-agent" });
        var report = ReportFor(endpointSettings, logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        // The report describes the deployment's own limits and never who is calling or who may call.
        var record = Assert.Single(logs.Records);
        Assert.DoesNotContain("desktop-agent", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopAsync_AfterReporting_SaysNothingFurther()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(EnabledEndpoint(new McpRateLimitingOptions { Enabled = false }), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);
        await report.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(logs.Records);
    }

    private static McpEndpointOptions EnabledEndpoint(McpRateLimitingOptions rateLimitingSettings) => new()
    {
        Enabled = true,
        Authentication = McpTransportAuthenticationMethods.None,
        RateLimiting = rateLimitingSettings,
    };

    private static McpRateLimitingStartupReport ReportFor(McpEndpointOptions settings, RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new McpRateLimitingStartupReport(
            Options.Create(settings),
            loggerFactory.CreateLogger<McpRateLimitingStartupReport>());
    }
}
