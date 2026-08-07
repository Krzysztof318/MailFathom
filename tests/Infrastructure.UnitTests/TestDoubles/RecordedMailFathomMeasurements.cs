// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.UnitTests.TestDoubles;

/// <summary>Records what the named instruments on MailFathom's own meter actually measured.</summary>
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
/// </remarks>
internal sealed class RecordedMailFathomMeasurements : IDisposable
{
    private readonly MeterListener listener;
    private readonly List<RecordedMeasurement> recorded = [];
    private readonly HashSet<string> instrumentNames;

    /// <summary>Subscribes to the named instruments and records every measurement they take from now on.</summary>
    /// <param name="instrumentNames">The instrument names to watch, as they are published.</param>
    internal RecordedMailFathomMeasurements(params IReadOnlyList<string> instrumentNames)
    {
        this.instrumentNames = [.. instrumentNames];

        this.listener = new MeterListener
        {
            InstrumentPublished = (instrument, subscription) =>
            {
                if (instrument.Meter.Name == Telemetry.Name && this.instrumentNames.Contains(instrument.Name))
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
    internal IReadOnlyList<RecordedMeasurement> Recorded => this.recorded;

    /// <summary>Asks every observable instrument being watched for its current value.</summary>
    /// <remarks>A gauge is only measured when something asks, so a test that never asks records nothing from one.</remarks>
    internal void ObserveGauges() => this.listener.RecordObservableInstruments();

    /// <summary>Gets the values one instrument measured, in order.</summary>
    /// <param name="instrumentName">The instrument to read.</param>
    /// <returns>Its measurements, or an empty sequence when it took none.</returns>
    internal IReadOnlyList<double> ValuesOf(string instrumentName) =>
        [.. this.recorded.Where(measurement => measurement.InstrumentName == instrumentName)
            .Select(measurement => measurement.Value)];

    /// <summary>Gets the tag pairs one instrument published, in order, rendered as <c>outcome/failure</c>.</summary>
    /// <param name="instrumentName">The instrument to read.</param>
    /// <returns>One rendered pair per measurement.</returns>
    internal IReadOnlyList<string> TagsOf(string instrumentName) =>
        [.. this.recorded.Where(measurement => measurement.InstrumentName == instrumentName)
            .Select(measurement => measurement.Tags)];

    /// <inheritdoc />
    public void Dispose() => this.listener.Dispose();

    private void Record<T>(Instrument instrument, T measurement, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
        where T : struct =>
        this.recorded.Add(new RecordedMeasurement(
            instrument.Name,
            Convert.ToDouble(measurement, System.Globalization.CultureInfo.InvariantCulture),
            DescribeTags(tags)));

    /// <summary>Renders the outcome and failure tags as one value a sequence assertion can compare.</summary>
    /// <remarks>
    /// The two are rendered together rather than kept apart because they are read together: a series split on the
    /// outcome alone would not distinguish two provider failures, and one split on the failure alone would not
    /// distinguish a success from an instance that embeds nothing.
    /// </remarks>
    private static string DescribeTags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string? outcome = null;
        string? failure = null;

        foreach (var tag in tags)
        {
            if (tag.Key == "mailfathom.embedding.outcome")
            {
                outcome = tag.Value as string;
            }
            else if (tag.Key == "mailfathom.embedding.failure")
            {
                failure = tag.Value as string;
            }
        }

        return $"{outcome}/{failure}";
    }
}

/// <summary>One measurement an instrument published, as a listener saw it.</summary>
/// <param name="InstrumentName">Which instrument took it.</param>
/// <param name="Value">What it measured, widened so a counter and a histogram compare the same way.</param>
/// <param name="Tags">Its outcome and failure tags, rendered as <c>outcome/failure</c>.</param>
internal sealed record RecordedMeasurement(string InstrumentName, double Value, string Tags);
