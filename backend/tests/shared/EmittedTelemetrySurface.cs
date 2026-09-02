// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using MailFathom.Common.Observability;

namespace MailFathom.TestSupport;

/// <summary>Records the whole of what MailFathom's own registries emitted, rather than one instrument of it.</summary>
/// <remarks>
/// <para>
/// <see cref="RecordedMailFathomMeasurements" /> answers what one named instrument measured, which is what a test about
/// a feature asks. This answers what the process publishes at all — every instrument that appeared on the meter, every
/// span that was started on the activity source, and every name, key, and value on either — which is the only level a
/// contract over the emitted surface can be asserted at. A rule checked per publisher is a rule the next publisher is
/// not covered by.
/// </para>
/// <para>
/// Both listeners are process-wide, and xUnit runs test classes in parallel, so what this records includes whatever
/// another class published while it was open. That is deliberate rather than tolerated: a name is either permitted for
/// every publisher or for none, so a name arriving from elsewhere is judged by the same rule and can only fail the
/// contract earlier. What must not depend on the timing is a *value*, which is why the values this asserts on are
/// sentinels the driving test supplied itself — no other class can produce one, so no other class can make an
/// assertion about one pass or fail.
/// </para>
/// <para>
/// Construct it before the publishers under test. An instrument is announced when it is created, so a listener opened
/// afterwards sees the instrument only once something measures on it, and never sees one that only ever reports through
/// an observable callback.
/// </para>
/// </remarks>
internal sealed class EmittedTelemetrySurface : IDisposable
{
    private readonly ActivityListener activityListener;
    private readonly MeterListener meterListener;
    private readonly ConcurrentQueue<RecordedSpan> spans = new();
    private readonly ConcurrentQueue<RecordedMeasurement> measurements = new();
    private readonly ConcurrentDictionary<string, byte> instrumentNames = new(StringComparer.Ordinal);

    /// <summary>Subscribes to MailFathom's activity source and meter and records everything either publishes.</summary>
    internal EmittedTelemetrySurface()
    {
        this.activityListener = new ActivityListener
        {
            ShouldListenTo = source => StringComparer.Ordinal.Equals(source.Name, Telemetry.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = this.RecordSpan,
        };

        ActivitySource.AddActivityListener(this.activityListener);

        this.meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, subscription) =>
            {
                if (!StringComparer.Ordinal.Equals(instrument.Meter.Name, Telemetry.Name))
                {
                    return;
                }

                this.instrumentNames.TryAdd(instrument.Name, 0);
                subscription.EnableMeasurementEvents(instrument);
            },
        };

        this.meterListener.SetMeasurementEventCallback<long>(this.RecordMeasurement);
        this.meterListener.SetMeasurementEventCallback<int>(this.RecordMeasurement);
        this.meterListener.SetMeasurementEventCallback<double>(this.RecordMeasurement);
        this.meterListener.Start();
    }

    /// <summary>Gets every span recorded so far, in the order they ended.</summary>
    internal IReadOnlyList<RecordedSpan> Spans => [.. this.spans];

    /// <summary>Gets every measurement recorded so far, in the order the instruments took them.</summary>
    internal IReadOnlyList<RecordedMeasurement> Measurements => [.. this.measurements];

    /// <summary>Gets the name of every instrument that appeared on MailFathom's meter.</summary>
    internal IReadOnlyCollection<string> InstrumentNames => [.. this.instrumentNames.Keys];

    /// <summary>Gets the name every span was started under, one entry per span rather than per distinct name.</summary>
    internal IReadOnlyCollection<string> SpanNames => [.. this.Spans.Select(span => span.Name)];

    /// <summary>Gets every span name, tag key, instrument name, and dimension key emitted.</summary>
    /// <remarks>
    /// The names and the keys are one list because the contract over them is one rule: a reader of a dashboard meets
    /// them in the same place, and neither may say anything about a message or a person.
    /// </remarks>
    internal IReadOnlyCollection<string> EmittedNames =>
    [
        .. this.InstrumentNames,
        .. this.Spans.Select(span => span.Name),
        .. this.Spans.SelectMany(span => span.Tags.Keys),
        .. this.Measurements.SelectMany(measurement => measurement.Tags.Keys),
    ];

    /// <summary>Gets every tag the surface carries, whether it came from a span or from a measurement.</summary>
    internal IReadOnlyList<KeyValuePair<string, object?>> EmittedTags =>
    [
        .. this.Spans.SelectMany(span => span.Tags),
        .. this.Measurements.SelectMany(measurement => measurement.Tags),
    ];

    /// <summary>Asks every observable instrument being watched for its current value.</summary>
    /// <remarks>An observable instrument publishes nothing until something asks, so its dimensions are invisible until then.</remarks>
    internal void ObserveGauges() => this.meterListener.RecordObservableInstruments();

    /// <inheritdoc />
    public void Dispose()
    {
        this.activityListener.Dispose();
        this.meterListener.Dispose();
    }

    private void RecordSpan(Activity activity) =>
        this.spans.Enqueue(new RecordedSpan(
            activity.OperationName,
            activity.TagObjects.ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal)));

    private void RecordMeasurement<TMeasurement>(
        Instrument instrument,
        TMeasurement measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags,
        object? state)
        where TMeasurement : struct
    {
        this.instrumentNames.TryAdd(instrument.Name, 0);
        this.measurements.Enqueue(new RecordedMeasurement(
            instrument.Name,
            Convert.ToDouble(measurement, CultureInfo.InvariantCulture),
            tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value, StringComparer.Ordinal)));
    }
}

/// <summary>One span MailFathom published, as a listener saw it when it ended.</summary>
/// <param name="Name">The operation name it was started under.</param>
/// <param name="Tags">The attributes it carried when it stopped.</param>
internal sealed record RecordedSpan(string Name, IReadOnlyDictionary<string, object?> Tags);
