// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.Metrics;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Common.Observability;
using MailFathom.Infrastructure.Observability;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

public sealed class EmailEmbeddingTelemetryTests
{
    private const string MessageCountInstrument = "mailfathom.embedding.messages";

    private const string MessageDurationInstrument = "mailfathom.embedding.message.duration";

    private const string OutcomeTagName = "mailfathom.embedding.outcome";

    private const string FailureTagName = "mailfathom.embedding.failure";

    /// <summary>
    /// The tag strings are the whole value of the instrument: an operator diagnoses a falling-behind instance by
    /// splitting these series on the outcome and, when the provider refused, on the classification. A swapped or
    /// mistyped string would mislabel that split rather than fail, so every value of both closed sets is driven through
    /// the mapping here.
    /// </summary>
    [Fact]
    public void RecordEmbeddedMessage_EveryOutcomeAndFailure_TagsTheSeriesWithTheNameThatOutcomeIsReadBy()
    {
        // Arrange
        var telemetry = new EmailEmbeddingTelemetry();
        List<string> recorded = [];
        using var listener = ListenForCountedMessages(recorded);

        StoredEmailEmbeddingRun[] runs =
        [
            StoredEmailEmbeddingRun.Embedded(3),
            StoredEmailEmbeddingRun.NoActiveProfile(),
            StoredEmailEmbeddingRun.GeneratorDisagreesWithProfile(),
            .. Enum.GetValues<EmbeddingGenerationFailure>()
                .Select(failure => StoredEmailEmbeddingRun.ProviderFailed(1, failure)),
        ];

        // Act
        foreach (var run in runs)
        {
            telemetry.RecordEmbeddedMessage(run, TimeSpan.FromSeconds(2));
        }

        // Assert
        Assert.Equal(
            [
                "embedded/none",
                "no_active_profile/none",
                "generator_disagrees_with_profile/none",
                "provider_failed/credential_rejected",
                "provider_failed/rate_limited",
                "provider_failed/request_timed_out",
                "provider_failed/transport_faulted",
                "provider_failed/request_refused",
                "provider_failed/vector_shape_unexpected",
            ],
            recorded);
    }

    /// <summary>
    /// The duration carries the same tags as the count, because a dashboard splits both on one dimension and a series
    /// present on one instrument and absent from the other reads as a gap rather than as an absence.
    /// </summary>
    [Fact]
    public void RecordEmbeddedMessage_AProviderFailure_TagsTheDurationAsItTagsTheCount()
    {
        // Arrange
        var telemetry = new EmailEmbeddingTelemetry();
        List<string> counted = [];
        List<string> timed = [];
        using var listener = ListenFor(MessageCountInstrument, counted, MessageDurationInstrument, timed);

        // Act
        telemetry.RecordEmbeddedMessage(
            StoredEmailEmbeddingRun.ProviderFailed(0, EmbeddingGenerationFailure.RateLimited),
            TimeSpan.FromSeconds(2));

        // Assert
        Assert.Equal(["provider_failed/rate_limited"], counted);
        Assert.Equal(counted, timed);
    }

    private static MeterListener ListenForCountedMessages(List<string> recorded) =>
        ListenFor(MessageCountInstrument, recorded, durationInstrumentName: null, timed: null);

    /// <summary>Subscribes to MailFathom's own meter and records the tag pair of every measurement the named instruments take.</summary>
    /// <remarks>
    /// A listener rather than an inspection of the type, because the tags are what leaves the process and a test that
    /// read them from a private member would pass while the instrument published something else.
    /// </remarks>
    private static MeterListener ListenFor(
        string countInstrumentName,
        List<string> counted,
        string? durationInstrumentName,
        List<string>? timed)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, subscription) =>
            {
                if (instrument.Meter.Name != Telemetry.Name)
                {
                    return;
                }

                if (instrument.Name == countInstrumentName
                    || (durationInstrumentName is not null && instrument.Name == durationInstrumentName))
                {
                    subscription.EnableMeasurementEvents(instrument);
                }
            },
        };

        listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
        {
            if (instrument.Name == countInstrumentName)
            {
                counted.Add(DescribeTags(tags));
            }
        });

        listener.SetMeasurementEventCallback<double>((instrument, _, tags, _) =>
        {
            if (instrument.Name == durationInstrumentName)
            {
                timed?.Add(DescribeTags(tags));
            }
        });

        listener.Start();

        return listener;
    }

    /// <summary>Renders one measurement's outcome and failure tags as a single value a sequence assertion can compare.</summary>
    private static string DescribeTags(ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        string? outcome = null;
        string? failure = null;

        foreach (var tag in tags)
        {
            if (tag.Key == OutcomeTagName)
            {
                outcome = tag.Value as string;
            }
            else if (tag.Key == FailureTagName)
            {
                failure = tag.Value as string;
            }
        }

        return $"{outcome}/{failure}";
    }
}
