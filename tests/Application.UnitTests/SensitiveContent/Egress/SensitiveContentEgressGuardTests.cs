// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.SensitiveContent.Egress;

/// <summary>Covers the one thing every egress point calls before it hands text to somebody else.</summary>
public sealed class SensitiveContentEgressGuardTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 12, 9, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task GuardAsync_ADetectedValue_IsReplacedBeforeTheTextIsHandedOn()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        // Act
        var guarded = await egress.Guard.GuardAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            $"the key is {Marker} and it works",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("the key is [redacted:CloudKey] and it works", guarded);
    }

    /// <summary>An opt-in nobody took must not appear on any path, so an inactive guard constructs nothing and scans nothing.</summary>
    [Fact]
    public async Task GuardAsync_ADeploymentThatScansNothing_HandsTheTextBackUnchanged()
    {
        // Arrange
        var guard = SensitiveContentEgressGuards.Inactive();

        // Act
        var guarded = await guard.GuardAsync(
            SensitiveContentEgressPoint.McpSnippet,
            $"the key is {Marker}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(guard.IsActive);
        Assert.Equal($"the key is {Marker}", guarded);
    }

    /// <summary>An opt-in that degraded to handing the text on under load would be worse than no switch at all.</summary>
    [Fact]
    public async Task GuardAsync_ADetectorThatCannotAnswer_RefusesTheEgressRatherThanServingItUnscanned()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(this.timeProvider);

        // Act
        var refusal = await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            egress.Guard.GuardAsync(
                SensitiveContentEgressPoint.HostedEmbeddingInput,
                "whatever the message said",
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(SensitiveContentScannerKind.Secrets, refusal.Scanner);
        Assert.DoesNotContain("whatever the message said", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>A refusal an operator cannot see is a protection nobody can tell is in force.</summary>
    [Fact]
    public async Task GuardAsync_ADetectorThatCannotAnswer_ReportsTheRefusalAgainstTheEgressPoint()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Unavailable(this.timeProvider);

        // Act
        await Assert.ThrowsAsync<SensitiveContentScannerUnavailableException>(() =>
            egress.Guard.GuardAsync(
                SensitiveContentEgressPoint.HostedEmbeddingInput,
                "whatever the message said",
                TestContext.Current.CancellationToken));

        // Assert
        var refused = Assert.Single(egress.Telemetry.Refused);
        Assert.Equal(SensitiveContentEgressPoint.HostedEmbeddingInput, refused.EgressPoint);
        Assert.Equal(SensitiveContentScannerKind.Secrets, refused.Scanner);
        Assert.Empty(egress.Telemetry.Guarded);
    }

    /// <summary>The findings are what an operator splits by category, and none of them may carry what was found.</summary>
    [Fact]
    public async Task GuardAsync_ADetectedValue_ReportsTheFindingAgainstTheEgressPointAndItsCategory()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        // Act
        await egress.Guard.GuardAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            $"{Marker} and {Marker}",
            TestContext.Current.CancellationToken);

        // Assert
        var guarded = Assert.Single(egress.Telemetry.Guarded);
        Assert.Equal(SensitiveContentEgressPoint.ChatPrompt, guarded.EgressPoint);
        Assert.Equal(
            ["CloudKey", "CloudKey"],
            guarded.Redacted.Findings.Select(finding => finding.Category.Name));
    }

    /// <summary>A publication is several values, and each is scanned on its own so no detection can straddle two of them.</summary>
    [Fact]
    public async Task GuardAllAsync_SeveralTexts_GuardsEachOneOnItsOwnAndKeepsTheOrder()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        // Act
        var guarded = await egress.Guard.GuardAllAsync(
            SensitiveContentEgressPoint.McpSnippet,
            [$"first {Marker}", "second", $"third {Marker}"],
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ["first [redacted:CloudKey]", "second", "third [redacted:CloudKey]"],
            guarded);
        Assert.Equal([$"first {Marker}", "second", $"third {Marker}"], egress.Scanner.ScannedTexts);
    }

    [Fact]
    public async Task GuardAllAsync_ADeploymentThatScansNothing_HandsTheTextsBackUnchanged()
    {
        // Arrange
        var guard = SensitiveContentEgressGuards.Inactive();
        IReadOnlyList<string> texts = [$"first {Marker}", "second"];

        // Act
        var guarded = await guard.GuardAllAsync(
            SensitiveContentEgressPoint.McpSnippet,
            texts,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(texts, guarded);
    }

    /// <summary>A subject nobody wrote and a subject redacted to nothing are different facts, and a reader acts differently on each.</summary>
    [Fact]
    public async Task GuardOptionalAsync_NoTextAtAll_StaysAbsentRatherThanBecomingEmpty()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        // Act
        var guarded = await egress.Guard.GuardOptionalAsync(
            SensitiveContentEgressPoint.McpSnippet,
            text: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(guarded);
        Assert.Empty(egress.Scanner.ScannedTexts);
    }

    [Fact]
    public async Task GuardOptionalAsync_ADetectedValue_IsReplacedBeforeTheTextIsHandedOn()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        // Act
        var guarded = await egress.Guard.GuardOptionalAsync(
            SensitiveContentEgressPoint.McpSnippet,
            $"re: {Marker}",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("re: [redacted:CloudKey]", guarded);
    }

    [Fact]
    public async Task GuardAsync_NoText_IsRefusedAsAnArgument()
    {
        // Arrange
        var guard = SensitiveContentEgressGuards.Inactive();

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => guard.GuardAsync(
            SensitiveContentEgressPoint.ChatPrompt,
            null!,
            TestContext.Current.CancellationToken));
    }
}
