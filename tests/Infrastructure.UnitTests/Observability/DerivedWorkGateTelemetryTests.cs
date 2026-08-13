// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using MailFathom.Application.Spam.Gating;
using MailFathom.Common.Observability;
using MailFathom.Infrastructure.Observability;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what an operator reads to tell a withheld mailbox from an idle one.</summary>
/// <remarks>
/// Withholding leaves no trace anywhere else: the work is never started, so a gate holding everything and a mailbox
/// producing nothing publish the same absence of embedding and rule activity. The admission tag is what separates them,
/// and it is the only thing about a message these instruments may carry.
/// </remarks>
public sealed class DerivedWorkGateTelemetryTests
{
    private const string AdmissionsInstrumentName = "mailfathom.spam.derived_work.admissions";

    private const string DiscardedInstrumentName = "mailfathom.spam.derived_work.discarded";

    private const string AdmissionTagName = "mailfathom.spam.admission";

    private readonly DerivedWorkGateTelemetry telemetry = new();

    /// <summary>Each answer is its own series, because each one is a different thing for an operator to do about it.</summary>
    [Theory]
    [InlineData(DerivedWorkAdmission.Admitted, "admitted")]
    [InlineData(DerivedWorkAdmission.WithheldAsJunk, "withheld_as_junk")]
    [InlineData(DerivedWorkAdmission.AwaitingClassification, "awaiting_classification")]
    [InlineData(DerivedWorkAdmission.ReleasedAsUnclassifiable, "released_as_unclassifiable")]
    [InlineData(DerivedWorkAdmission.ReleasedAfterWaiting, "released_after_waiting")]
    public void RecordAdmission_OneDecision_CountsItUnderItsOwnAdmission(DerivedWorkAdmission admission, string tag)
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.RecordAdmission(admission);

        // Assert
        Assert.Equal(1, Assert.Single(collector.Read(AdmissionsInstrumentName, tag)).Value);
    }

    [Fact]
    public void RecordDiscardedPassages_AJunkVerdictOverDerivedData_CountsThePassagesItRemoved()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.RecordDiscardedPassages(6);

        // Assert
        Assert.Equal(6, collector.Read(DiscardedInstrumentName).Sum(measurement => measurement.Value));
    }

    /// <summary>A deployment whose classification has caught up removes nothing, and a zero is not a measurement.</summary>
    [Fact]
    public void RecordDiscardedPassages_AJunkVerdictOverNothingDerived_PublishesNoMeasurement()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.RecordDiscardedPassages(0);

        // Assert
        Assert.Empty(collector.Read(DiscardedInstrumentName));
    }

    /// <summary>Reads what the counters on MailFathom's own meter published.</summary>
    private sealed class MeasurementCollector : IDisposable
    {
        private readonly MeterListener listener = new();

        // Concurrent because the listener is enabled for every instrument on MailFathom's one meter, so any other test
        // class publishing to it writes here while this one reads — which a plain list reports as a modified collection.
        private readonly ConcurrentQueue<PublishedMeasurement> measurements = [];

        internal MeasurementCollector()
        {
            this.listener.InstrumentPublished = (instrument, activeListener) =>
            {
                if (StringComparer.Ordinal.Equals(instrument.Meter.Name, Telemetry.Name))
                {
                    activeListener.EnableMeasurementEvents(instrument);
                }
            };
            this.listener.SetMeasurementEventCallback<long>(this.Record);
            this.listener.Start();
        }

        public void Dispose() => this.listener.Dispose();

        /// <summary>Returns what one instrument published, in order.</summary>
        internal IReadOnlyList<PublishedMeasurement> Read(string instrumentName) =>
            [
                .. this.measurements.ToArray().Where(measurement =>
                    StringComparer.Ordinal.Equals(measurement.InstrumentName, instrumentName)),
            ];

        /// <summary>Returns what one instrument published for one admission, in order.</summary>
        internal IReadOnlyList<PublishedMeasurement> Read(string instrumentName, string admission) =>
            [
                .. this.Read(instrumentName).Where(measurement =>
                    measurement.Tags.TryGetValue(AdmissionTagName, out var tag) && Equals(tag, admission)),
            ];

        private void Record<TMeasurement>(
            Instrument instrument,
            TMeasurement measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
            where TMeasurement : struct =>
            this.measurements.Enqueue(new PublishedMeasurement(
                instrument.Name,
                Convert.ToDouble(measurement, CultureInfo.InvariantCulture),
                tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal)));
    }

    /// <summary>One measurement an instrument published, with the dimensions it carried.</summary>
    private sealed record PublishedMeasurement(
        string InstrumentName,
        double Value,
        IReadOnlyDictionary<string, object?> Tags);
}
