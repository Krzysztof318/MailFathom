// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent.Detection;

/// <summary>Covers what a finding is allowed to carry: a position, never a value, and an attribution that is reproducible.</summary>
public sealed class SensitiveContentFindingTests
{
    private static readonly SensitiveContentCategory CloudKey = SensitiveContentCategory.Create("CloudKey");
    private static readonly SensitiveContentRule AccessKeyRule = SensitiveContentRule.Create(CloudKey, "cloud-access-key");
    private static readonly SensitiveContentDetector Detector =
        SensitiveContentDetector.Create("in-process-secrets", "2026.08.01");

    private static readonly DateTimeOffset DetectedAt = new(2026, 8, 11, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_Finding_CarriesTheRuleCategoryPositionConfidenceDetectorAndTime()
    {
        // Act
        var finding = SensitiveContentFinding.Create(
            AccessKeyRule,
            SensitiveContentSpan.Create(12, 40),
            0.85,
            Detector,
            DetectedAt);

        // Assert
        Assert.Equal(AccessKeyRule, finding.Rule);
        Assert.Equal(CloudKey, finding.Category);
        Assert.Equal(12, finding.Span.Start);
        Assert.Equal(52, finding.Span.End);
        Assert.Equal(0.85, finding.Confidence);
        Assert.Equal("in-process-secrets", finding.Detector.Name);
        Assert.Equal("2026.08.01", finding.Detector.Revision);
        Assert.Equal(DetectedAt, finding.DetectedAt);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    public void Create_ConfidenceOutsideZeroToOne_IsRejected(double confidence)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SensitiveContentFinding.Create(
            AccessKeyRule,
            SensitiveContentSpan.Create(0, 1),
            confidence,
            Detector,
            DetectedAt));
    }

    /// <summary>The struct default describes no region, and redacting one would put a placeholder where nothing was found.</summary>
    [Fact]
    public void Create_UnspecifiedSpan_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SensitiveContentFinding.Create(
            AccessKeyRule,
            default,
            1,
            Detector,
            DetectedAt));
    }

    [Fact]
    public void Create_WithoutARuleOrADetector_IsRejected()
    {
        // Arrange
        var span = SensitiveContentSpan.Create(0, 1);

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => SensitiveContentFinding.Create(null!, span, 1, Detector, DetectedAt));
        Assert.Throws<ArgumentNullException>(() => SensitiveContentFinding.Create(AccessKeyRule, span, 1, null!, DetectedAt));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    public void Span_RegionNoFindingCouldCover_IsRejected(int start, int length)
    {
        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => SensitiveContentSpan.Create(start, length));
    }

    [Fact]
    public void Span_Default_DescribesNoRegion()
    {
        // Act
        var unspecified = default(SensitiveContentSpan);

        // Assert
        Assert.False(unspecified.IsSpecified);
        Assert.Equal("(unspecified)", unspecified.ToString());
    }

    /// <summary>Touching spans stay two placeholders; sharing a character is what makes one region out of two findings.</summary>
    [Theory]
    [InlineData(0, 5, 5, 5, false)]
    [InlineData(0, 5, 4, 5, true)]
    [InlineData(0, 10, 2, 3, true)]
    [InlineData(6, 4, 0, 5, false)]
    public void Span_Overlaps_IsTrueOnlyWhenACharacterIsShared(
        int firstStart,
        int firstLength,
        int secondStart,
        int secondLength,
        bool expected)
    {
        // Arrange
        var first = SensitiveContentSpan.Create(firstStart, firstLength);
        var second = SensitiveContentSpan.Create(secondStart, secondLength);

        // Act, Assert
        Assert.Equal(expected, first.Overlaps(second));
    }

    [Fact]
    public void Span_CoverWith_ProducesTheSmallestSpanCoveringBoth()
    {
        // Arrange
        var first = SensitiveContentSpan.Create(4, 6);
        var second = SensitiveContentSpan.Create(8, 10);

        // Act
        var covering = first.CoverWith(second);

        // Assert
        Assert.Equal(4, covering.Start);
        Assert.Equal(18, covering.End);
        Assert.Equal("[4, 18)", covering.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("-leading-dash")]
    public void Detector_UnacceptableIdentity_IsRejected(string value)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => SensitiveContentDetector.Create(value, "2026.08.01"));
        Assert.Throws<ArgumentException>(() => SensitiveContentDetector.Create("in-process-secrets", value));
    }

    /// <summary>Two deployments on one corpus redact identically, so the revision travels with the finding rather than being read from whatever is installed.</summary>
    [Fact]
    public void Detector_ToString_NamesBothTheDetectorAndItsRevision()
    {
        // Act, Assert
        Assert.Equal("in-process-secrets@2026.08.01", Detector.ToString());
    }
}
