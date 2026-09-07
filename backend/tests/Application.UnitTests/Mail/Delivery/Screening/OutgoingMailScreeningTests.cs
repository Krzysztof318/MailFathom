// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
                FixedSensitiveContentPostures.ScanningNothing(),
                new RecordingSensitiveContentEgressTelemetry(),
                this.timeProvider));

        // Act
        var refusal = await screening.FindRefusalAsync(
            ScanningSensitiveContentEgress.User,
            RawMime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(refusal);
        await reader.DidNotReceive().ReadForScreeningAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Bytes that are no message are refused whatever the deployment screens for, because an argument guard that fired
    /// on one deployment and not on another would let the draft book file a draft of nothing wherever screening is off.
    /// </summary>
    [Fact]
    public async Task FindRefusalAsync_EmptyBytesOnADeploymentThatScreensNothing_RefusesTheArgument()
    {
        // Arrange
        var reader = Substitute.For<IOutgoingMailTextReader>();
        var screening = new OutgoingMailScreening(
            reader,
            new SensitiveContentEgressScreen(
                FixedSensitiveContentPostures.ScanningNothing(),
                new RecordingSensitiveContentEgressTelemetry(),
                this.timeProvider));

        // Act
        var refusal = await Assert.ThrowsAsync<ArgumentException>(
            () => screening.FindRefusalAsync(
                ScanningSensitiveContentEgress.User,
                ReadOnlyMemory<byte>.Empty,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("rawMime", refusal.ParamName);
        await reader.DidNotReceive().ReadForScreeningAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>What is screened is the composed message rather than anything an author supplied, which is what covers every route into the outbox identically.</summary>
    [Fact]
    public async Task FindRefusalAsync_ASwitchedOnDeployment_ScreensWhatTheMessageSays()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        var reader = Substitute.For<IOutgoingMailTextReader>();
        reader.ReadForScreeningAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new OutgoingMailText("a subject", $"the key is {Marker}", HtmlBody: null));

        var screening = new OutgoingMailScreening(reader, egress.Screen);

        // Act
        var refusal = await screening.FindRefusalAsync(
            ScanningSensitiveContentEgress.User,
            RawMime,
            TestContext.Current.CancellationToken);

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
        reader.ReadForScreeningAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new OutgoingMailText("a subject", "an ordinary message", "<p>an ordinary message</p>"));

        var screening = new OutgoingMailScreening(reader, egress.Screen);

        // Act
        var refusal = await screening.FindRefusalAsync(
            ScanningSensitiveContentEgress.User,
            RawMime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(refusal);
        Assert.Equal(3, egress.Scanner.ScannedTexts.Count);
    }

    /// <summary>
    /// The point of screening an attachment at all: a credential inside the attached document is judged with the one
    /// typed into the covering note, so what protects an author does not depend on which half they put it in.
    /// </summary>
    [Fact]
    public async Task FindRefusalAsync_AnAttachmentCarryingScreenedMaterial_StopsTheAct()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        var screening = ScreeningOver(
            egress,
            new OutgoingMailText("a subject", "the file is attached", HtmlBody: null)
            {
                AttachmentTexts = [$"the key is {Marker}"],
            });

        // Act
        var refusal = await screening.FindRefusalAsync(
            ScanningSensitiveContentEgress.User,
            RawMime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(refusal);
        Assert.Equal(SensitiveContentEgressRefusalReason.ContentFound, refusal.Reason);
        Assert.Equal(["a subject", "the file is attached", $"the key is {Marker}"], egress.Scanner.ScannedTexts);
    }

    /// <summary>
    /// A file nothing could read stops the act rather than passing as clean, which is the whole difference between
    /// screening a message and screening the half of it somebody happened to type.
    /// </summary>
    [Fact]
    public async Task FindRefusalAsync_AnAttachmentNothingCouldRead_StopsTheActWithoutNamingWhy()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        var screening = ScreeningOver(
            egress,
            new OutgoingMailText("a subject", "the file is attached", HtmlBody: null)
            {
                AttachmentRefusal = OutgoingAttachmentRefusal.NotRead,
            });

        // Act
        var refusal = await screening.FindRefusalAsync(
            ScanningSensitiveContentEgress.User,
            RawMime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(refusal);
        Assert.Equal(SensitiveContentEgressRefusalReason.AttachmentNotRead, refusal.Reason);
        Assert.Null(refusal.Scanner);
        Assert.Null(refusal.Category);
    }

    /// <summary>
    /// A message whose attachments together outran what a whole message may be read within is refused for the ceiling
    /// rather than for a file, because every document in it was read successfully — telling its author to convert one
    /// would name a file that was never the problem, and only a shorter message or a raised ceiling changes the answer.
    /// </summary>
    [Fact]
    public async Task FindRefusalAsync_AttachmentsReachingAMessageCeiling_StopsTheActAsNotFullyScanned()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        var screening = ScreeningOver(
            egress,
            new OutgoingMailText("a subject", "the files are attached", HtmlBody: null)
            {
                AttachmentRefusal = OutgoingAttachmentRefusal.MessageCeilingReached,
            });

        // Act
        var refusal = await screening.FindRefusalAsync(
            ScanningSensitiveContentEgress.User,
            RawMime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(refusal);
        Assert.Equal(SensitiveContentEgressRefusalReason.TextExceededScanCeiling, refusal.Reason);
        Assert.Null(refusal.Scanner);
        Assert.Null(refusal.Category);
    }

    /// <summary>
    /// A message carrying both is refused for the finding, because that is the half its author can act on. Reporting an
    /// unreadable file first would send them looking at an archive for a credential the scanner found in the body.
    /// </summary>
    [Fact]
    public async Task FindRefusalAsync_AMessageCarryingBothAFindingAndAnUnreadableFile_StopsForTheFinding()
    {
        // Arrange
        using var egress = ScanningSensitiveContentEgress.Finding(Marker, this.timeProvider);

        var screening = ScreeningOver(
            egress,
            new OutgoingMailText("a subject", $"the key is {Marker}", HtmlBody: null)
            {
                AttachmentRefusal = OutgoingAttachmentRefusal.NotRead,
            });

        // Act
        var refusal = await screening.FindRefusalAsync(
            ScanningSensitiveContentEgress.User,
            RawMime,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(refusal);
        Assert.Equal(SensitiveContentEgressRefusalReason.ContentFound, refusal.Reason);
    }

    /// <summary>Composes the screening over a reader that answers with one message, whatever bytes it is handed.</summary>
    private static OutgoingMailScreening ScreeningOver(
        ScanningSensitiveContentEgress egress,
        OutgoingMailText composed)
    {
        var reader = Substitute.For<IOutgoingMailTextReader>();
        reader.ReadForScreeningAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>()).Returns(composed);

        return new OutgoingMailScreening(reader, egress.Screen);
    }
}
