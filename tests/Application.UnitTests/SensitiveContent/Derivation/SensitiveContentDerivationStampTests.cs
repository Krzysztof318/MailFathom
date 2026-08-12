// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.UnitTests.TestDoubles;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent.Derivation;

/// <summary>Covers what identifies the configuration a piece of derived data was written under.</summary>
public sealed class SensitiveContentDerivationStampTests
{
    private static readonly SensitiveContentCategory ProviderToken =
        SensitiveContentCategory.Create("ProviderToken");

    private static readonly SensitiveContentCategory PrivateKey = SensitiveContentCategory.Create("PrivateKey");

    [Fact]
    public void Compute_TheSameConfiguration_ProducesTheSameStamp()
    {
        // Arrange
        var scanner = Scanner(SensitiveContentScannerKind.Secrets, "corpus", "1");

        // Act
        var first = SensitiveContentDerivationStamp.Compute(Plan(scanner, ProviderToken), [scanner]);
        var second = SensitiveContentDerivationStamp.Compute(Plan(scanner, ProviderToken), [scanner]);

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(SensitiveContentDerivationStamp.Length, first.Value.Length);
    }

    /// <summary>Two operators who named the same categories in different orders configured the same deployment.</summary>
    [Fact]
    public void Compute_TheSameCategoriesInAnotherOrder_ProducesTheSameStamp()
    {
        // Arrange
        var scanner = Scanner(SensitiveContentScannerKind.Secrets, "corpus", "1");

        // Act
        var written = SensitiveContentDerivationStamp.Compute(Plan(scanner, ProviderToken, PrivateKey), [scanner]);
        var reordered = SensitiveContentDerivationStamp.Compute(Plan(scanner, PrivateKey, ProviderToken), [scanner]);

        // Assert
        Assert.Equal(written, reordered);
    }

    /// <summary>The case the whole stamp exists for: a widened set leaves every earlier row under-redacted.</summary>
    [Fact]
    public void Compute_AWidenedCategorySet_ProducesADifferentStamp()
    {
        // Arrange
        var scanner = Scanner(SensitiveContentScannerKind.Secrets, "corpus", "1");

        // Act
        var narrow = SensitiveContentDerivationStamp.Compute(Plan(scanner, ProviderToken), [scanner]);
        var widened = SensitiveContentDerivationStamp.Compute(Plan(scanner, ProviderToken, PrivateKey), [scanner]);

        // Assert
        Assert.NotEqual(narrow, widened);
    }

    /// <summary>The same categories under a newer corpus are a different redaction of the same message.</summary>
    [Fact]
    public void Compute_ANewerDetectorRevision_ProducesADifferentStamp()
    {
        // Arrange
        var before = Scanner(SensitiveContentScannerKind.Secrets, "corpus", "1");
        var after = Scanner(SensitiveContentScannerKind.Secrets, "corpus", "2");

        // Act
        var written = SensitiveContentDerivationStamp.Compute(Plan(before, ProviderToken), [before]);
        var upgraded = SensitiveContentDerivationStamp.Compute(Plan(after, ProviderToken), [after]);

        // Assert
        Assert.NotEqual(written, upgraded);
    }

    /// <summary>A rule silenced inside a category that stays on changes what is stored, so it changes the stamp.</summary>
    [Fact]
    public void Compute_ASuppressedRule_ProducesADifferentStamp()
    {
        // Arrange
        var scanner = Scanner(SensitiveContentScannerKind.Secrets, "corpus", "1");
        var withoutSuppression = Plan(scanner, ProviderToken);
        var withSuppression = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [
                SensitiveContentScannerPlan.Create(
                    scanner.Scanner,
                    [ProviderToken],
                    [SensitiveContentRule.Create(ProviderToken, "noisy-rule")]),
            ]);

        // Act
        var written = SensitiveContentDerivationStamp.Compute(withoutSuppression, [scanner]);
        var suppressed = SensitiveContentDerivationStamp.Compute(withSuppression, [scanner]);

        // Assert
        Assert.NotEqual(written, suppressed);
    }

    /// <summary>What one scan may spend decides throughput rather than placeholders, so it must not mark a mailbox stale.</summary>
    [Fact]
    public void Compute_ADifferentScanBudget_ProducesTheSameStamp()
    {
        // Arrange
        var scanner = Scanner(SensitiveContentScannerKind.Secrets, "corpus", "1");
        var widened = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Create(
                SensitiveContentScanBounds.Default.MaximumAnalyzedCharacters,
                TimeSpan.FromSeconds(45),
                SensitiveContentScanBounds.Default.MaximumConcurrentScans),
            [SensitiveContentScannerPlan.Create(scanner.Scanner, [ProviderToken], [])]);

        // Act
        var written = SensitiveContentDerivationStamp.Compute(Plan(scanner, ProviderToken), [scanner]);
        var retuned = SensitiveContentDerivationStamp.Compute(widened, [scanner]);

        // Assert
        Assert.Equal(written, retuned);
    }

    /// <summary>A stamp composed from a detector nothing registered would describe a redaction nothing performed.</summary>
    [Fact]
    public void Compute_AScannerNothingRegistered_RefusesToNameAConfigurationNothingRuns()
    {
        // Arrange
        var registered = Scanner(SensitiveContentScannerKind.Secrets, "corpus", "1");
        var plan = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [
                SensitiveContentScannerPlan.Create(SensitiveContentScannerKind.Secrets, [ProviderToken], []),
                SensitiveContentScannerPlan.Create(SensitiveContentScannerKind.Pii, [PrivateKey], []),
            ]);

        // Act
        var refusal = Assert.Throws<SensitiveContentScannerUnavailableException>(() =>
            SensitiveContentDerivationStamp.Compute(plan, [registered]));

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Pii, refusal.Scanner);
    }

    [Fact]
    public void Create_AValueThatIsNotADigest_IsRefused()
    {
        // Arrange
        const string notADigest = "REDACTED";

        // Act
        var refusal = Assert.Throws<ArgumentException>(() => SensitiveContentDerivationStamp.Create(notADigest));

        // Assert
        Assert.Equal("value", refusal.ParamName);
    }

    /// <summary>A row's stored stamp has to read back as the value the deployment would compute for itself.</summary>
    [Fact]
    public void Create_AStampThatWasComputed_ReadsBackAsTheSameValue()
    {
        // Arrange
        var scanner = Scanner(SensitiveContentScannerKind.Secrets, "corpus", "1");
        var computed = SensitiveContentDerivationStamp.Compute(Plan(scanner, ProviderToken), [scanner]);

        // Act
        var readBack = SensitiveContentDerivationStamp.Create(computed.Value);

        // Assert
        Assert.Equal(computed, readBack);
        Assert.Equal(computed.Value, readBack.ToString());
    }

    private static SensitiveContentPlan Plan(
        ScriptedSensitiveContentScanner scanner,
        params SensitiveContentCategory[] categories) =>
        SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [SensitiveContentScannerPlan.Create(scanner.Scanner, categories, [])]);

    private static ScriptedSensitiveContentScanner Scanner(
        SensitiveContentScannerKind kind,
        string detectorName,
        string revision) =>
        new(kind) { Detector = SensitiveContentDetector.Create(detectorName, revision) };
}
