// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Failures;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent.Redaction;

/// <summary>Covers the one implementation both the derived path and the read path redact through.</summary>
public sealed class SensitiveContentRedactorTests : IDisposable
{
    private static readonly SensitiveContentCategory CloudKey = SensitiveContentCategory.Create("CloudKey");
    private static readonly SensitiveContentCategory PersonName = SensitiveContentCategory.Create("PersonName");
    private static readonly SensitiveContentDetector Detector =
        SensitiveContentDetector.Create("in-process-secrets", "2026.08.01");

    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 11, 10, 0, 0, TimeSpan.Zero));
    private readonly List<SensitiveContentScanConcurrency> openedPermits = [];

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var permits in this.openedPermits)
        {
            permits.Dispose();
        }
    }

    [Fact]
    public async Task RedactAsync_DetectedRegion_IsReplacedByThePlaceholderNamingItsCategory()
    {
        // Arrange
        var scanner = new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Secrets)
        {
            Findings = [this.Finding(CloudKey, 11, 4)],
        };
        var redactor = this.Redactor(SensitiveContentScanBounds.Default, scanner);

        // Act
        var redacted = await redactor.RedactAsync("the key is AKIA and it works", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the key is [redacted:CloudKey] and it works", redacted.Text);
        Assert.True(redacted.IsRedacted);
        Assert.Equal(0, redacted.OmittedCharacterCount);
    }

    [Fact]
    public async Task RedactAsync_NothingDetected_ReturnsTheTextUnchanged()
    {
        // Arrange
        var scanner = new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Secrets);
        var redactor = this.Redactor(SensitiveContentScanBounds.Default, scanner);

        // Act
        var redacted = await redactor.RedactAsync("nothing to see here", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("nothing to see here", redacted.Text);
        Assert.False(redacted.IsRedacted);
        Assert.Empty(redacted.Findings);
    }

    /// <summary>A citation from a redacted chunk lands on the reader's redacted text only if the same input always redacts identically.</summary>
    [Fact]
    public async Task RedactAsync_SameInputAndPlan_ProducesTheSameTextOnRepeat()
    {
        // Arrange
        var secrets = new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Secrets)
        {
            Findings = [this.Finding(CloudKey, 24, 3), this.Finding(CloudKey, 4, 3)],
        };
        var personalData = new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Pii)
        {
            Findings = [this.Finding(PersonName, 14, 4, "sidecar-personal-data", "2.2.355")],
        };
        var redactor = this.Redactor(SensitiveContentScanBounds.Default, secrets, personalData);
        const string text = "one two three four five six seven";

        // Act
        var first = await redactor.RedactAsync(text, TestContext.Current.CancellationToken);
        var second = await redactor.RedactAsync(text, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first.Text, second.Text);
        Assert.Equal(
            "one [redacted:CloudKey] three [redacted:PersonName] five [redacted:CloudKey] seven",
            first.Text);
    }

    /// <summary>Dropping the second finding would leave the part of it reaching past the first in the text that is handed on.</summary>
    [Fact]
    public async Task RedactAsync_OverlappingFindings_LeaveNoCoveredCharacterBehind()
    {
        // Arrange
        var scanner = new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Secrets)
        {
            Findings = [this.Finding(CloudKey, 4, 5), this.Finding(PersonName, 6, 6)],
        };
        var redactor = this.Redactor(SensitiveContentScanBounds.Default, scanner);

        // Act
        var redacted = await redactor.RedactAsync("abcdefghijklmnop", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("abcd[redacted:CloudKey]mnop", redacted.Text);
        Assert.Equal(2, redacted.Findings.Count);
    }

    /// <summary>Text nothing analyzed is exactly the text that must not leave, so the ceiling truncates rather than admitting a remainder.</summary>
    [Fact]
    public async Task RedactAsync_TextBeyondTheAnalyzedCeiling_IsDroppedAndReported()
    {
        // Arrange
        var scanner = new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Secrets);
        var bounds = SensitiveContentScanBounds.Create(8, TimeSpan.FromSeconds(5), 4);
        var redactor = this.Redactor(bounds, scanner);

        // Act
        var redacted = await redactor.RedactAsync("0123456789abc", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("01234567", redacted.Text);
        Assert.Equal(5, redacted.OmittedCharacterCount);
        Assert.Equal("01234567", Assert.Single(scanner.ScannedTexts));
    }

    /// <summary>An unpaired surrogate is text no encoder can represent, so the cut moves back rather than splitting a character.</summary>
    [Fact]
    public async Task RedactAsync_CeilingFallingInsideACharacter_DropsThatCharacterWhole()
    {
        // Arrange
        var scanner = new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Secrets);
        var bounds = SensitiveContentScanBounds.Create(5, TimeSpan.FromSeconds(5), 4);
        var redactor = this.Redactor(bounds, scanner);

        // Act
        var redacted = await redactor.RedactAsync("abcd\U0001F600e", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("abcd", redacted.Text);
        Assert.Equal(3, redacted.OmittedCharacterCount);
    }

    [Fact]
    public async Task RedactAsync_ScannerThatFails_RefusesTheOperationRatherThanServingTheText()
    {
        // Arrange
        var scanner = new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Secrets)
        {
            Failure = new InvalidOperationException("the corpus did not load"),
        };
        var redactor = this.Redactor(SensitiveContentScanBounds.Default, scanner);

        // Act
        var failure = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(
            () => redactor.RedactAsync("a secret", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.SensitiveContentScannerUnavailable, failure.ErrorCode);
        Assert.Equal(SensitiveContentScannerKind.Secrets, failure.Scanner);
        Assert.DoesNotContain("corpus did not load", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Nobody the redaction can see cancelled, so this is a scanner that did not answer rather than a budget that was spent.</summary>
    [Fact]
    public async Task RedactAsync_ScannerThatCancelsItself_RefusesTheOperationWithoutNamingABudget()
    {
        // Arrange
        var scanner = new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Secrets)
        {
            Failure = new OperationCanceledException("the adapter gave up"),
        };
        var redactor = this.Redactor(SensitiveContentScanBounds.Default, scanner);

        // Act
        var failure = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(
            () => redactor.RedactAsync("a secret", TestContext.Current.CancellationToken));

        // Assert
        Assert.DoesNotContain("within", failure.Message, StringComparison.Ordinal);
        Assert.IsType<OperationCanceledException>(failure.InnerException);
    }

    [Fact]
    public async Task RedactAsync_ScannerThatOverrunsItsBudget_RefusesTheOperation()
    {
        // Arrange
        var scanner = new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Secrets) { NeverAnswers = true };
        var bounds = SensitiveContentScanBounds.Create(1_000, TimeSpan.FromSeconds(3), 4);
        var redactor = this.Redactor(bounds, scanner);

        // Act
        var redaction = redactor.RedactAsync("a secret", TestContext.Current.CancellationToken);
        await scanner.Entered;
        this.timeProvider.Advance(TimeSpan.FromSeconds(3));

        // Assert
        var failure = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() => redaction);
        Assert.Contains("00:00:03", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A shutting-down host and a detector that stopped answering are different facts, so a caller's own cancellation stays one.</summary>
    [Fact]
    public async Task RedactAsync_CallerCancels_RaisesCancellationRatherThanAScannerFailure()
    {
        // Arrange
        var scanner = new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Secrets) { NeverAnswers = true };
        var redactor = this.Redactor(SensitiveContentScanBounds.Default, scanner);
        using var caller = new CancellationTokenSource();

        // Act
        var redaction = redactor.RedactAsync("a secret", caller.Token);
        await scanner.Entered;
        await caller.CancelAsync();

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => redaction);
    }

    /// <summary>Startup already refuses this, so reaching it means a detector disappeared under a running deployment.</summary>
    [Fact]
    public async Task RedactAsync_PlannedScannerNothingRegistered_RefusesTheOperation()
    {
        // Arrange
        var plan = SensitiveContentPlan.Create(
            SensitiveContentScanBounds.Default,
            [SensitiveContentScannerPlan.Create(SensitiveContentScannerKind.Pii, [PersonName], [])]);
        var redactor = new SensitiveContentRedactor(plan, [], this.timeProvider, this.Permits(plan.Bounds));

        // Act
        var failure = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(
            () => redactor.RedactAsync("a name", TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Pii, failure.Scanner);
    }

    /// <summary>Clamping a stray span would redact a region nothing found while leaving whatever the detector meant untouched.</summary>
    [Fact]
    public async Task RedactAsync_FindingBeyondTheTextItWasHanded_RefusesTheOperation()
    {
        // Arrange
        var scanner = new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Secrets)
        {
            Findings = [this.Finding(CloudKey, 4, 40)],
        };
        var redactor = this.Redactor(SensitiveContentScanBounds.Default, scanner);

        // Act, Assert
        await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(
            () => redactor.RedactAsync("short text", TestContext.Current.CancellationToken));
    }

    /// <summary>A message a caller hands over may be large, so what may run at once is bounded rather than left to the callers.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RedactAsync_ConcurrentCallers_NeverExceedTheConfiguredConcurrency(int permits)
    {
        // Arrange
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scanner = new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Secrets) { Gate = gate };
        var bounds = SensitiveContentScanBounds.Create(1_000, TimeSpan.FromSeconds(30), permits);
        var redactor = this.Redactor(bounds, scanner);

        // Act
        var first = redactor.RedactAsync("one", TestContext.Current.CancellationToken);
        await scanner.Entered;
        var second = redactor.RedactAsync("two", TestContext.Current.CancellationToken);
        gate.SetResult();
        await Task.WhenAll(first, second);

        // Assert
        Assert.Equal(permits, scanner.PeakConcurrentCalls);
    }

    [Fact]
    public async Task RedactAsync_WithoutText_IsRejected()
    {
        // Arrange
        var redactor = this.Redactor(
            SensitiveContentScanBounds.Default,
            new ScriptedSensitiveContentScanner(SensitiveContentScannerKind.Secrets));

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => redactor.RedactAsync(null!, TestContext.Current.CancellationToken));
    }

    private SensitiveContentRedactor Redactor(
        SensitiveContentScanBounds bounds,
        params ScriptedSensitiveContentScanner[] scanners) => new(
        SensitiveContentPlan.Create(
            bounds,
            [.. scanners.Select(scanner => SensitiveContentScannerPlan.Create(
                scanner.Scanner,
                [scanner.Scanner == SensitiveContentScannerKind.Secrets ? CloudKey : PersonName],
                []))]),
        scanners,
        this.timeProvider,
        this.Permits(bounds));

    /// <summary>Opens the permits one redaction runs under, and keeps them for this test class to release.</summary>
    /// <remarks>
    /// The budget outlives the redaction rather than belonging to it — a deployment holds one for every posture — so a
    /// test holds it too instead of scoping it to the redactor it is about to build.
    /// </remarks>
    private SensitiveContentScanConcurrency Permits(SensitiveContentScanBounds bounds)
    {
        var permits = new SensitiveContentScanConcurrency(bounds.MaximumConcurrentScans);

        this.openedPermits.Add(permits);

        return permits;
    }

    private SensitiveContentFinding Finding(
        SensitiveContentCategory category,
        int start,
        int length,
        string detector = "in-process-secrets",
        string revision = "2026.08.01") => SensitiveContentFinding.Create(
        SensitiveContentRule.Create(category, "only-rule"),
        SensitiveContentSpan.Create(start, length),
        1,
        detector == Detector.Name && revision == Detector.Revision
            ? Detector
            : SensitiveContentDetector.Create(detector, revision),
        this.timeProvider.GetUtcNow());
}
