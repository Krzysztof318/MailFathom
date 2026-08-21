// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Common.Observability;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the recorder the surface-wide telemetry contract is asserted over.</summary>
/// <remarks>
/// A fault here reports a false verdict in every suite that uses it, and the false verdict is the dangerous direction:
/// a recorder that quietly saw nothing makes a contract over an absence pass. So both halves are proved — that what
/// MailFathom publishes is recorded with its names and its dimensions, and that what another registry publishes is not
/// mistaken for it.
/// </remarks>
public sealed class EmittedTelemetrySurfaceTests
{
    /// <summary>The span this drives, named so it is not read as a publisher's own declaration.</summary>
    /// <remarks>
    /// A constant ending in <c>SpanName</c> is how the contract finds a publisher, and this class is a test
    /// rather than one — so the name here deliberately stops short of the convention.
    /// </remarks>
    private const string ProbeSpan = "shared_sources_probe_span";
    private const string CounterName = "mailfathom.shared_sources.probe.counter";
    private const string GaugeName = "mailfathom.shared_sources.probe.gauge";
    private const string DimensionName = "mailfathom.shared_sources.probe.outcome";

    /// <summary>A span MailFathom started is recorded with the name it opened under and the tags it carried.</summary>
    [Fact]
    public void Spans_AnActivityOnMailFathomsSource_AreRecordedWithTheirTags()
    {
        // Arrange
        using var surface = new EmittedTelemetrySurface();

        // Act
        using (var activity = Telemetry.ActivitySource.StartActivity(ProbeSpan))
        {
            activity?.SetTag(DimensionName, "succeeded");
        }

        // Assert
        var span = Assert.Single(surface.Spans, recorded => recorded.Name == ProbeSpan);

        Assert.Equal("succeeded", span.Tags[DimensionName]);
        Assert.Contains(ProbeSpan, surface.SpanNames);
        Assert.Contains(DimensionName, surface.EmittedNames);
    }

    /// <summary>A span another library started is not recorded, so a contract judges MailFathom's own surface.</summary>
    [Fact]
    public void Spans_AnActivityOnAnotherSource_AreNotRecorded()
    {
        // Arrange
        using var somebodyElse = new ActivitySource("SharedSources.NotMailFathom");
        using var surface = new EmittedTelemetrySurface();

        // Act
        using (somebodyElse.StartActivity(ProbeSpan))
        {
            // The span is recorded when it ends, which is where the source is judged.
        }

        // Assert
        Assert.DoesNotContain(ProbeSpan, surface.SpanNames);
    }

    /// <summary>An instrument is recorded by name, and its measurements by value and by dimension.</summary>
    [Fact]
    public void Measurements_ACounterOnMailFathomsMeter_AreRecordedWithTheirDimensions()
    {
        // Arrange
        using var surface = new EmittedTelemetrySurface();
        var counter = Telemetry.Meter.CreateCounter<long>(CounterName);

        // Act
        counter.Add(3, new KeyValuePair<string, object?>(DimensionName, "refused"));

        // Assert
        var measurement = Assert.Single(surface.Measurements, recorded => recorded.InstrumentName == CounterName);

        Assert.Equal(3, measurement.Value);
        Assert.Equal("refused", measurement.Tags[DimensionName]);
        Assert.Contains(CounterName, surface.InstrumentNames);
        Assert.Contains(surface.EmittedTags, tag => tag.Key == DimensionName && Equals(tag.Value, "refused"));
    }

    /// <summary>An observable instrument reports nothing until it is asked, which is what the gauge read is for.</summary>
    [Fact]
    public void ObserveGauges_AnObservableInstrument_ReportsOnlyOnceItIsAsked()
    {
        // Arrange
        using var surface = new EmittedTelemetrySurface();
        Telemetry.Meter.CreateObservableGauge(GaugeName, static () => new Measurement<long>(7));

        // Act
        var beforeAsking = surface.Measurements.Any(recorded => recorded.InstrumentName == GaugeName);
        surface.ObserveGauges();

        // Assert
        Assert.False(beforeAsking);
        Assert.Contains(surface.Measurements, recorded => recorded.InstrumentName == GaugeName && recorded.Value == 7);
    }

    /// <summary>An instrument on another meter is not recorded, however MailFathom-shaped its name is.</summary>
    [Fact]
    public void Measurements_ACounterOnAnotherMeter_AreNotRecorded()
    {
        // Arrange
        using var somebodyElse = new Meter("SharedSources.NotMailFathom");
        using var surface = new EmittedTelemetrySurface();
        var counter = somebodyElse.CreateCounter<long>(CounterName);

        // Act
        counter.Add(5);

        // Assert
        Assert.DoesNotContain(
            surface.Measurements,
            recorded => recorded.InstrumentName == CounterName && recorded.Value == 5);
    }
}
