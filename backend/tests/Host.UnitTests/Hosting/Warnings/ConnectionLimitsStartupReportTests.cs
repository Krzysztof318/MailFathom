// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.Endpoints;
using MailFathom.Host.Hosting.Warnings;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Warnings;

/// <summary>Covers what an operator is told about the ceiling on connections this process accepts.</summary>
/// <remarks>
/// One line for the process rather than one per endpoint, which is the fact worth reporting: every other transport
/// bound belongs to a surface and this one is reached before anything knows which surface a connection is for.
/// </remarks>
public sealed class ConnectionLimitsStartupReportTests
{
    [Fact]
    public async Task StartAsync_WithNothingConfigured_StatesTheDefaultCeiling()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new ConnectionLimitsOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Equal(
            new ConnectionLimitsOptions().MaxConcurrentConnections,
            Assert.Contains("MaxConcurrentConnections", record.Properties));
    }

    [Fact]
    public async Task StartAsync_WithAConfiguredCeiling_StatesIt()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new ConnectionLimitsOptions { MaxConcurrentConnections = 250 }, logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(250, Assert.Contains("MaxConcurrentConnections", record.Properties));
    }

    [Fact]
    public async Task StartAsync_WithTheCeilingTurnedOff_WarnsThatNothingBoundsTheAccepts()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new ConnectionLimitsOptions { Enabled = false }, logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Equal(LogLevel.Warning, record.Level);

        // The setting named is the one an operator removes to restore the bound.
        Assert.Equal(
            $"{ConnectionLimitsOptions.SectionName}:{nameof(ConnectionLimitsOptions.Enabled)}",
            Assert.Contains("ConnectionLimitSetting", record.Properties));
    }

    /// <summary>
    /// The warning has to say what a flood spends below the endpoint limits, because an operator who has already
    /// configured rate limiting would otherwise read this as a bound they had covered elsewhere.
    /// </summary>
    [Fact]
    public async Task StartAsync_WithTheCeilingTurnedOff_SaysWhatIsSpentBeforeARequestExists()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new ConnectionLimitsOptions { Enabled = false }, logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(logs.Records);
        Assert.Contains("TLS handshakes", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StopAsync_AfterReporting_SaysNothingFurther()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        var report = ReportFor(new ConnectionLimitsOptions(), logs);

        // Act
        await report.StartAsync(TestContext.Current.CancellationToken);
        await report.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(logs.Records);
    }

    [Fact]
    public void Constructor_WithNoSettings_Throws()
    {
        // Arrange
        using var logs = new RecordingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        // Act and assert
        Assert.Throws<ArgumentNullException>(() => new ConnectionLimitsStartupReport(
            null!,
            loggerFactory.CreateLogger<ConnectionLimitsStartupReport>()));
    }

    private static ConnectionLimitsStartupReport ReportFor(
        ConnectionLimitsOptions connectionLimitSettings,
        RecordingLoggerProvider logs)
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));

        return new ConnectionLimitsStartupReport(
            Options.Create(connectionLimitSettings),
            loggerFactory.CreateLogger<ConnectionLimitsStartupReport>());
    }
}
