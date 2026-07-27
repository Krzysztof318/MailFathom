// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Host.Observability;
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
        using var recorder = new BootstrapLogRecorder();
        using var bootstrapLogger = new BootstrapLogger(recorder, Settings);

        // Act
        bootstrapLogger.RecordHostStarting();

        // Assert
        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal(
            [
                KeyValuePair.Create("EnvironmentName", (object?)"Production"),
                KeyValuePair.Create("ServiceName", (object?)"mailmcp-host"),
                KeyValuePair.Create("ServiceVersion", (object?)"1.2.3"),
            ],
            entry.Properties.OrderBy(property => property.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void RecordHostFailed_Always_ReportsCriticalAndCarriesTheExceptionThatEndedTheProcess()
    {
        // Arrange
        using var recorder = new BootstrapLogRecorder();
        using var bootstrapLogger = new BootstrapLogger(recorder, Settings);
        var startupFailure = new InvalidOperationException("host startup failed");

        // Act
        bootstrapLogger.RecordHostFailed(startupFailure);

        // Assert
        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(LogLevel.Critical, entry.Level);
        Assert.Same(startupFailure, entry.Exception);
        Assert.Equal(
            [KeyValuePair.Create("ServiceName", (object?)"mailmcp-host")],
            entry.Properties);
    }

    [Fact]
    public void RecordHostFailed_Always_KeepsTheExceptionOutOfTheMessageSoOnlyStructuredDataCarriesIt()
    {
        // Arrange
        using var recorder = new BootstrapLogRecorder();
        using var bootstrapLogger = new BootstrapLogger(recorder, Settings);

        // Act
        bootstrapLogger.RecordHostFailed(new InvalidOperationException("Password=hunter2"));

        // Assert
        var entry = Assert.Single(recorder.Entries);
        Assert.DoesNotContain("hunter2", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RecordHostStopped_Always_ReportsAnOrderlyShutdown()
    {
        // Arrange
        using var recorder = new BootstrapLogRecorder();
        using var bootstrapLogger = new BootstrapLogger(recorder, Settings);

        // Act
        bootstrapLogger.RecordHostStopped();

        // Assert
        var entry = Assert.Single(recorder.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Equal(
            [KeyValuePair.Create("ServiceName", (object?)"mailmcp-host")],
            entry.Properties);
    }

    [Fact]
    public void Constructor_Always_WritesUnderTheStartupCategory()
    {
        // Arrange
        using var recorder = new BootstrapLogRecorder();

        // Act
        using var bootstrapLogger = new BootstrapLogger(recorder, Settings);

        // Assert
        Assert.Equal("MailMcp.Host.Startup", recorder.CategoryName);
    }

    [Fact]
    public void Dispose_Always_ReleasesTheOwnedPipeline()
    {
        // Arrange
        using var recorder = new BootstrapLogRecorder();
        var bootstrapLogger = new BootstrapLogger(recorder, Settings);

        // Act
        bootstrapLogger.Dispose();

        // Assert
        Assert.Equal(1, recorder.DisposeCount);
    }

    [Fact]
    public void Dispose_CalledTwice_ReleasesTheOwnedPipelineOnce()
    {
        // Arrange
        using var recorder = new BootstrapLogRecorder();
        var bootstrapLogger = new BootstrapLogger(recorder, Settings);

        // Act
        bootstrapLogger.Dispose();
        bootstrapLogger.Dispose();

        // Assert
        Assert.Equal(1, recorder.DisposeCount);
    }
}
