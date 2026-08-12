// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Common.Observability;
using MailFathom.Infrastructure.Observability;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Observability;

/// <summary>Covers what an operator reads to tell that a switched-on scanner is guarding anything.</summary>
/// <remarks>
/// Each test names an egress point of its own, because the instruments live on the application's one meter and a
/// series is told apart by its tags rather than by which test published it.
/// </remarks>
public sealed class SensitiveContentEgressTelemetryTests
{
    private const string GuardedInstrumentName = "mailfathom.sensitive_content.guarded";
    private const string FindingsInstrumentName = "mailfathom.sensitive_content.findings";
    private const string OmittedInstrumentName = "mailfathom.sensitive_content.omitted";
    private const string RefusalsInstrumentName = "mailfathom.sensitive_content.refusals";
    private const string DurationInstrumentName = "mailfathom.sensitive_content.scan.duration";

    private const string EgressPointTagName = "mailfathom.sensitive_content.egress_point";
    private const string CategoryTagName = "mailfathom.sensitive_content.category";
    private const string ScannerTagName = "mailfathom.sensitive_content.scanner";

    private static readonly DateTimeOffset DetectedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly SensitiveContentEgressTelemetry telemetry = new();

    /// <summary>What a scan cost and how much of it there was are the two series a bound is changed from.</summary>
    [Fact]
    public void RecordGuarded_AScannedText_CountsItAndTimesItAgainstItsEgressPoint()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.RecordGuarded(
            SensitiveContentEgressPoint.McpSnippet,
            RedactedText.Create("nothing here", [], omittedCharacterCount: 0),
            TimeSpan.FromMilliseconds(250));

        // Assert
        var guarded = Assert.Single(collector.Read(GuardedInstrumentName, "mcp_snippet"));
        var duration = Assert.Single(collector.Read(DurationInstrumentName, "mcp_snippet"));

        Assert.Equal(1, guarded.Value);
        Assert.Equal(0.25, duration.Value);
    }

    /// <summary>Which kind of material a mailbox produces is what decides whether a category list is right.</summary>
    [Fact]
    public void RecordGuarded_FindingsOfSeveralCategories_CountsEachCategoryOnItsOwnSeries()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.RecordGuarded(
            SensitiveContentEgressPoint.ChatPrompt,
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
        var findings = collector.Read(FindingsInstrumentName, "chat_prompt");

        Assert.Equal(
            [("CloudKey", 2d), ("EmailAddress", 1d)],
            findings.Select(finding => (finding.Tags[CategoryTagName] as string, finding.Value)).Order());
    }

    /// <summary>A zero on every guarded text would make the series say the ceiling is in play on ordinary mail.</summary>
    [Fact]
    public void RecordGuarded_ATextTheCeilingDidNotCut_ReportsNoOmission()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.RecordGuarded(
            SensitiveContentEgressPoint.HostedEmbeddingInput,
            RedactedText.Create("nothing here", [], omittedCharacterCount: 0),
            TimeSpan.FromMilliseconds(10));

        // Assert
        Assert.Empty(collector.Read(OmittedInstrumentName, "hosted_embedding_input"));
    }

    /// <summary>Text nothing analyzed is exactly the text that must not leave, so an operator is told how much of it there was.</summary>
    [Fact]
    public void RecordGuarded_ATextTheCeilingCut_ReportsHowMuchWasDropped()
    {
        // Arrange
        using var collector = new MeasurementCollector();

        // Act
        this.telemetry.RecordGuarded(
            SensitiveContentEgressPoint.McpSnippet,
            RedactedText.Create("as far as the ceiling reached", [], omittedCharacterCount: 4096),
            TimeSpan.FromMilliseconds(10));

        // Assert
        var omitted = Assert.Single(collector.Read(OmittedInstrumentName, "mcp_snippet"));

        Assert.Equal(4096, omitted.Value);
    }

    /// <summary>A refusal an operator cannot see is a protection nobody can tell is in force.</summary>
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
        this.telemetry.RecordRefused(SensitiveContentEgressPoint.ChatPrompt, scanner);

        // Assert
        var refusal = Assert.Single(
            collector.Read(RefusalsInstrumentName, "chat_prompt"),
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

        /// <summary>Returns what one instrument published for one egress point, in order.</summary>
        internal IReadOnlyList<PublishedMeasurement> Read(string instrumentName, string egressPoint) =>
            [
                .. this.measurements.ToArray().Where(measurement =>
                    StringComparer.Ordinal.Equals(measurement.InstrumentName, instrumentName)
                    && measurement.Tags.TryGetValue(EgressPointTagName, out var point)
                    && Equals(point, egressPoint)),
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
