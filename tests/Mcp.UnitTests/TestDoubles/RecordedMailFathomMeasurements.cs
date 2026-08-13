// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using MailFathom.Common.Observability;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Records what MailFathom's own meter published while one test ran.</summary>
/// <remarks>
/// <para>
/// A listener rather than an inspection of the publishing type, because what an operator reads is what left the
/// process: a test reading a private field would pass while the instrument published something else, or nothing.
/// </para>
/// <para>
/// The collection is concurrent because the writer is not the reader. This listener enables every instrument of the
/// process-wide meter, so a test in another class publishing its own telemetry calls back on whatever thread it runs
/// on while xUnit runs the classes in parallel — which is also why every read filters by instrument and every
/// assertion selects by the dimensions it is about rather than expecting a single measurement.
/// </para>
/// </remarks>
internal sealed class RecordedMailFathomMeasurements : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly ConcurrentQueue<RecordedMeasurement> measurements = new();

    internal RecordedMailFathomMeasurements()
    {
        this.listener.InstrumentPublished = (instrument, activeListener) =>
        {
            if (StringComparer.Ordinal.Equals(instrument.Meter.Name, Telemetry.Name))
            {
                activeListener.EnableMeasurementEvents(instrument);
            }
        };
        this.listener.SetMeasurementEventCallback<long>(this.Record);
        this.listener.SetMeasurementEventCallback<double>(this.Record);
        this.listener.Start();
    }

    /// <inheritdoc />
    public void Dispose() => this.listener.Dispose();

    /// <summary>Returns what one instrument published, in the order it measured.</summary>
    /// <param name="instrumentName">The instrument to read.</param>
    /// <returns>Its measurements since this collector started.</returns>
    internal IReadOnlyList<RecordedMeasurement> Read(string instrumentName) =>
    [
        .. this.measurements.Where(measurement =>
            StringComparer.Ordinal.Equals(measurement.InstrumentName, instrumentName)),
    ];

    private void Record<TMeasurement>(
        Instrument instrument,
        TMeasurement measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
        where TMeasurement : struct =>
        this.measurements.Enqueue(new RecordedMeasurement(
            instrument.Name,
            Convert.ToDouble(measurement, CultureInfo.InvariantCulture),
            tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal)));
}

/// <summary>One measurement an instrument published, with the dimensions it carried.</summary>
/// <param name="InstrumentName">Which instrument took it.</param>
/// <param name="Value">What it measured, widened so a counter and a histogram compare the same way.</param>
/// <param name="Tags">The dimensions it was published under.</param>
internal sealed record RecordedMeasurement(
    string InstrumentName,
    double Value,
    IReadOnlyDictionary<string, object?> Tags);
