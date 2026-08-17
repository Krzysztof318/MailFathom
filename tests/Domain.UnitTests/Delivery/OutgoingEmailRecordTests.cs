// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Delivery;
using Xunit;

namespace MailFathom.Domain.UnitTests.Delivery;

public sealed class OutgoingEmailRecordTests
{
    private static readonly DateTimeOffset Answered =
        DateTimeOffset.Parse("2026-08-16T10:00:04Z", CultureInfo.InvariantCulture);

    /// <summary>
    /// What an attempt resuming from a stage is allowed to do: only the transmission stage is undecidable, and only the
    /// three terminal ones stop the send being attempted at all.
    /// </summary>
    [Theory]
    [InlineData(OutgoingEmailStage.Recorded, false, false)]
    [InlineData(OutgoingEmailStage.TransmissionBegun, true, false)]
    [InlineData(OutgoingEmailStage.Sent, false, true)]
    [InlineData(OutgoingEmailStage.Refused, false, true)]
    [InlineData(OutgoingEmailStage.Cancelled, false, true)]
    public void Stage_EveryStageASendCanBeResumedFrom_ReportsWhetherItIsUndecidableOrFinished(
        OutgoingEmailStage stage,
        bool expectedUnknownOutcome,
        bool expectedTerminal)
    {
        // Arrange
        var recipient = OutgoingRecipientOutcome.Unanswered(
            OutgoingDeliveryFixture.Recipient("anna@example.test", OutgoingRecipientRole.To));

        // Act
        var record = OutgoingDeliveryFixture.Record(stage, recipient);

        // Assert
        Assert.Equal(expectedUnknownOutcome, record.HasUnknownOutcome);
        Assert.Equal(expectedTerminal, record.IsTerminal);
    }

    /// <summary>
    /// The partial acceptance an attempt has to survive: a recipient the message reached and one permanently refused
    /// are both settled, so a later attempt offers neither and nobody receives the message twice.
    /// </summary>
    [Fact]
    public void OutstandingRecipients_AfterAPartialAcceptance_OffersOnlyTheUnsettledOnes()
    {
        // Arrange
        var delivered = OutgoingDeliveryFixture.Recipient("anna@example.test", OutgoingRecipientRole.To);
        var refused = OutgoingDeliveryFixture.Recipient("bruno@example.invalid", OutgoingRecipientRole.To);
        var deferred = OutgoingDeliveryFixture.Recipient("clara@example.test", OutgoingRecipientRole.Cc);

        // Act
        var record = OutgoingDeliveryFixture.Record(
            OutgoingEmailStage.Recorded,
            OutgoingRecipientOutcome.Answered(delivered, OutgoingRecipientStatus.Accepted, 250, Answered),
            OutgoingRecipientOutcome.Answered(refused, OutgoingRecipientStatus.Refused, 550, Answered),
            OutgoingRecipientOutcome.Answered(deferred, OutgoingRecipientStatus.Pending, 451, Answered));

        // Assert
        Assert.Equal([deferred], record.OutstandingRecipients);
    }

    /// <summary>A recipient a server temporarily rejected is offered again, and the reply that deferred them is kept.</summary>
    [Fact]
    public void Recipients_TemporarilyRejected_StaysOutstandingAndKeepsTheReply()
    {
        // Arrange
        var deferred = OutgoingDeliveryFixture.Recipient("clara@example.test", OutgoingRecipientRole.Cc);

        // Act
        var outcome = OutgoingRecipientOutcome.Answered(deferred, OutgoingRecipientStatus.Pending, 451, Answered);

        // Assert
        Assert.True(outcome.IsOutstanding);
        Assert.Equal(451, outcome.LastReplyCode);
        Assert.Equal(Answered, outcome.AnsweredAt);
    }

    /// <summary>A record every recipient is settled on owes nothing further, whatever stage it stands at.</summary>
    [Fact]
    public void OutstandingRecipients_EveryRecipientSettled_IsEmpty()
    {
        // Arrange
        var delivered = OutgoingDeliveryFixture.Recipient("anna@example.test", OutgoingRecipientRole.To);

        // Act
        var record = OutgoingDeliveryFixture.Record(
            OutgoingEmailStage.Sent,
            OutgoingRecipientOutcome.Answered(delivered, OutgoingRecipientStatus.Accepted, 250, Answered));

        // Assert
        Assert.Empty(record.OutstandingRecipients);
    }

    /// <summary>Nothing has been offered before the first attempt, so every recipient is one the send still owes.</summary>
    [Fact]
    public void OutstandingRecipients_NewRecord_OwesEveryRecipient()
    {
        // Arrange
        var anna = OutgoingDeliveryFixture.Recipient("anna@example.test", OutgoingRecipientRole.To);
        var bruno = OutgoingDeliveryFixture.Recipient("bruno@example.test", OutgoingRecipientRole.Bcc);

        // Act
        var record = OutgoingDeliveryFixture.Record(
            OutgoingEmailStage.Recorded,
            OutgoingRecipientOutcome.Unanswered(anna),
            OutgoingRecipientOutcome.Unanswered(bruno));

        // Assert
        Assert.Equal([anna, bruno], record.OutstandingRecipients);
        Assert.All(record.Recipients, outcome => Assert.Null(outcome.LastReplyCode));
    }

    [Theory]
    [InlineData(99)]
    [InlineData(600)]
    public void Answered_ReplyCodeThatIsNotThreeDigits_IsRefused(int replyCode)
    {
        // Arrange
        var recipient = OutgoingDeliveryFixture.Recipient("anna@example.test", OutgoingRecipientRole.To);

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => OutgoingRecipientOutcome.Answered(
            recipient,
            OutgoingRecipientStatus.Accepted,
            replyCode,
            Answered));
    }

    [Fact]
    public void Answered_StatusOutsideTheDeclaredSet_IsRefused()
    {
        // Arrange
        var recipient = OutgoingDeliveryFixture.Recipient("anna@example.test", OutgoingRecipientRole.To);

        // Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => OutgoingRecipientOutcome.Answered(
            recipient,
            (OutgoingRecipientStatus)11,
            250,
            Answered));
    }
}
