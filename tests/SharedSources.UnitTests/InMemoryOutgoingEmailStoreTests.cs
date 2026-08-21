// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Scheduling;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the outbox store the delivery use cases and the tools over them write their sends through.</summary>
/// <remarks>
/// The guarantees asserted here are the ones the real store gets from a unique index and a claim statement, which the
/// suites using this double have no other way to reach. A double that opened a second record under an identity it
/// already held, or let a lease an attempt had lost go on writing, would let a regression in exactly the behaviour
/// those suites exist to prove pass unnoticed.
/// </remarks>
public sealed class InMemoryOutgoingEmailStoreTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly IPersistenceSession Session = new IgnoredPersistenceSession();

    private static readonly OutgoingEmailPrincipal Principal = OutgoingEmailPrincipal.Of("agent-key");

    [Fact]
    public async Task OpenAsync_ARequestNothingHasBeenWrittenFor_RecordsItAtTheOpeningStage()
    {
        // Arrange
        var store = Store();

        // Act
        var opened = await OpenAsync(store, Requester("send-1"));

        // Assert
        Assert.True(opened.WasRecordedNow);
        Assert.Equal(Account, opened.Record.AccountId);
        Assert.Equal(OutgoingEmailStage.Recorded, opened.Record.Stage);
        Assert.Equal(Moment, opened.Record.RecordedAt);
        Assert.Equal(Principal, opened.Record.Principal);
        Assert.Equal(0, opened.Record.AttemptCount);
    }

    /// <summary>The identity is what a repeat reaches the first record through, which is the unique index's whole job.</summary>
    [Fact]
    public async Task OpenAsync_ARequestUnderAnIdentityAlreadyRecorded_ReadsThatRecordBackInsteadOfOpeningASecond()
    {
        // Arrange
        var store = Store();
        var first = await OpenAsync(store, Requester("send-1"));

        // Act
        var again = await OpenAsync(store, Requester("send-1"));

        // Assert
        Assert.False(again.WasRecordedNow);
        Assert.Equal(first.Record.Id, again.Record.Id);
        Assert.Equal(2, store.OpenRequests.Count);
    }

    /// <summary>Two origins stating the same identity are two sends, exactly as the real store's key reads them.</summary>
    [Fact]
    public async Task OpenAsync_TheSameIdentityUnderADifferentOrigin_OpensARecordOfItsOwn()
    {
        // Arrange
        var store = Store();
        var command = await OpenAsync(store, OutgoingEmailRequester.Command("one"));

        // Act
        var scheduled = await OpenAsync(store, OutgoingEmailRequester.Create(OutgoingEmailOrigin.Rule, "one"));

        // Assert
        Assert.True(scheduled.WasRecordedNow);
        Assert.NotEqual(command.Record.Id, scheduled.Record.Id);
    }

    /// <summary>A session that never commits leaves nothing behind, the way a losing insert meets the unique index.</summary>
    [Fact]
    public async Task OpenAsync_ASessionTheTestDeclaresAsRollingBack_LeavesNothingForTheNextReadToFind()
    {
        // Arrange
        var store = new InMemoryOutgoingEmailStore(
            sessionCommits: _ => false,
            new FakeTimeProvider(Moment));

        // Act
        var lost = await OpenAsync(store, Requester("send-1"));
        var retried = await OpenAsync(store, Requester("send-1"));

        // Assert
        Assert.True(retried.WasRecordedNow);
        Assert.NotEqual(lost.Record.Id, retried.Record.Id);
    }

    /// <summary>A held send is unclaimable because of when it is available, which is the only rule holding one back.</summary>
    [Fact]
    public async Task OpenAsync_ASendHeldUntilLater_IsAvailableAtTheInstantItWasHeldFor()
    {
        // Arrange
        var store = Store();
        var dueAt = ZonedInstant.At(Moment.AddHours(3));

        // Act
        var opened = await OpenAsync(store, Requester("send-1"), dueAt);

        // Assert
        Assert.Equal(dueAt.Instant, opened.Record.AvailableAt);
        Assert.Empty(await store.ClaimAsync(Claim(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ClaimAsync_ARecordDueNow_HandsItToOneHolderAndCountsTheAttempt()
    {
        // Arrange
        var store = Store();
        var opened = await OpenAsync(store, Requester("send-1"));

        // Act
        var claimed = await store.ClaimAsync(Claim(), TestContext.Current.CancellationToken);
        var second = await store.ClaimAsync(Claim(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(opened.Record.Id, Assert.Single(claimed).Record.Id);
        Assert.Equal(1, claimed[0].Record.AttemptCount);
        Assert.True(store.IsLeased(opened.Record.Id));
        Assert.Empty(second);
    }

    /// <summary>An attempt whose record has been handed on writes nothing more, which is what the lease column buys.</summary>
    [Fact]
    public async Task RecordTransmissionBegunAsync_ALeaseALaterAttemptHasTakenOver_RefusesTheWrite()
    {
        // Arrange
        var store = Store();
        var opened = await OpenAsync(store, Requester("send-1"));
        var claimed = await store.ClaimAsync(Claim(), TestContext.Current.CancellationToken);
        store.Reassign(opened.Record.Id);

        // Act
        var refused = await Assert.ThrowsAsync<OutgoingEmailLeaseLostException>(() =>
            store.RecordTransmissionBegunAsync(
                Session,
                claimed[0].Lease,
                opened.Record.Id,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(opened.Record.Id, refused.OutgoingEmailId);
    }

    /// <summary>A delivery is only ever claimed from the stage that recorded it started, never stamped over another.</summary>
    [Fact]
    public async Task AdvanceAsync_ASendNothingRecordedATransmissionFor_RefusesTheDeliveredEnding()
    {
        // Arrange
        var store = Store();
        var opened = await OpenAsync(store, Requester("send-1"));
        var claimed = await store.ClaimAsync(Claim(), TestContext.Current.CancellationToken);

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AdvanceAsync(
            Session,
            claimed[0].Lease,
            opened.Record.Id,
            OutgoingEmailStage.Sent,
            replyCode: 250,
            TestContext.Current.CancellationToken));
    }

    /// <summary>An outcome naming somebody the record does not is a caller answering about the wrong send.</summary>
    [Fact]
    public async Task RecordRecipientOutcomesAsync_AnAddressTheRecordDoesNotName_RefusesTheOutcome()
    {
        // Arrange
        var store = Store();
        var opened = await OpenAsync(store, Requester("send-1"));
        var claimed = await store.ClaimAsync(Claim(), TestContext.Current.CancellationToken);

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordRecipientOutcomesAsync(
            Session,
            claimed[0].Lease,
            opened.Record.Id,
            [OutgoingRecipientOutcome.Unanswered(Recipient("somebody-else@example.test"))],
            TestContext.Current.CancellationToken));
    }

    /// <summary>Every non-terminal stage is answered for, so a drained account is not read as one nothing measured.</summary>
    [Fact]
    public async Task CountOutstandingByStageAsync_AnAccountWithOneRecordedSend_AnswersForEveryStageASendMayMoveFrom()
    {
        // Arrange
        var store = Store();
        await OpenAsync(store, Requester("send-1"));

        // Act
        var counted = await store.CountOutstandingByStageAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [(OutgoingEmailStage.Recorded, 1), (OutgoingEmailStage.TransmissionBegun, 0)],
            counted.Select(count => (count.Stage, count.Count)));
    }

    [Fact]
    public async Task FindAsync_AReadTheTestMadeFail_RaisesWhatItSaidRatherThanAnswering()
    {
        // Arrange
        var store = Store();
        var opened = await OpenAsync(store, Requester("send-1"));
        store.ReadFailure = () => new InvalidOperationException("The database went away.");

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.FindAsync(opened.Record.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>A withdrawal is refused while an attempt holds the record, because the bytes may already have gone.</summary>
    [Fact]
    public async Task Withdraw_ARecordAnAttemptIsHolding_ReportsTheAttemptRatherThanCancellingIt()
    {
        // Arrange
        var store = Store();
        var opened = await OpenAsync(store, Requester("send-1"));
        await store.ClaimAsync(Claim(), TestContext.Current.CancellationToken);

        // Act
        var outcome = store.Withdraw(opened.Record.Id, Moment);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.AttemptUnderWay, outcome);
        Assert.Equal(OutgoingEmailStage.Recorded, store.Read(opened.Record.Id).Stage);
    }

    [Fact]
    public void Withdraw_ARecordNothingWroteDown_ReportsThatNothingCarriesTheIdentifier() => Assert.Equal(
        OutboxDecisionOutcome.RecordUnknown,
        Store().Withdraw(OutgoingEmailId.Create(Guid.CreateVersion7()), Moment));

    /// <summary>A published record is one another writer already committed, which is how a race is interleaved.</summary>
    [Fact]
    public void Publish_ARecordAnotherWriterCommitted_MakesItReadableWithoutASession()
    {
        // Arrange
        var store = Store();
        var request = Request(Requester("send-1"));

        // Act
        var published = store.Publish(request, mimeByteLength: 64);

        // Assert
        Assert.Equal(published, store.Read(published.Id));
        Assert.Null(published.Principal);
        Assert.Empty(store.OpenRequests);
    }

    private static InMemoryOutgoingEmailStore Store() => new(timeProvider: new FakeTimeProvider(Moment));

    private static OutgoingEmailRequester Requester(string identity) => OutgoingEmailRequester.Command(identity);

    private static OutgoingEmailClaimRequest Claim() =>
        OutgoingEmailClaimRequest.Create(Account, batchSize: 8, TimeSpan.FromMinutes(5));

    private static OutgoingRecipient Recipient(string address)
    {
        if (!EmailAddress.TryCreate(displayName: null, address, out var mailbox))
        {
            throw new InvalidOperationException($"The test address '{address}' names no mailbox.");
        }

        return OutgoingRecipient.Create(mailbox, OutgoingRecipientRole.To);
    }

    private static OutgoingEmailRequest Request(OutgoingEmailRequester requester, ZonedInstant? dueAt = null) =>
        OutgoingEmailRequest.Create(Account, requester, [Recipient("anna@example.test")], dueAt);

    private static Task<OpenedOutgoingEmail> OpenAsync(
        InMemoryOutgoingEmailStore store,
        OutgoingEmailRequester requester,
        ZonedInstant? dueAt = null) =>
        store.OpenAsync(
            Session,
            Request(requester, dueAt),
            Principal,
            mimeByteLength: 64,
            TestContext.Current.CancellationToken);
}
