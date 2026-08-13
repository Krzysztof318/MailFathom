// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Common.Observability;
using MailFathom.Infrastructure.Observability;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what an operator reads to tell what redacting the derived writes is finding and costing.</summary>
/// <remarks>
/// The instruments carry no egress point, because a derived write goes nowhere: what distinguishes these series from
/// the guarded-egress ones is their own names, which is why every one of them is read by name here.
/// </remarks>
public sealed class SensitiveContentDerivationTelemetryTests
{
    private const string RedactedInstrumentName = "mailfathom.sensitive_content.derivation.redacted";
    private const string FindingsInstrumentName = "mailfathom.sensitive_content.derivation.findings";
    private const string OmittedInstrumentName = "mailfathom.sensitive_content.derivation.omitted";
    private const string RefusalsInstrumentName = "mailfathom.sensitive_content.derivation.refusals";
    private const string DurationInstrumentName = "mailfathom.sensitive_content.derivation.duration";

    private const string CategoryTagName = "mailfathom.sensitive_content.category";
    private const string ScannerTagName = "mailfathom.sensitive_content.scanner";

    private static readonly DateTimeOffset DetectedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly SensitiveContentDerivationTelemetry telemetry = new();

    /// <summary>The acceptance the whole feature is measured against: what the scan adds to a derivation is reported.</summary>
    [Fact]
    public void RecordDerived_ARedactedText_CountsItAndTimesTheScanItCost()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.RecordDerived(
            RedactedText.Create("nothing here", [], omittedCharacterCount: 0),
            TimeSpan.FromMilliseconds(250));

        // Assert
        var redacted = Assert.Single(collector.Read(RedactedInstrumentName));
        var duration = Assert.Single(collector.Read(DurationInstrumentName));

        Assert.Equal(1, redacted.Value);
        Assert.Equal(0.25, duration.Value);
    }

    /// <summary>Which kind of material a mailbox produces is what decides whether a category list is right.</summary>
    [Fact]
    public void RecordDerived_FindingsOfSeveralCategories_CountsEachCategoryOnItsOwnSeries()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.RecordDerived(
            RedactedText.Create(
                "[redacted:CloudKey] and [redacted:CloudKey] and [redacted:EmailAddress]",
                [
                    FindingOf("CloudKey", start: 0),
                    FindingOf("CloudKey", start: 24),
                    FindingOf("EmailAddress", start: 48),
                ],
                omittedCharacterCount: 0),
            TimeSpan.FromMilliseconds(10));

        // Assert
        var findings = collector.Read(FindingsInstrumentName);

        Assert.Equal(
            [("CloudKey", 2d), ("EmailAddress", 1d)],
            findings.Select(finding => (finding.Tags[CategoryTagName] as string, finding.Value)).Order());
    }

    /// <summary>A zero on every derived write would make the series say the ceiling is in play on ordinary mail.</summary>
    [Fact]
    public void RecordDerived_ATextTheCeilingDidNotCut_ReportsNoOmission()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.RecordDerived(
            RedactedText.Create("nothing here", [], omittedCharacterCount: 0),
            TimeSpan.FromMilliseconds(10));

        // Assert
        Assert.Empty(collector.Read(OmittedInstrumentName));
    }

    /// <summary>What the ceiling cuts here is cut out of the index for as long as the message stays derived.</summary>
    [Fact]
    public void RecordDerived_ATextTheCeilingCut_ReportsHowMuchWasDropped()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.RecordDerived(
            RedactedText.Create("as far as the ceiling reached", [], omittedCharacterCount: 4096),
            TimeSpan.FromMilliseconds(10));

        // Assert
        var omitted = Assert.Single(collector.Read(OmittedInstrumentName));

        Assert.Equal(4096, omitted.Value);
    }

    /// <summary>A refused derived write is a message that gains no passages at all, which an operator has to see.</summary>
    [Theory]
    [InlineData(nameof(SensitiveContentScannerKind.Secrets), "secrets")]
    [InlineData(nameof(SensitiveContentScannerKind.Pii), "pii")]
    public void RecordRefused_AScannerThatCouldNotAnswer_CountsTheRefusalAgainstThatScanner(
        string scannerName,
        string expectedTag)
    {
        // Arrange
        using var collector = new MeasurementCollector();
        var scanner = Enum.Parse<SensitiveContentScannerKind>(scannerName);

        // Act
        this.telemetry.RecordRefused(scanner);

        // Assert
        var refusal = Assert.Single(
            collector.Read(RefusalsInstrumentName),
            measurement => Equals(measurement.Tags[ScannerTagName], expectedTag));

        Assert.Equal(1, refusal.Value);
    }

    private static SensitiveContentFinding FindingOf(string category, int start) =>
        SensitiveContentFinding.Create(
            SensitiveContentRule.Create(SensitiveContentCategory.Create(category), $"{category}-rule"),
            SensitiveContentSpan.Create(start, length: 8),
            confidence: 1,
            SensitiveContentDetector.Create("test", "1"),
            DetectedAt);

    /// <summary>Reads what the counters and the histogram on MailFathom's own meter published.</summary>
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
            this.listener.SetMeasurementEventCallback<double>(this.Record);
            this.listener.Start();
        }

        public void Dispose() => this.listener.Dispose();

        /// <summary>Returns what one instrument published, in order.</summary>
        internal IReadOnlyList<PublishedMeasurement> Read(string instrumentName) =>
            [
                .. this.measurements.ToArray().Where(measurement =>
                    StringComparer.Ordinal.Equals(measurement.InstrumentName, instrumentName)),
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
