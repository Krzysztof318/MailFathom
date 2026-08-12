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
