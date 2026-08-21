// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Infrastructure.Observability;
using MailFathom.TestSupport;
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
        using var measurements = new RecordedMailFathomMeasurements(RedactedInstrumentName, DurationInstrumentName);

        // Act
        this.telemetry.RecordDerived(
            RedactedText.Create("nothing here", [], omittedCharacterCount: 0),
            TimeSpan.FromMilliseconds(250));

        // Assert
        Assert.Equal([1d], measurements.ValuesOf(RedactedInstrumentName));
        Assert.Equal([0.25d], measurements.ValuesOf(DurationInstrumentName));
    }

    /// <summary>Which kind of material a mailbox produces is what decides whether a category list is right.</summary>
    [Fact]
    public void RecordDerived_FindingsOfSeveralCategories_CountsEachCategoryOnItsOwnSeries()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(FindingsInstrumentName);

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
        var findings = measurements.Read(FindingsInstrumentName);

        Assert.Equal(
            [("CloudKey", 2d), ("EmailAddress", 1d)],
            findings.Select(finding => (finding.Tags[CategoryTagName] as string, finding.Value)).Order());
    }

    /// <summary>A zero on every derived write would make the series say the ceiling is in play on ordinary mail.</summary>
    [Fact]
    public void RecordDerived_ATextTheCeilingDidNotCut_ReportsNoOmission()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(OmittedInstrumentName);

        // Act
        this.telemetry.RecordDerived(
            RedactedText.Create("nothing here", [], omittedCharacterCount: 0),
            TimeSpan.FromMilliseconds(10));

        // Assert
        Assert.Empty(measurements.Read(OmittedInstrumentName));
    }

    /// <summary>What the ceiling cuts here is cut out of the index for as long as the message stays derived.</summary>
    [Fact]
    public void RecordDerived_ATextTheCeilingCut_ReportsHowMuchWasDropped()
    {
        // Arrange
        using var measurements = new RecordedMailFathomMeasurements(OmittedInstrumentName);

        // Act
        this.telemetry.RecordDerived(
            RedactedText.Create("as far as the ceiling reached", [], omittedCharacterCount: 4096),
            TimeSpan.FromMilliseconds(10));

        // Assert
        Assert.Equal([4096d], measurements.ValuesOf(OmittedInstrumentName));
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
        using var measurements = new RecordedMailFathomMeasurements(RefusalsInstrumentName);
        var scanner = Enum.Parse<SensitiveContentScannerKind>(scannerName);

        // Act
        this.telemetry.RecordRefused(scanner);

        // Assert
        var refusal = Assert.Single(
            measurements.Read(RefusalsInstrumentName),
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
}
