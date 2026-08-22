// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the switched-on deployment every guarded egress point is exercised against.</summary>
public sealed class ScanningSensitiveContentEgressTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Finding_ADeploymentThatDetectsTheMarker_ReplacesItAndReportsWhatItFound()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        // Act
        var guarded = await egress.Guard.GuardAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            $"the key is {Marker}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(egress.Guard.IsActive);
        Assert.Equal("the key is [redacted:CloudKey]", guarded);
        Assert.Equal([$"the key is {Marker}"], egress.Scanner.ScannedTexts);

        var recorded = Assert.Single(egress.Telemetry.Guarded);

        Assert.Equal(SensitiveContentEgressPoint.ChatPrompt, recorded.EgressPoint);
    }

    /// <summary>A consumer has to behave identically under either switch, so either one can be the deployment's.</summary>
    [Theory]
    [InlineData(SensitiveContentScannerKind.Secrets)]
    [InlineData(SensitiveContentScannerKind.Pii)]
    public async Task Finding_ADeploymentWithOneSwitchOn_ScansUnderTheSwitchItWasGiven(
        SensitiveContentScannerKind scanner)
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider, scanner);

        // Act
        var guarded = await egress.Guard.GuardAsync(
            SensitiveContentEgressPoint.McpEmailContent,
            $"the key is {Marker}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(scanner, egress.Scanner.Scanner);
        Assert.Equal("the key is [redacted:CloudKey]", guarded);
    }

    /// <summary>The analyzed ceiling is the one bound that truncates rather than refusing, so a test has to be able to reach it.</summary>
    [Fact]
    public async Task Finding_ADeploymentWhoseCeilingCutsTheText_DropsWhatItDidNotAnalyze()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(
            Marker,
            this.timeProvider,
            bounds: SensitiveContentScanBounds.Create(8, TimeSpan.FromSeconds(5), 4));

        // Act
        var guarded = await egress.Guard.GuardWithOmissionAsync(
            SensitiveContentEgressPoint.McpEmailContent,
            "12345678 and everything after it",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("12345678", guarded.Text);
        Assert.Equal(24, guarded.OmittedCharacterCount);
    }

    [Fact]
    public async Task Unavailable_ADeploymentWhoseDetectorCannotAnswer_RefusesEveryTextItIsHanded()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(this.timeProvider);

        // Act
        var refusal = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            egress.Guard.GuardAsync(
                SensitiveContentEgressPoint.McpSnippet,
                "whatever the message said",
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Secrets, refusal.Scanner);
        Assert.Equal(
            [SensitiveContentEgressPoint.McpSnippet],
            egress.Telemetry.Refused.Select(recorded => recorded.EgressPoint));
    }

    /// <summary>
    /// The screen shares the deployment's redactor and its plan, and screens for whichever scanner the deployment was
    /// built around — a screen built for the other one would let every test's marker through.
    /// </summary>
    [Theory]
    [InlineData(SensitiveContentScannerKind.Secrets)]
    [InlineData(SensitiveContentScannerKind.Pii)]
    public async Task Screen_ADeploymentWithOneSwitchOn_StopsAnActUnderTheSwitchItWasGiven(
        SensitiveContentScannerKind scanner)
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider, scanner);

        // Act
        var refusal = await egress.Screen.ScreenAsync(
            SensitiveContentEgressPoint.OutgoingMail,
            [$"the key is {Marker}"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(egress.Screen.IsActive);
        Assert.NotNull(refusal);
        Assert.Equal(scanner, refusal.Scanner);
    }

    /// <summary>The redactor holds a deployment's concurrency permits, so the holder releases it rather than the test.</summary>
    [Fact]
    public async Task Dispose_ADeploymentATestFinishedWith_ReleasesTheRedactionItHeld()
    {
        // Arrange
        var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        // Act
        egress.Dispose();

        // Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            egress.Guard.GuardAsync(
                SensitiveContentEgressPoint.ChatPrompt,
                Marker,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Finding_NoClock_IsRefusedAsAnArgument() =>

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ScanningSensitiveContentEgress.Finding(Marker, null!));

    [Fact]
    public void Unavailable_NoClock_IsRefusedAsAnArgument() =>

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => ScanningSensitiveContentEgress.Unavailable(null!));
}
