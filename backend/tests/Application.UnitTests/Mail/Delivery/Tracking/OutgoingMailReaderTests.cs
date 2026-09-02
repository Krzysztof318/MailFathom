// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Failures;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Tracking;

/// <summary>Covers what a caller may learn about a send, and what it may not learn about anybody else's.</summary>
public sealed class OutgoingMailReaderTests
{
    /// <summary>Reading back is what makes queueing an acceptable answer, so the record has to come back whole.</summary>
    [Fact]
    public async Task ReadAsync_ASendTheCallerQueued_AnswersWithTheRecord()
    {
        // Arrange
        var harness = new OutgoingMailTrackingHarness();
        var queued = harness.Queue();

        // Act
        var read = await harness.Reader.ReadAsync(queued.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(queued.Id, read.Id);
        Assert.Equal(OutgoingMailTrackingHarness.Account.Id, read.AccountId);
        Assert.Equal(OutgoingEmailStage.Recorded, read.Stage);
        Assert.Equal(queued.Recipients.Count, read.Recipients.Count);
    }

    /// <summary>An identifier alone must not tell a caller that this mailbox sent something.</summary>
    [Fact]
    public async Task ReadAsync_ASendAnotherCallerQueued_IsRefusedAsNotFound()
    {
        // Arrange
        var harness = new OutgoingMailTrackingHarness();
        var queued = harness.Queue(queuedBy: "another-agent-key");

        // Act
        var refusal = await Assert.ThrowsAsync<QueuedSendRefusedException>(
            () => harness.Reader.ReadAsync(queued.Id, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingEmailNotFound, refusal.ErrorCode);
    }

    /// <summary>The record that does not exist and the record that is not this caller's answer identically.</summary>
    [Fact]
    public async Task ReadAsync_AnIdentifierNoRecordCarries_IsRefusedTheSameWayAsAnotherCallersSend()
    {
        // Arrange
        var harness = new OutgoingMailTrackingHarness();
        var queued = harness.Queue(queuedBy: "another-agent-key");
        var absent = OutgoingEmailId.Create(Guid.CreateVersion7());

        // Act
        var forAbsent = await Assert.ThrowsAsync<QueuedSendRefusedException>(
            () => harness.Reader.ReadAsync(absent, TestContext.Current.CancellationToken));
        var forSomebodyElse = await Assert.ThrowsAsync<QueuedSendRefusedException>(
            () => harness.Reader.ReadAsync(queued.Id, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(forAbsent.ErrorCode, forSomebodyElse.ErrorCode);
        Assert.Equal(forAbsent.Message, forSomebodyElse.Message);
    }

    /// <summary>A send this deployment made for itself was asked for by nobody, so no credential reaches it.</summary>
    /// <remarks>
    /// The origin decides it rather than the principal, which is what keeps the answer right on a deployment whose
    /// credential happens to be named as this process is.
    /// </remarks>
    [Fact]
    public async Task ReadAsync_ASendARuleQueued_IsRefusedAsNotFound()
    {
        // Arrange
        var harness = new OutgoingMailTrackingHarness(callerIdentity: AuthorizedPrincipal.ProcessIdentityName);
        var queued = harness.Queue(
            queuedBy: AuthorizedPrincipal.ProcessIdentityName,
            origin: OutgoingEmailOrigin.Rule);

        // Act
        var refusal = await Assert.ThrowsAsync<QueuedSendRefusedException>(
            () => harness.Reader.ReadAsync(queued.Id, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingEmailNotFound, refusal.ErrorCode);
    }

    /// <summary>A row from a build that kept no principal matches nobody, because no caller can prove it queued one.</summary>
    [Fact]
    public async Task ReadAsync_ASendWrittenBeforeThePrincipalWasKept_IsRefusedAsNotFound()
    {
        // Arrange
        var harness = new OutgoingMailTrackingHarness();
        var queued = harness.Queue(queuedBy: null);

        // Act
        var refusal = await Assert.ThrowsAsync<QueuedSendRefusedException>(
            () => harness.Reader.ReadAsync(queued.Id, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingEmailNotFound, refusal.ErrorCode);
    }

    /// <summary>The grant is the sending one, so a credential given a mailbox to read learns nothing about what was sent from it.</summary>
    [Fact]
    public async Task ReadAsync_ACallerHoldingOnlyTheReadingGrant_IsRefused()
    {
        // Arrange
        var harness = new OutgoingMailTrackingHarness(granted: [MailFathomPermission.MailRead]);
        var queued = harness.Queue();

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => harness.Reader.ReadAsync(queued.Id, TestContext.Current.CancellationToken));
    }
}
