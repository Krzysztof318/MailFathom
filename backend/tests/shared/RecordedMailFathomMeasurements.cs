// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using MailFathom.Common.Observability;

namespace MailFathom.TestSupport;

/// <summary>Records what the instruments on MailFathom's own meter actually measured.</summary>
/// <remarks>
/// <para>
/// A listener rather than an inspection of the publishing type, because what an operator reads is what left the
/// process: a test that took the value off a private field would pass while the instrument published something else,
/// or nothing at all.
/// </para>
/// <para>
/// Start it after the type under test has been constructed. An instrument is published on the meter when it is
/// created, and <see cref="MeterListener.Start" /> enumerates the ones that already exist — a listener started first
/// would see nothing. Instruments left behind by an earlier test bearing the same name are enabled too and are
/// harmless: nothing calls them, so they measure nothing.
/// </para>
/// <para>
/// The collection is concurrent because the writer is not the reader. A measurement is recorded on whatever thread
/// published it, and xUnit runs test classes in parallel, so two classes watching one instrument name have this
/// callback running while the other reads. That is also why every read filters by instrument and why an assertion
/// selects by the dimensions it is about rather than expecting a single measurement.
/// </para>
/// </remarks>
internal sealed class RecordedMailFathomMeasurements : IDisposable
{
    private readonly MeterListener listener;
    private readonly ConcurrentQueue<RecordedMeasurement> recorded = new();
    private readonly HashSet<string> instrumentNames;

    /// <summary>Subscribes to instruments on MailFathom's meter and records every measurement they take from now on.</summary>
    /// <param name="instrumentNames">
    /// The instrument names to watch, as they are published. Watches every instrument on the meter when none is named,
    /// which is what a test asserting that an instrument published *nothing* has to do.
    /// </param>
    internal RecordedMailFathomMeasurements(params IReadOnlyList<string> instrumentNames)
    {
        this.instrumentNames = [.. instrumentNames];

        this.listener = new MeterListener
        {
            InstrumentPublished = (instrument, subscription) =>
            {
                if (this.Watches(instrument))
                {
                    subscription.EnableMeasurementEvents(instrument);
                }
            },
        };

        this.listener.SetMeasurementEventCallback<long>(this.Record);
        this.listener.SetMeasurementEventCallback<int>(this.Record);
        this.listener.SetMeasurementEventCallback<double>(this.Record);
        this.listener.Start();
    }

    /// <summary>Gets every measurement recorded so far, in the order the instruments took them.</summary>
    /// <remarks>A snapshot, so a caller may enumerate it while the instruments it watches keep measuring.</remarks>
    internal IReadOnlyList<RecordedMeasurement> Recorded => [.. this.recorded];

    /// <summary>Asks every observable instrument being watched for its current value.</summary>
    /// <remarks>A gauge is only measured when something asks, so a test that never asks records nothing from one.</remarks>
    internal void ObserveGauges() => this.listener.RecordObservableInstruments();

    /// <summary>Discards what has been recorded and asks every observable instrument being watched again.</summary>
    /// <remarks>
    /// A gauge reports the state it is in whenever it is asked, so a second reading appended to the first reads as a
    /// series rather than as the one state the instrument holds now. A test that asks once while a condition holds and
    /// once after it has passed is asserting on the second answer alone, which is what discarding the first leaves it.
    /// </remarks>
    internal void ObserveGaugesAfresh()
    {
        this.recorded.Clear();
        this.listener.RecordObservableInstruments();
    }

    /// <summary>Gets what one instrument published, in the order it measured.</summary>
    /// <param name="instrumentName">The instrument to read.</param>
    /// <returns>Its measurements, or an empty sequence when it took none.</returns>
    internal IReadOnlyList<RecordedMeasurement> Read(string instrumentName) =>
        [.. this.recorded.Where(measurement =>
            StringComparer.Ordinal.Equals(measurement.InstrumentName, instrumentName))];

    /// <summary>Gets the values one instrument measured, in order.</summary>
    /// <param name="instrumentName">The instrument to read.</param>
    /// <returns>Its measurements, or an empty sequence when it took none.</returns>
    internal IReadOnlyList<double> ValuesOf(string instrumentName) =>
        [.. this.Read(instrumentName).Select(measurement => measurement.Value)];

    /// <summary>Gets what one instrument published under one dimension, in order.</summary>
    /// <param name="instrumentName">The instrument to read.</param>
    /// <param name="tagName">The dimension to read off each of its measurements.</param>
    /// <returns>One value per measurement, or <see langword="null" /> where the measurement carried no such tag.</returns>
    internal IReadOnlyList<object?> DimensionOf(string instrumentName, string tagName) =>
        [.. this.Read(instrumentName).Select(measurement => measurement.Tags.GetValueOrDefault(tagName))];

    /// <inheritdoc />
    public void Dispose() => this.listener.Dispose();

    private bool Watches(Instrument instrument) =>
        StringComparer.Ordinal.Equals(instrument.Meter.Name, Telemetry.Name)
        && (this.instrumentNames.Count == 0 || this.instrumentNames.Contains(instrument.Name));

    private void Record<TMeasurement>(
        Instrument instrument,
        TMeasurement measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
        where TMeasurement : struct =>
        this.recorded.Enqueue(new RecordedMeasurement(
            instrument.Name,
            Convert.ToDouble(measurement, CultureInfo.InvariantCulture),
            tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal)));
}

/// <summary>One measurement an instrument published, as a listener saw it.</summary>
/// <param name="InstrumentName">Which instrument took it.</param>
/// <param name="Value">What it measured, widened so a counter and a histogram compare the same way.</param>
/// <param name="Tags">The dimensions it was published under.</param>
internal sealed record RecordedMeasurement(
    string InstrumentName,
    double Value,
    IReadOnlyDictionary<string, object?> Tags);
