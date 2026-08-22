// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Access;
using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers what an operator is told about how long each enabled endpoint lets one request run.</summary>
/// <remarks>
/// The ceiling applies whether or not anyone configured it, so this report is the only place a deployment running on
/// the default can read what it is enforcing. Turning it off is the case the report exists for: from that point a
/// request holds its concurrency permit for as long as it takes and nothing else would say so.
/// </remarks>
public sealed class TransportRequestTimeoutStartupReportTests
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
    public async Task StartAsync_EnabledMcpEndpointWithoutACeiling_WarnsThatNothingBoundsTheRequest()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            EnabledMcpEndpoint(new TransportRequestTimeoutOptions { Enabled = false }),
            new AdminEndpointOptions(),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("MCP", Assert.Contains("EndpointName", record.Properties));
        Assert.Equal("/mcp", Assert.Contains("EndpointPath", record.Properties));

        // The section named is the one an operator edits to undo it, which is not the same key on both endpoints.
        Assert.Equal("McpEndpoint:RequestTimeout", Assert.Contains("RequestTimeoutSection", record.Properties));
    }

    [Fact]
    public async Task StartAsync_EnabledAdminEndpointWithoutACeiling_WarnsThatNothingBoundsTheRequest()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            new McpEndpointOptions(),
            EnabledAdminEndpoint(new TransportRequestTimeoutOptions { Enabled = false }),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Equal("administrative", Assert.Contains("EndpointName", record.Properties));
        Assert.Equal("/api/admin", Assert.Contains("EndpointPath", record.Properties));
        Assert.Equal("AdminEndpoint:RequestTimeout", Assert.Contains("RequestTimeoutSection", record.Properties));
    }

    [Fact]
    public async Task StartAsync_EnabledEndpointWithACeiling_StatesTheDurationItAbandonsARequestAt()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            EnabledMcpEndpoint(new TransportRequestTimeoutOptions { Duration = TimeSpan.FromMinutes(2) }),
            new AdminEndpointOptions(),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal(TimeSpan.FromMinutes(2), Assert.Contains("RequestTimeout", record.Properties));
    }

    /// <summary>
    /// The two endpoints carry independent ceilings, and the administrative one is the one worth narrowing, so an
    /// operator has to be able to read back that they narrowed the endpoint they meant.
    /// </summary>
    [Fact]
    public async Task StartAsync_WithBothEndpointsEnabled_StatesEachOnesCeilingSeparately()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            EnabledMcpEndpoint(new TransportRequestTimeoutOptions { Duration = TimeSpan.FromMinutes(10) }),
            EnabledAdminEndpoint(new TransportRequestTimeoutOptions { Duration = TimeSpan.FromSeconds(30) }),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, logs.Records.Count);
        Assert.Equal(
            ["MCP", "administrative"],
            logs.Records.Select(record => Assert.Contains("EndpointName", record.Properties)));
        Assert.Equal(
            [TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(30)],
            logs.Records.Select(record => Assert.Contains("RequestTimeout", record.Properties)));
    }

    [Fact]
    public async Task StartAsync_EnabledEndpointWithNothingConfigured_StatesTheDefaultRatherThanStayingSilent()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(
            EnabledMcpEndpoint(new TransportRequestTimeoutOptions()),
            EnabledAdminEndpoint(new TransportRequestTimeoutOptions()),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        // A deployment that wrote no number is the one running on a ceiling nobody has seen, so it is the one that most
        // needs it reported.
        var expected = new TransportRequestTimeoutOptions().Duration;

        Assert.All(logs.Records, record =>
        {
            Assert.Equal(LogLevel.Information, record.Level);
            Assert.Equal(expected, Assert.Contains("RequestTimeout", record.Properties));
        });
    }

    [Fact]
    public async Task StartAsync_WithApiKeysConfigured_NamesNoClient()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var mcpEndpointSettings = EnabledMcpEndpoint(new TransportRequestTimeoutOptions());
        mcpEndpointSettings.Authentication.Add(ConfiguredAuthentication.ApiKey("desktop-agent"));

        var adminEndpointSettings = EnabledAdminEndpoint(new TransportRequestTimeoutOptions());
        adminEndpointSettings.Authentication.Add(ConfiguredAuthentication.ApiKey("operator-laptop"));

        var report = ReportFor(mcpEndpointSettings, adminEndpointSettings, logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        // The report describes each deployment's own ceiling and never who is calling or who may call.
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
            EnabledMcpEndpoint(new TransportRequestTimeoutOptions { Enabled = false }),
            new AdminEndpointOptions(),
            logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);
        await report.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(logs.Records);
    }

    private static McpEndpointOptions EnabledMcpEndpoint(TransportRequestTimeoutOptions requestTimeoutSettings) => new()
    {
        Enabled = true,
        RequestTimeout = requestTimeoutSettings,
    };

    private static AdminEndpointOptions EnabledAdminEndpoint(TransportRequestTimeoutOptions requestTimeoutSettings) => new()
    {
        Enabled = true,
        RequestTimeout = requestTimeoutSettings,
    };

    private static TransportRequestTimeoutStartupReport ReportFor(
        McpEndpointOptions mcpEndpointSettings,
        AdminEndpointOptions adminEndpointSettings,
        RecordingLoggerProvider logs,
        ClientEndpointOptions? clientEndpointSettings = null)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new TransportRequestTimeoutStartupReport(
            Options.Create(mcpEndpointSettings),
            Options.Create(adminEndpointSettings),
            Options.Create(clientEndpointSettings ?? new ClientEndpointOptions()),
            loggerFactory.CreateLogger<TransportRequestTimeoutStartupReport>());
    }
}
