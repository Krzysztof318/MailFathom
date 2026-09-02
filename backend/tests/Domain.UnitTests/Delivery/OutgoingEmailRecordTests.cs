// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Scheduling;
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

    /// <summary>A send held for an instant that has not arrived is waiting; the same send once it has is not.</summary>
    [Theory]
    [InlineData("2026-08-16T09:59:59Z", true)]
    [InlineData("2026-08-16T10:00:00Z", false)]
    [InlineData("2026-08-16T10:00:01Z", false)]
    public void IsWaitingAt_ARecordedSend_ReportsWhetherItsInstantHasArrived(string asOf, bool expected)
    {
        // Arrange
        var record = Held(OutgoingEmailStage.Recorded, "2026-08-16T10:00:00Z");

        // Act, Assert
        Assert.Equal(expected, record.IsWaitingAt(Instant(asOf)));
    }

    /// <summary>A send that has begun to be answered for is not waiting, whatever instant it names.</summary>
    [Theory]
    [InlineData(OutgoingEmailStage.TransmissionBegun)]
    [InlineData(OutgoingEmailStage.Sent)]
    [InlineData(OutgoingEmailStage.Cancelled)]
    public void IsWaitingAt_ASendPastTheRecordedStage_IsNotWaiting(OutgoingEmailStage stage)
    {
        // Arrange
        var record = Held(stage, "2026-08-16T10:00:00Z");

        // Act, Assert
        Assert.False(record.IsWaitingAt(Instant("2026-08-16T09:00:00Z")));
    }

    /// <summary>Either side of the lateness bound, including the instant the bound itself names, which is still in time.</summary>
    /// <remarks>
    /// The boundary is the case worth stating: a message exactly as late as the deployment allows is delivered, so the
    /// bound reads as "up to this much" rather than as "less than this much".
    /// </remarks>
    [Theory]
    [InlineData("2026-08-16T17:59:59Z", false)]
    [InlineData("2026-08-16T18:00:00Z", false)]
    [InlineData("2026-08-16T18:00:01Z", true)]
    public void HasMissedItsDueTime_ASendWrittenForANamedTime_ReportsWhichSideOfTheBoundItIsOn(
        string asOf,
        bool expected)
    {
        // Arrange
        var record = Held(OutgoingEmailStage.Recorded, "2026-08-16T10:00:00Z");

        // Act, Assert
        Assert.Equal(expected, record.HasMissedItsDueTime(Instant(asOf), TimeSpan.FromHours(8)));
    }

    /// <summary>A send that named no time is never late, however long anything else has held it.</summary>
    [Fact]
    public void HasMissedItsDueTime_ASendThatNamedNoTime_IsNeverLate()
    {
        // Arrange
        var record = OutgoingDeliveryFixture.Record(
            OutgoingEmailStage.Recorded,
            OutgoingRecipientOutcome.Unanswered(
                OutgoingDeliveryFixture.Recipient("anna@example.test", OutgoingRecipientRole.To)));

        // Act, Assert
        Assert.False(record.HasMissedItsDueTime(Instant("2027-01-01T00:00:00Z"), TimeSpan.FromHours(8)));
    }

    /// <summary>Builds a record written to leave at one instant, at the stage the scenario is about.</summary>
    private static OutgoingEmailRecord Held(OutgoingEmailStage stage, string dueAt)
    {
        var record = OutgoingDeliveryFixture.Record(
            stage,
            OutgoingRecipientOutcome.Unanswered(
                OutgoingDeliveryFixture.Recipient("anna@example.test", OutgoingRecipientRole.To)));

        return record with { AvailableAt = Instant(dueAt), DueAt = ZonedInstant.At(Instant(dueAt)) };
    }

    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(
        value,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
