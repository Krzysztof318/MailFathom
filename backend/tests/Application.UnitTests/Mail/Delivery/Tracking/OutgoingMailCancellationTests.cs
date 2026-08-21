// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Tracking;

/// <summary>Covers the one window in which sending is reversible, and every way of being outside it.</summary>
public sealed class OutgoingMailCancellationTests
{
    [Fact]
    public async Task CancelAsync_ASendNothingHasBegunTransmitting_WithdrawsIt()
    {
        // Arrange
        var harness = new OutgoingMailTrackingHarness();
        var queued = harness.Queue();

        // Act
        var withdrawn = await harness.Cancellation.CancelAsync(queued.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingEmailStage.Cancelled, withdrawn.Stage);
        Assert.Equal(OutgoingEmailStage.Cancelled, harness.Store.Read(queued.Id).Stage);
    }

    /// <summary>Withdrawing twice is one withdrawal, which is what makes the tool over this honestly idempotent.</summary>
    [Fact]
    public async Task CancelAsync_ASendAlreadyWithdrawn_AnswersWithItAndChangesNothing()
    {
        // Arrange
        var harness = new OutgoingMailTrackingHarness();
        var queued = harness.Queue();
        var first = await harness.Cancellation.CancelAsync(queued.Id, TestContext.Current.CancellationToken);

        harness.Clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        var second = await harness.Cancellation.CancelAsync(queued.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingEmailStage.Cancelled, second.Stage);
        Assert.Equal(first.StageChangedAt, second.StageChangedAt);
    }

    /// <summary>A message whose body has begun to go out cannot be withdrawn from the mailbox it may have reached.</summary>
    [Fact]
    public async Task CancelAsync_ASendWhoseTransmissionHasBegun_IsRefusedAndLeftWhereItIs()
    {
        // Arrange
        var harness = new OutgoingMailTrackingHarness();
        var queued = harness.Queue();
        await harness.BeginTransmissionAsync(queued.Id);

        // Act
        var refusal = await Assert.ThrowsAsync<QueuedSendRefusedException>(
            () => harness.Cancellation.CancelAsync(queued.Id, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingEmailNoLongerCancellable, refusal.ErrorCode);
        Assert.Equal(OutgoingEmailStage.TransmissionBegun, harness.Store.Read(queued.Id).Stage);
    }

    /// <summary>A delivery attempt holding the record may transmit at any instant, so nothing is cancelled underneath it.</summary>
    /// <remarks>
    /// The record is still at the stage a withdrawal is allowed from, and the refusal is the lease alone. That is the
    /// whole of what "never races the delivery worker" means here: the condition is the claim's own, so the two can
    /// never both decide they have the record.
    /// </remarks>
    [Fact]
    public async Task CancelAsync_ASendADeliveryAttemptStillHolds_IsRefusedAndLeftQueued()
    {
        // Arrange
        var harness = new OutgoingMailTrackingHarness();
        var queued = harness.Queue();
        harness.HandToADeliveryAttempt(queued.Id);

        // Act
        var refusal = await Assert.ThrowsAsync<QueuedSendRefusedException>(
            () => harness.Cancellation.CancelAsync(queued.Id, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingEmailNoLongerCancellable, refusal.ErrorCode);
        Assert.Equal(OutgoingEmailStage.Recorded, harness.Store.Read(queued.Id).Stage);
    }

    /// <summary>An attempt whose lease has run out is gone, and its record is free by the same rule that makes it claimable again.</summary>
    [Fact]
    public async Task CancelAsync_ASendWhoseHoldersLeaseHasRunOut_WithdrawsIt()
    {
        // Arrange
        var harness = new OutgoingMailTrackingHarness();
        var queued = harness.Queue();
        harness.HandToADeliveryAttempt(queued.Id);

        harness.Clock.Advance(TimeSpan.FromHours(1));

        // Act
        var withdrawn = await harness.Cancellation.CancelAsync(queued.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingEmailStage.Cancelled, withdrawn.Stage);
        Assert.Equal(OutgoingEmailStage.Cancelled, harness.Store.Read(queued.Id).Stage);
    }

    /// <summary>Nothing here reaches a message this caller did not ask for, and the refusal says no more than that.</summary>
    [Fact]
    public async Task CancelAsync_ASendAnotherCallerQueued_IsRefusedAsNotFoundAndLeftQueued()
    {
        // Arrange
        var harness = new OutgoingMailTrackingHarness();
        var queued = harness.Queue(queuedBy: "another-agent-key");

        // Act
        var refusal = await Assert.ThrowsAsync<QueuedSendRefusedException>(
            () => harness.Cancellation.CancelAsync(queued.Id, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingEmailNotFound, refusal.ErrorCode);
        Assert.Equal(OutgoingEmailStage.Recorded, harness.Store.Read(queued.Id).Stage);
    }

    /// <summary>What a caller may stop is exactly what it was allowed to start.</summary>
    [Fact]
    public async Task CancelAsync_ACallerHoldingOnlyTheReadingGrant_IsRefused()
    {
        // Arrange
        var harness = new OutgoingMailTrackingHarness(granted: [MailFathomPermission.MailRead]);
        var queued = harness.Queue();

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Cancellation.CancelAsync(queued.Id, TestContext.Current.CancellationToken));
    }
}
