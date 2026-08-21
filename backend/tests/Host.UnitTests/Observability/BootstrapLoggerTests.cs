// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using Xunit;

namespace MailFathom.Host.UnitTests.Observability;

public sealed class BootstrapLoggerTests
{
    private static readonly BootstrapLoggingSettings Settings = new(
        ServiceName: "mailfathom-host",
        ServiceVersion: "1.2.3",
        ServiceRevision: "3f1c9ab",
        EnvironmentName: "Production",
        ExportsToCollector: false);

    [Fact]
    public void RecordHostStarting_Always_ReportsServiceEnvironmentVersionAndRevisionAndNothingElse()
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
                KeyValuePair.Create("ServiceName", (object?)"mailfathom-host"),
                KeyValuePair.Create("ServiceRevision", (object?)"3f1c9ab"),
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
            [KeyValuePair.Create("ServiceName", (object?)"mailfathom-host")],
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
            [KeyValuePair.Create("ServiceName", (object?)"mailfathom-host")],
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
        Assert.Equal("MailFathom.Host.Startup", loggerFactory.CategoryName);
    }

    /// <summary>
    /// The startup records travel to the same collector as everything the container pipeline exports, so a build named
    /// on one and not on the other is one process arriving as two. The service name stays the SDK's to resolve, which
    /// is the other half of that agreement.
    /// </summary>
    [Fact]
    public void CreateResourceBuilder_Always_NamesTheBuildAndLeavesTheServiceNameToTheSdk()
    {
        // Arrange
        var sdkResolved = ResourceBuilder.CreateDefault().Build();

        // Act
        var resource = BootstrapLogger.CreateResourceBuilder().Build();

        // Assert
        var version = Assert.Single(
            resource.Attributes,
            attribute => attribute.Key == StampedBuildResourceExtensions.ServiceVersionAttributeName);
        Assert.Equal(StampedBuildResourceExtensions.StampedServiceVersion, version.Value);
        var revision = Assert.Single(
            resource.Attributes,
            attribute => attribute.Key == StampedBuildResourceExtensions.SourceRevisionAttributeName);
        Assert.Equal(StampedBuildResourceExtensions.StampedSourceRevision, revision.Value);
        Assert.Equal(
            sdkResolved.Attributes.Single(attribute => attribute.Key == "service.name").Value,
            resource.Attributes.Single(attribute => attribute.Key == "service.name").Value);
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
