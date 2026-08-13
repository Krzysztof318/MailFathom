// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using MailFathom.Common.Observability;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the recorder every suite asserts its instruments through.</summary>
/// <remarks>
/// Each test names its own instruments, because instruments live on the process-wide meter for as long as the process
/// does: a name shared with another test would let one run's measurements arrive in another's assertion.
/// </remarks>
public sealed class RecordedMailFathomMeasurementsTests
{
    private const string OutcomeTagName = "mailfathom.test.outcome";

    /// <summary>What a recorder is for: the value and the dimensions an instrument actually published.</summary>
    [Fact]
    public void Recorded_AnInstrumentItWatches_CarriesTheValueAndTheDimensions()
    {
        // Arrange
        const string InstrumentName = "mailfathom.test.recorded.counter";
        var counter = Telemetry.Meter.CreateCounter<long>(InstrumentName);
        using var measurements = new RecordedMailFathomMeasurements(InstrumentName);

        // Act
        counter.Add(3, new TagList { { OutcomeTagName, "succeeded" } });

        // Assert
        Assert.Equal([3d], measurements.ValuesOf(InstrumentName));
        Assert.Equal(["succeeded"], measurements.DimensionOf(InstrumentName, OutcomeTagName));
    }

    /// <summary>Naming instruments is what keeps one class's assertion free of another class's measurements.</summary>
    [Fact]
    public void Recorded_AnInstrumentItWasNotGiven_RecordsNothingFromIt()
    {
        // Arrange
        const string WatchedName = "mailfathom.test.watched.counter";
        const string UnwatchedName = "mailfathom.test.unwatched.counter";
        var watched = Telemetry.Meter.CreateCounter<long>(WatchedName);
        var unwatched = Telemetry.Meter.CreateCounter<long>(UnwatchedName);
        using var measurements = new RecordedMailFathomMeasurements(WatchedName);

        // Act
        watched.Add(1);
        unwatched.Add(1);

        // Assert
        Assert.Equal([1d], measurements.ValuesOf(WatchedName));
        Assert.Empty(measurements.Read(UnwatchedName));
    }

    /// <summary>Watching everything is what a test asserting that an instrument published nothing has to do.</summary>
    [Fact]
    public void Recorded_NoInstrumentNamed_WatchesEveryInstrumentOnTheMeter()
    {
        // Arrange
        const string InstrumentName = "mailfathom.test.unnamed.histogram";
        var histogram = Telemetry.Meter.CreateHistogram<double>(InstrumentName);
        using var measurements = new RecordedMailFathomMeasurements();

        // Act
        histogram.Record(1.5);

        // Assert
        Assert.Equal([1.5], measurements.ValuesOf(InstrumentName));
    }

    /// <summary>A gauge measures when something asks, so the recorder has to be able to ask.</summary>
    [Fact]
    public void ObserveGauges_AnObservableInstrument_RecordsWhatItReportsWhenAsked()
    {
        // Arrange
        const string InstrumentName = "mailfathom.test.observed.gauge";
        Telemetry.Meter.CreateObservableGauge(InstrumentName, () => 7L);
        using var measurements = new RecordedMailFathomMeasurements(InstrumentName);

        // Act
        measurements.ObserveGauges();

        // Assert
        Assert.Equal([7d], measurements.ValuesOf(InstrumentName));
    }

    /// <summary>A dimension an instrument never published reads as absent rather than as an empty value.</summary>
    [Fact]
    public void DimensionOf_ATagTheMeasurementDidNotCarry_ReadsAsAbsent()
    {
        // Arrange
        const string InstrumentName = "mailfathom.test.untagged.counter";
        var counter = Telemetry.Meter.CreateCounter<int>(InstrumentName);
        using var measurements = new RecordedMailFathomMeasurements(InstrumentName);

        // Act
        counter.Add(1);

        // Assert
        Assert.Equal([null], measurements.DimensionOf(InstrumentName, OutcomeTagName));
        Assert.All(measurements.Read(InstrumentName), measurement => Assert.Empty(measurement.Tags));
    }
}
