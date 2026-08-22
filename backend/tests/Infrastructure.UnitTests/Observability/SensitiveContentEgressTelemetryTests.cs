// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
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
    private const string StoppedInstrumentName = "mailfathom.sensitive_content.stopped";
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
        using var measurements = new RecordedMailFathomMeasurements(GuardedInstrumentName, DurationInstrumentName);

        // Act
        this.telemetry.RecordGuarded(
            SensitiveContentEgressPoint.McpSnippet,
            RedactedText.Create("nothing here", [], omittedCharacterCount: 0),
            TimeSpan.FromMilliseconds(250));

        // Assert
        Assert.Equal([1d], measurements.ValuesOf(GuardedInstrumentName));
        Assert.Equal(["mcp_snippet"], measurements.DimensionOf(GuardedInstrumentName, EgressPointTagName));
        Assert.Equal([0.25d], measurements.ValuesOf(DurationInstrumentName));
        Assert.Equal(["mcp_snippet"], measurements.DimensionOf(DurationInstrumentName, EgressPointTagName));
    }

    /// <summary>Which kind of material a mailbox produces is what decides whether a category list is right.</summary>
    [Fact]
    public void RecordGuarded_FindingsOfSeveralCategories_CountsEachCategoryOnItsOwnSeries()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(FindingsInstrumentName);

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
        var findings = measurements.Read(FindingsInstrumentName);

        Assert.All(
            measurements.DimensionOf(FindingsInstrumentName, EgressPointTagName),
            egressPoint => Assert.Equal("chat_prompt", egressPoint));
        Assert.Equal(
            [("CloudKey", 2d), ("EmailAddress", 1d)],
            findings.Select(finding => (finding.Tags[CategoryTagName] as string, finding.Value)).Order());
    }

    /// <summary>A zero on every guarded text would make the series say the ceiling is in play on ordinary mail.</summary>
    [Fact]
    public void RecordGuarded_ATextTheCeilingDidNotCut_ReportsNoOmission()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(OmittedInstrumentName);

        // Act
        this.telemetry.RecordGuarded(
            SensitiveContentEgressPoint.HostedEmbeddingInput,
            RedactedText.Create("nothing here", [], omittedCharacterCount: 0),
            TimeSpan.FromMilliseconds(10));

        // Assert
        Assert.Empty(measurements.Read(OmittedInstrumentName));
    }

    /// <summary>Text nothing analyzed is exactly the text that must not leave, so an operator is told how much of it there was.</summary>
    [Fact]
    public void RecordGuarded_ATextTheCeilingCut_ReportsHowMuchWasDropped()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(OmittedInstrumentName);

        // Act
        this.telemetry.RecordGuarded(
            SensitiveContentEgressPoint.McpSnippet,
            RedactedText.Create("as far as the ceiling reached", [], omittedCharacterCount: 4096),
            TimeSpan.FromMilliseconds(10));

        // Assert
        Assert.Equal([4096d], measurements.ValuesOf(OmittedInstrumentName));
        Assert.Equal(["mcp_snippet"], measurements.DimensionOf(OmittedInstrumentName, EgressPointTagName));
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
        using var measurements = new RecordedMailFathomMeasurements(RefusalsInstrumentName);
        var scanner = Enum.Parse<SensitiveContentScannerKind>(scannerName);

        // Act
        this.telemetry.RecordRefused(SensitiveContentEgressPoint.ChatPrompt, scanner);

        // Assert
        var refusal = Assert.Single(
            measurements.Read(RefusalsInstrumentName),
            measurement => Equals(measurement.Tags[ScannerTagName], expectedTag));

        Assert.Equal(1, refusal.Value);
        Assert.Equal("chat_prompt", refusal.Tags[EgressPointTagName]);
    }

    /// <summary>A dashboard and an alert are written against the tag value, so every point has to publish its own.</summary>
    [Theory]
    [InlineData(nameof(SensitiveContentEgressPoint.ChatPrompt), "chat_prompt")]
    [InlineData(nameof(SensitiveContentEgressPoint.HostedEmbeddingInput), "hosted_embedding_input")]
    [InlineData(nameof(SensitiveContentEgressPoint.McpSnippet), "mcp_snippet")]
    [InlineData(nameof(SensitiveContentEgressPoint.McpEmailContent), "mcp_email_content")]
    [InlineData(nameof(SensitiveContentEgressPoint.OutgoingMail), "outgoing_mail")]
    public void RecordGuarded_EachEgressPoint_PublishesItsOwnTagValue(string egressPointName, string expectedTag)
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(GuardedInstrumentName);
        var egressPoint = Enum.Parse<SensitiveContentEgressPoint>(egressPointName);

        // Act
        this.telemetry.RecordGuarded(
            egressPoint,
            RedactedText.Create("nothing here", [], omittedCharacterCount: 0),
            TimeSpan.FromMilliseconds(10));

        // Assert
        Assert.Equal([1d], measurements.ValuesOf(GuardedInstrumentName));
        Assert.Equal([expectedTag], measurements.DimensionOf(GuardedInstrumentName, EgressPointTagName));
    }

    /// <summary>An act nobody was allowed to perform is the one thing this family does that a redaction never shows.</summary>
    [Fact]
    public void RecordStopped_AnActStoppedForWhatWasFound_CountsItAgainstTheScannerAndTheCategory()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(StoppedInstrumentName);

        // Act
        this.telemetry.RecordStopped(
            SensitiveContentEgressPoint.OutgoingMail,
            SensitiveContentEgressRefusal.ContentFound(
                SensitiveContentScannerKind.Secrets,
                SensitiveContentCategory.Create("CloudKey")));

        // Assert
        var stopped = Assert.Single(measurements.Read(StoppedInstrumentName));

        Assert.Equal(1, stopped.Value);
        Assert.Equal("outgoing_mail", stopped.Tags[EgressPointTagName]);
        Assert.Equal("secrets", stopped.Tags[ScannerTagName]);
        Assert.Equal("CloudKey", stopped.Tags[CategoryTagName]);
    }

    /// <summary>
    /// A length refusal names no scanner and no category, and both tags are written all the same: an operator summing
    /// this counter by scanner would otherwise lose every act stopped because nothing read the whole text.
    /// </summary>
    [Fact]
    public void RecordStopped_AnActStoppedBecauseNothingReadItAll_StillPublishesBothDimensions()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(StoppedInstrumentName);

        // Act
        this.telemetry.RecordStopped(
            SensitiveContentEgressPoint.OutgoingMail,
            SensitiveContentEgressRefusal.NotFullyScanned());

        // Assert
        var stopped = Assert.Single(measurements.Read(StoppedInstrumentName));

        Assert.Equal("not_scanned", stopped.Tags[ScannerTagName]);
        Assert.Equal("not_scanned", stopped.Tags[CategoryTagName]);
    }

    private static SensitiveContentFinding FindingOf(string category, int start) =>
        SensitiveContentFinding.Create(
            SensitiveContentRule.Create(SensitiveContentCategory.Create(category), $"{category}-rule"),
            SensitiveContentSpan.Create(start, length: 8),
            confidence: 1,
            SensitiveContentDetector.Create("test", "1"),
            DetectedAt);
}
