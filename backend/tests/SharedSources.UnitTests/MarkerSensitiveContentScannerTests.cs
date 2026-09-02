// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the detector every guarded-egress test puts a "credential" through.</summary>
public sealed class MarkerSensitiveContentScannerTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly DateTimeOffset ScannedAt = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ScanAsync_TextCarryingTheMarkerTwice_ReportsBothOccurrencesWhereTheyAre()
    {
        // Arrange
        var scanner = ScannerOver(Marker);

        // Act
        var findings = await scanner.ScanAsync(
            $"{Marker} and then {Marker}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [(0, Marker.Length), (Marker.Length + 10, Marker.Length)],
            findings.Select(finding => (finding.Span.Start, finding.Span.Length)));
        Assert.All(findings, finding => Assert.Equal("CloudKey", finding.Category.Name));
    }

    [Fact]
    public async Task ScanAsync_TextCarryingNothingTheScannerKnows_ReportsNoFinding()
    {
        // Arrange
        var scanner = ScannerOver(Marker);

        // Act
        var findings = await scanner.ScanAsync("nothing here", TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(findings);
    }

    /// <summary>Every text a guard handed on is recorded, which is how a test proves a value was scanned on its own.</summary>
    [Fact]
    public async Task ScanAsync_SeveralTexts_RecordsEachOneInTheOrderItArrived()
    {
        // Arrange
        var scanner = ScannerOver(Marker);

        // Act
        await scanner.ScanAsync("first", TestContext.Current.CancellationToken);
        await scanner.ScanAsync("second", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["first", "second"], scanner.ScannedTexts);
    }

    /// <summary>The failure is what makes this scanner the one thing a fail-closed test needs.</summary>
    [Fact]
    public async Task ScanAsync_AScannerGivenAFailure_RaisesItInsteadOfAnswering()
    {
        // Arrange
        var scanner = ScannerOver(Marker);
        scanner.Failure = new InvalidOperationException("The detector is not answering.");

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scanner.ScanAsync(Marker, TestContext.Current.CancellationToken));
    }

    /// <summary>What a caller times around a scan is nothing at all unless the scan can be made to take time.</summary>
    [Fact]
    public async Task ScanAsync_AScannerGivenSomethingToDoWhileScanning_RunsItBeforeItAnswers()
    {
        // Arrange
        var timeProvider = new FakeTimeProvider(ScannedAt);
        var scanner = new MarkerSensitiveContentScanner(Marker, SensitiveContentScannerKind.Secrets, timeProvider);
        scanner.WhileScanning = () => timeProvider.Advance(TimeSpan.FromMilliseconds(250));

        // Act
        var findings = await scanner.ScanAsync(Marker, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ScannedAt.AddMilliseconds(250), timeProvider.GetUtcNow());

        // Run before the answer rather than after it, so a finding is stamped with the clock the caller will read.
        Assert.Equal(ScannedAt.AddMilliseconds(250), Assert.Single(findings).DetectedAt);
    }

    /// <summary>A scanner nobody gave anything to do must answer rather than fail on an absent callback.</summary>
    [Fact]
    public async Task ScanAsync_AScannerGivenNothingToDoWhileScanning_AnswersAsItAlwaysDid()
    {
        // Arrange
        var scanner = ScannerOver(Marker);

        // Act
        var findings = await scanner.ScanAsync(Marker, TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(findings);
    }

    [Fact]
    public async Task ScanAsync_ACallerThatCancelled_StopsBeforeItScans()
    {
        // Arrange
        var scanner = ScannerOver(Marker);
        using var cancellation = new CancellationTokenSource();

        await cancellation.CancelAsync();

        // Act, Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scanner.ScanAsync(Marker, cancellation.Token));
        Assert.Empty(scanner.ScannedTexts);
    }

    [Fact]
    public void Constructor_NoMarkerToLookFor_IsRefusedAsAnArgument() =>

        // Act, Assert
        Assert.Throws<ArgumentException>(() => ScannerOver(string.Empty));

    /// <summary>Which switch a scanner answers for is what a refusal names, so it is carried rather than assumed.</summary>
    [Theory]
    [InlineData(nameof(SensitiveContentScannerKind.Secrets))]
    [InlineData(nameof(SensitiveContentScannerKind.Pii))]
    public void Scanner_TheKindItWasBuiltFor_IsWhatItReports(string scannerName)
    {
        // Arrange
        var expected = Enum.Parse<SensitiveContentScannerKind>(scannerName);

        // Act
        var scanner = new MarkerSensitiveContentScanner(Marker, expected, TimeProvider.System);

        // Assert
        Assert.Equal(expected, scanner.Scanner);
    }

    private static MarkerSensitiveContentScanner ScannerOver(string marker) =>
        new(marker, SensitiveContentScannerKind.Secrets, new FakeTimeProvider(ScannedAt));
}
