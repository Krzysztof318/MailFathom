// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the recorder every guarded-egress test reads its assertions out of.</summary>
public sealed class RecordingSensitiveContentEgressTelemetryTests
{
    [Fact]
    public void RecordGuarded_SeveralGuardedTexts_AreKeptInTheOrderTheGuardsRan()
    {
        // Arrange
        var telemetry = new RecordingSensitiveContentEgressTelemetry();
        var redacted = RedactedText.Create("nothing here", [], omittedCharacterCount: 0);

        // Act
        telemetry.RecordGuarded(SensitiveContentEgressPoint.ChatPrompt, redacted, TimeSpan.FromMilliseconds(5));
        telemetry.RecordGuarded(SensitiveContentEgressPoint.McpSnippet, redacted, TimeSpan.FromMilliseconds(9));

        // Assert
        Assert.Equal(
            [
                (SensitiveContentEgressPoint.ChatPrompt, TimeSpan.FromMilliseconds(5)),
                (SensitiveContentEgressPoint.McpSnippet, TimeSpan.FromMilliseconds(9)),
            ],
            telemetry.Guarded.Select(guarded => (guarded.EgressPoint, guarded.Elapsed)));
        Assert.All(telemetry.Guarded, guarded => Assert.Same(redacted, guarded.Redacted));
    }

    [Fact]
    public void RecordRefused_SeveralRefusals_AreKeptInTheOrderTheyHappened()
    {
        // Arrange
        var telemetry = new RecordingSensitiveContentEgressTelemetry();

        // Act
        telemetry.RecordRefused(SensitiveContentEgressPoint.HostedEmbeddingInput, SensitiveContentScannerKind.Secrets);
        telemetry.RecordRefused(SensitiveContentEgressPoint.ChatPrompt, SensitiveContentScannerKind.Pii);

        // Assert
        Assert.Equal(
            [
                (SensitiveContentEgressPoint.HostedEmbeddingInput, SensitiveContentScannerKind.Secrets),
                (SensitiveContentEgressPoint.ChatPrompt, SensitiveContentScannerKind.Pii),
            ],
            telemetry.Refused.Select(refused => (refused.EgressPoint, refused.Scanner)));
        Assert.Empty(telemetry.Guarded);
    }

    [Fact]
    public void BeginGuardedOperation_WhatWasReportedIntoEachOperation_IsKeptOnTheOperationItBelongsTo()
    {
        // Arrange
        var telemetry = new RecordingSensitiveContentEgressTelemetry();

        // Act
        using (var refused = telemetry.BeginGuardedOperation(
            SensitiveContentEgressPoint.McpEmailContent,
            TestContext.Current.CancellationToken))
        {
            refused.TextGuarded();
            refused.TextGuarded();
            refused.Refused();
        }

        using (var stopped = telemetry.BeginGuardedOperation(
            SensitiveContentEgressPoint.McpSnippet,
            TestContext.Current.CancellationToken))
        {
            stopped.TextGuarded();
        }

        using (var succeeded = telemetry.BeginGuardedOperation(
            SensitiveContentEgressPoint.ChatPrompt,
            TestContext.Current.CancellationToken))
        {
            succeeded.Completed();
        }

        // Assert
        Assert.Equal(
            [
                (SensitiveContentEgressPoint.McpEmailContent, 2, true, false, true),
                (SensitiveContentEgressPoint.McpSnippet, 1, false, false, true),
                (SensitiveContentEgressPoint.ChatPrompt, 0, false, true, true),
            ],
            telemetry.Operations.Select(operation => (
                operation.EgressPoint,
                operation.GuardedTextCount,
                operation.WasRefused,
                operation.WasCompleted,
                operation.WasClosed)));
    }

    [Fact]
    public void RecordStopped_SeveralStoppedActs_AreKeptWithTheRefusalEachCarried()
    {
        // Arrange
        var telemetry = new RecordingSensitiveContentEgressTelemetry();
        var found = SensitiveContentEgressRefusal.ContentFound(
            SensitiveContentScannerKind.Secrets,
            SensitiveContentCategory.Create("CloudKey"));
        var unscanned = SensitiveContentEgressRefusal.NotFullyScanned();

        // Act
        telemetry.RecordStopped(SensitiveContentEgressPoint.OutgoingMail, found);
        telemetry.RecordStopped(SensitiveContentEgressPoint.OutgoingMail, unscanned);

        // Assert
        Assert.Equal(
            [
                (SensitiveContentEgressPoint.OutgoingMail, found),
                (SensitiveContentEgressPoint.OutgoingMail, unscanned),
            ],
            telemetry.Stopped.Select(stopped => (stopped.EgressPoint, stopped.Refusal)));
        Assert.Empty(telemetry.Guarded);
    }

    [Fact]
    public void RecordGuarded_NoRedaction_IsRefusedAsAnArgument()
    {
        // Arrange
        var telemetry = new RecordingSensitiveContentEgressTelemetry();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() =>
            telemetry.RecordGuarded(SensitiveContentEgressPoint.ChatPrompt, null!, TimeSpan.Zero));
    }
}
