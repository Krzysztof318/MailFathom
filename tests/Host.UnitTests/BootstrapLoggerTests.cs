// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Host.Observability;
using MailMcp.TestSupport;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MailMcp.Host.UnitTests;

public sealed class BootstrapLoggerTests
{
    private static readonly BootstrapLoggingSettings Settings = new(
        ServiceName: "mailmcp-host",
        ServiceVersion: "1.2.3",
        EnvironmentName: "Production",
        ExportsToCollector: false);

    [Fact]
    public void RecordHostStarting_Always_ReportsServiceEnvironmentAndVersionAndNothingElse()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        using var bootstrapLogger = new BootstrapLogger(loggerFactory, Settings);

        // Act
        bootstrapLogger.RecordHostStarting();

        // Assert
        var record = Assert.Single(loggerFactory.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Null(record.Failure);
        Assert.Equal(
            [
                KeyValuePair.Create("EnvironmentName", (object?)"Production"),
                KeyValuePair.Create("ServiceName", (object?)"mailmcp-host"),
                KeyValuePair.Create("ServiceVersion", (object?)"1.2.3"),
            ],
            record.Properties.OrderBy(property => property.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void RecordHostFailed_Always_ReportsCriticalAndCarriesTheExceptionThatEndedTheProcess()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        using var bootstrapLogger = new BootstrapLogger(loggerFactory, Settings);
        var startupFailure = new InvalidOperationException("host startup failed");

        // Act
        bootstrapLogger.RecordHostFailed(startupFailure);

        // Assert
        var record = Assert.Single(loggerFactory.Records);
        Assert.Equal(LogLevel.Critical, record.Level);
        Assert.Same(startupFailure, record.Failure);
        Assert.Equal(
            [KeyValuePair.Create("ServiceName", (object?)"mailmcp-host")],
            record.Properties);
    }

    [Fact]
    public void RecordHostFailed_Always_KeepsTheExceptionOutOfTheMessageSoOnlyStructuredDataCarriesIt()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        using var bootstrapLogger = new BootstrapLogger(loggerFactory, Settings);

        // Act
        bootstrapLogger.RecordHostFailed(new InvalidOperationException("Password=hunter2"));

        // Assert
        var record = Assert.Single(loggerFactory.Records);
        Assert.DoesNotContain("hunter2", record.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordHostStopped_Always_ReportsAnOrderlyShutdown()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        using var bootstrapLogger = new BootstrapLogger(loggerFactory, Settings);

        // Act
        bootstrapLogger.RecordHostStopped();

        // Assert
        var record = Assert.Single(loggerFactory.Records);
        Assert.Equal(LogLevel.Information, record.Level);
        Assert.Null(record.Failure);
        Assert.Equal(
            [KeyValuePair.Create("ServiceName", (object?)"mailmcp-host")],
            record.Properties);
    }

    [Fact]
    public void Constructor_Always_WritesUnderTheStartupCategory()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();

        // Act
        using var bootstrapLogger = new BootstrapLogger(loggerFactory, Settings);

        // Assert
        Assert.Equal("MailMcp.Host.Startup", loggerFactory.CategoryName);
    }

    [Fact]
    public void Dispose_Always_ReleasesTheOwnedPipeline()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        var bootstrapLogger = new BootstrapLogger(loggerFactory, Settings);

        // Act
        bootstrapLogger.Dispose();

        // Assert
        Assert.Equal(1, loggerFactory.DisposeCount);
    }

    [Fact]
    public void Dispose_CalledTwice_ReleasesTheOwnedPipelineOnce()
    {
        // Arrange
        using var loggerFactory = new RecordingLoggerFactory();
        var bootstrapLogger = new BootstrapLogger(loggerFactory, Settings);

        // Act
        bootstrapLogger.Dispose();
        bootstrapLogger.Dispose();

        // Assert
        Assert.Equal(1, loggerFactory.DisposeCount);
    }
}
