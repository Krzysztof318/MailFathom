// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Screening;

/// <summary>Covers what the outbox and the draft book ask before either of them writes anything down.</summary>
public sealed class OutgoingMailScreeningTests
{
    private const string Marker = "AKIAEXAMPLEKEY";

    private static readonly ReadOnlyMemory<byte> RawMime =
        Encoding.ASCII.GetBytes("Subject: a message\r\n\r\nHello.").AsMemory();

    private readonly FakeTimeProvider timeProvider = new(new DateTimeOffset(2026, 8, 22, 9, 0, 0, TimeSpan.Zero));

    /// <summary>A deployment that screens nothing parses no message, which is what makes an opt-in nobody took free.</summary>
    [Fact]
    public async Task FindRefusalAsync_ADeploymentThatScreensNothing_ReadsNothingBackAtAll()
    {
        // Arrange
        var reader = Substitute.For<IOutgoingMailTextReader>();
        var screening = new OutgoingMailScreening(
            reader,
            new SensitiveContentEgressScreen(
                redactor: null,
                SensitiveContentScreeningPolicy.ScreeningNothing(),
                new RecordingSensitiveContentEgressTelemetry(),
                this.timeProvider));

        // Act
        var refusal = await screening.FindRefusalAsync(RawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(refusal);
        await reader.DidNotReceive().ReadAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>What is screened is the composed message rather than anything an author supplied, which is what covers every route into the outbox identically.</summary>
    [Fact]
    public async Task FindRefusalAsync_ASwitchedOnDeployment_ScreensWhatTheMessageSays()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        var reader = Substitute.For<IOutgoingMailTextReader>();
        reader.ReadAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new OutgoingMailText("a subject", $"the key is {Marker}", HtmlBody: null));

        var screening = new OutgoingMailScreening(reader, egress.Screen);

        // Act
        var refusal = await screening.FindRefusalAsync(RawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(refusal);
        Assert.Equal(SensitiveContentEgressRefusalReason.ContentFound, refusal.Reason);
        Assert.Equal(SensitiveContentScannerKind.Secrets, refusal.Scanner);
        Assert.Equal(["a subject", $"the key is {Marker}"], egress.Scanner.ScannedTexts);

        var stopped = Assert.Single(egress.Telemetry.Stopped);

        Assert.Equal(SensitiveContentEgressPoint.OutgoingMail, stopped.EgressPoint);
    }

    /// <summary>A message carrying nothing the deployment screens for lets the act through.</summary>
    [Fact]
    public async Task FindRefusalAsync_AMessageCarryingNothingScreened_StopsNothing()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        var reader = Substitute.For<IOutgoingMailTextReader>();
        reader.ReadAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new OutgoingMailText("a subject", "an ordinary message", "<p>an ordinary message</p>"));

        var screening = new OutgoingMailScreening(reader, egress.Screen);

        // Act
        var refusal = await screening.FindRefusalAsync(RawMime, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(refusal);
        Assert.Equal(3, egress.Scanner.ScannedTexts.Count);
    }
}
