// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what an operator reads to tell whether the object-storage backend is working, and what stops it when it is not.</summary>
/// <remarks>
/// Each test names an operation of its own rather than one of the published constants. The instruments live on the
/// application's one meter and a series is told apart by its tags, so a name nothing else publishes is what keeps a
/// reading here from picking up the endpoint probe's own traffic while both suites run.
/// </remarks>
public sealed class ObjectStorageTelemetryTests
{
    private const string OperationsInstrumentName = "mailfathom.object_storage.operations";
    private const string DurationInstrumentName = "mailfathom.object_storage.operation.duration";
    private const string BytesInstrumentName = "mailfathom.object_storage.bytes";

    private const string OperationTagName = "mailfathom.object_storage.operation";
    private const string OutcomeTagName = "mailfathom.object_storage.outcome";
    private const string FailureTagName = "mailfathom.object_storage.failure";

    private readonly FakeTimeProvider timeProvider = new();

    /// <summary>How much of it is happening and how long it takes are the two series a bound is changed from.</summary>
    [Fact]
    public void Begin_AnOperationTheEndpointAnswered_CountsItAndTimesItAgainstThatOperation()
    {
        // Arrange
        var telemetry = new ObjectStorageTelemetry(this.timeProvider);
        const string operation = "test_answered";

        using var measurements = new RecordedMailFathomMeasurements(
            OperationsInstrumentName,
            DurationInstrumentName);

        // Act
        using (var measured = telemetry.Begin(operation))
        {
            this.timeProvider.Advance(TimeSpan.FromMilliseconds(250));
            measured.Succeeded();
        }

        // Assert
        var counted = Assert.Single(Of(measurements, OperationsInstrumentName, operation));
        var timed = Assert.Single(Of(measurements, DurationInstrumentName, operation));

        Assert.Equal(1d, counted.Value);
        Assert.Equal(ObjectStorageTelemetry.SucceededOutcomeName, counted.Tags[OutcomeTagName]);
        Assert.False(counted.Tags.ContainsKey(FailureTagName));
        Assert.Equal(0.25d, timed.Value);
    }

    /// <summary>How much is moving over the wire is the third question, and it is a distribution because what is acted on is the tail.</summary>
    [Fact]
    public void Begin_AnOperationThatCarriedAPayload_PublishesHowManyBytesItWas()
    {
        // Arrange
        var telemetry = new ObjectStorageTelemetry(this.timeProvider);
        const string operation = "test_written";

        using var measurements = new RecordedMailFathomMeasurements(BytesInstrumentName);

        // Act
        using (var measured = telemetry.Begin(operation))
        {
            measured.Succeeded(61_051);
        }

        // Assert
        var transferred = Assert.Single(Of(measurements, BytesInstrumentName, operation));

        Assert.Equal(61_051d, transferred.Value);
    }

    /// <summary>A zero on every operation that carries no payload would make the series say a listing moved nothing rather than that it moved nothing measurable.</summary>
    [Fact]
    public void Begin_AnOperationThatCarriedNoPayload_PublishesNoSize()
    {
        // Arrange
        var telemetry = new ObjectStorageTelemetry(this.timeProvider);
        const string operation = "test_listed";

        using var measurements = new RecordedMailFathomMeasurements(BytesInstrumentName);

        // Act
        using (var measured = telemetry.Begin(operation))
        {
            measured.Succeeded();
        }

        // Assert
        Assert.Empty(Of(measurements, BytesInstrumentName, operation));
    }

    /// <summary>
    /// A refused credential and an unreachable endpoint are the same operation ending differently, so what ended it is a
    /// dimension rather than an instrument of its own — one series an operator splits.
    /// </summary>
    [Fact]
    public void Begin_AnOperationThatFailed_CarriesWhatEndedItAsADimension()
    {
        // Arrange
        var telemetry = new ObjectStorageTelemetry(this.timeProvider);
        const string operation = "test_failed";

        using var measurements = new RecordedMailFathomMeasurements(OperationsInstrumentName);

        // Act
        foreach (var classification in ObjectStorageFailure.All)
        {
            using var measured = telemetry.Begin(operation);
            measured.Failed(classification);
        }

        // Assert
        var counted = Of(measurements, OperationsInstrumentName, operation);

        Assert.Equal(
            [.. ObjectStorageFailure.All.Select(classification => classification.Name)],
            counted.Select(measurement => measurement.Tags[FailureTagName]));
        Assert.All(
            counted,
            measurement => Assert.Equal(
                ObjectStorageTelemetry.FailedOutcomeName,
                measurement.Tags[OutcomeTagName]));
    }

    /// <summary>An unspecified classification names nothing, and publishing it would leave a series an alert cannot match on.</summary>
    [Fact]
    public void Begin_AFailureWithNoClassification_IsPublishedAsUnrecognized()
    {
        // Arrange
        var telemetry = new ObjectStorageTelemetry(this.timeProvider);
        const string operation = "test_unclassified";

        using var measurements = new RecordedMailFathomMeasurements(OperationsInstrumentName);

        // Act
        using (var measured = telemetry.Begin(operation))
        {
            measured.Failed(default);
        }

        // Assert
        var counted = Assert.Single(Of(measurements, OperationsInstrumentName, operation));

        Assert.Equal(ObjectStorageFailure.Unrecognized.Name, counted.Tags[FailureTagName]);
    }

    /// <summary>A scope that threw past every classification is not a success nobody observed.</summary>
    [Fact]
    public void Begin_AScopeThatRecordedNothing_IsPublishedAsUnrecognized()
    {
        // Arrange
        var telemetry = new ObjectStorageTelemetry(this.timeProvider);
        const string operation = "test_abandoned";

        using var measurements = new RecordedMailFathomMeasurements(OperationsInstrumentName);

        // Act
        telemetry.Begin(operation).Dispose();

        // Assert
        var counted = Assert.Single(Of(measurements, OperationsInstrumentName, operation));

        Assert.Equal(ObjectStorageTelemetry.FailedOutcomeName, counted.Tags[OutcomeTagName]);
        Assert.Equal(ObjectStorageFailure.Unrecognized.Name, counted.Tags[FailureTagName]);
    }

    /// <summary>A scope disposed twice would count one operation as two, which is what a <c>using</c> inside a retry loop would do.</summary>
    [Fact]
    public void Dispose_AScopeReleasedTwice_PublishesOneOperation()
    {
        // Arrange
        var telemetry = new ObjectStorageTelemetry(this.timeProvider);
        const string operation = "test_released_twice";

        using var measurements = new RecordedMailFathomMeasurements(OperationsInstrumentName);
        var measured = telemetry.Begin(operation);
        measured.Succeeded();

        // Act
        measured.Dispose();
        measured.Dispose();

        // Assert
        Assert.Single(Of(measurements, OperationsInstrumentName, operation));
    }

    /// <summary>The published words are what a dashboard query already matches on, so a rename here is a rename of somebody's alert.</summary>
    [Fact]
    public void OperationNames_AreThePublishedWords()
    {
        // Assert
        Assert.Equal("list", ObjectStorageTelemetry.ListOperationName);
        Assert.Equal("put", ObjectStorageTelemetry.PutOperationName);
        Assert.Equal("delete", ObjectStorageTelemetry.DeleteOperationName);
        Assert.Equal("succeeded", ObjectStorageTelemetry.SucceededOutcomeName);
        Assert.Equal("failed", ObjectStorageTelemetry.FailedOutcomeName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Begin_AnOperationWithABlankName_IsRefused(string operation)
    {
        // Arrange
        var telemetry = new ObjectStorageTelemetry(this.timeProvider);

        // Act, Assert
        Assert.Throws<ArgumentException>(() => telemetry.Begin(operation));
    }

    [Fact]
    public void Begin_AnOperationWithNoName_IsRefused()
    {
        // Arrange
        var telemetry = new ObjectStorageTelemetry(this.timeProvider);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => telemetry.Begin(operation: null!));
    }

    [Fact]
    public void Construction_NoClock_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new ObjectStorageTelemetry(timeProvider: null!));
    }

    private static IReadOnlyList<RecordedMeasurement> Of(
        RecordedMailFathomMeasurements measurements,
        string instrumentName,
        string operation) =>
        [.. measurements.Read(instrumentName)
            .Where(measurement => measurement.Tags.GetValueOrDefault(OperationTagName) as string == operation)];
}
