// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools;
using MailFathom.Mcp.Tools.Outgoing;
using MailFathom.TestSupport;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.Tools;

/// <summary>Covers what the two tools over one queued send own: reading the argument, and publishing the record.</summary>
/// <remarks>
/// Whose send a caller may reach, and when one may still be withdrawn, are the use cases' own and are proven there.
/// What is proven here is the pair either side of them: text that names no send is refused before anything is read,
/// and the record that comes back is published as the wire contract states — with what a mail server said about each
/// address the caller supplied, and with nothing about the message itself.
/// </remarks>
public sealed class OutgoingEmailToolTests
{
    private const string CallerIdentity = "agent-key";

    private static readonly DateTimeOffset Recorded = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    private static readonly OutgoingEmailId RecordId =
        OutgoingEmailId.Create(Guid.Parse("0198f0a0-2222-7000-8000-000000000002"));

    /// <summary>The answer is what a caller reads instead of sending again, so every part of it is the published contract.</summary>
    [Fact]
    public async Task GetOutgoingEmailAsync_ASendThisCallerQueued_PublishesItsStateAttemptsAndRecipientOutcomes()
    {
        // Arrange
        var store = StoreHolding(RecordAt(
            OutgoingEmailStage.Refused,
            attemptCount: 3,
            lastFailure: MailFathomErrorCode.OutgoingEmailRefused));
        var tool = new GetOutgoingEmailTool(ReaderOver(store));

        // Act
        var result = await tool.GetOutgoingEmailAsync(
            RecordId.ToString(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(RecordId.ToString(), result.OutgoingEmailId);
        Assert.Equal("work", result.AccountId);
        Assert.Equal(SendEmailState.Refused, result.State);
        Assert.Equal(3, result.AttemptCount);
        Assert.Equal(Recorded, result.QueuedAt);
        Assert.Equal("28009", result.FailureCode);
        Assert.Equal(
            [("anna@example.test", OutgoingEmailRecipientHeader.To, OutgoingEmailRecipientState.Refused, 550)],
            result.Recipients.Select(recipient =>
                (recipient.Address, recipient.Header, recipient.State, recipient.LastReplyCode)));
    }

    /// <summary>A send nothing has failed at carries no code, so a caller reads absence rather than a number meaning nothing.</summary>
    [Fact]
    public async Task GetOutgoingEmailAsync_ASendNoAttemptHasFailed_PublishesNoFailureCode()
    {
        // Arrange
        var store = StoreHolding(RecordAt(OutgoingEmailStage.Recorded));
        var tool = new GetOutgoingEmailTool(ReaderOver(store));

        // Act
        var result = await tool.GetOutgoingEmailAsync(
            RecordId.ToString(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SendEmailState.Queued, result.State);
        Assert.Null(result.FailureCode);
        Assert.Null(result.Recipients.Single().LastReplyCode);
    }

    /// <summary>Text that names no send at all is the caller's own mistake, and saying so is not the answer a missing record gets.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-uuid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetOutgoingEmailAsync_TextThatNamesNoSend_IsRefusedBeforeAnythingIsRead(string outgoingEmailId)
    {
        // Arrange
        var store = Substitute.For<IOutgoingEmailStore>();
        var tool = new GetOutgoingEmailTool(ReaderOver(store));

        // Act
        var refusal = await Assert.ThrowsAsync<QueuedSendRefusedException>(
            () => tool.GetOutgoingEmailAsync(outgoingEmailId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingEmailIdentifierMalformed, refusal.ErrorCode);
        await store.DidNotReceive().FindAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The answer reports the withdrawal in the state's own published spelling, so nothing has to be inferred from a silence.</summary>
    [Fact]
    public async Task CancelOutgoingEmailAsync_ASendStillWaiting_PublishesItAsCancelled()
    {
        // Arrange
        var store = StoreHolding(RecordAt(OutgoingEmailStage.Recorded));
        var outbox = Substitute.For<IOutboxOperationStore>();

        outbox.CancelAsync(RecordId, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                store.FindAsync(RecordId, Arg.Any<CancellationToken>())
                    .Returns(Task.FromResult<OutgoingEmailRecord?>(RecordAt(OutgoingEmailStage.Cancelled)));

                return Task.FromResult(OutboxDecisionOutcome.Accepted);
            });

        var tool = new CancelOutgoingEmailTool(CancellationOver(store, outbox));

        // Act
        var result = await tool.CancelOutgoingEmailAsync(
            RecordId.ToString(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(SendEmailState.Cancelled, result.State);
    }

    /// <summary>A message already going out is refused with the code that says so, rather than reported as withdrawn.</summary>
    [Fact]
    public async Task CancelOutgoingEmailAsync_ASendAlreadyBeingTransmitted_IsRefusedWithoutWriting()
    {
        // Arrange
        var store = StoreHolding(RecordAt(OutgoingEmailStage.TransmissionBegun, attemptCount: 1));
        var outbox = Substitute.For<IOutboxOperationStore>();
        var tool = new CancelOutgoingEmailTool(CancellationOver(store, outbox));

        // Act
        var refusal = await Assert.ThrowsAsync<QueuedSendRefusedException>(
            () => tool.CancelOutgoingEmailAsync(RecordId.ToString(), TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingEmailNoLongerCancellable, refusal.ErrorCode);
        await outbox.DidNotReceive().CancelAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    public async Task CancelOutgoingEmailAsync_TextThatNamesNoSend_IsRefusedBeforeAnythingIsRead(string outgoingEmailId)
    {
        // Arrange
        var store = Substitute.For<IOutgoingEmailStore>();
        var outbox = Substitute.For<IOutboxOperationStore>();
        var tool = new CancelOutgoingEmailTool(CancellationOver(store, outbox));

        // Act
        var refusal = await Assert.ThrowsAsync<QueuedSendRefusedException>(
            () => tool.CancelOutgoingEmailAsync(outgoingEmailId, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingEmailIdentifierMalformed, refusal.ErrorCode);
        await outbox.DidNotReceive().CancelAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>());
    }

    private static OutgoingMailReader ReaderOver(IOutgoingEmailStore store) => new(
        store,
        AccessAuthorizations.ForPrincipal(
            AuthorizedPrincipal.Caller(CallerIdentity, [MailFathomPermission.MailSend])));

    private static OutgoingMailCancellation CancellationOver(
        IOutgoingEmailStore store,
        IOutboxOperationStore outbox) => new(
        ReaderOver(store),
        outbox,
        AccessAuthorizations.ForPrincipal(
            AuthorizedPrincipal.Caller(CallerIdentity, [MailFathomPermission.MailSend])));

    private static IOutgoingEmailStore StoreHolding(OutgoingEmailRecord record)
    {
        var store = Substitute.For<IOutgoingEmailStore>();
        store.FindAsync(record.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<OutgoingEmailRecord?>(record));

        return store;
    }

    private static OutgoingEmailRecord RecordAt(
        OutgoingEmailStage stage,
        int attemptCount = 0,
        MailFathomErrorCode? lastFailure = null)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var address));

        var settled = lastFailure is not null;

        return new OutgoingEmailRecord
        {
            Id = RecordId,
            AccountId = MailAccountId.Create("work"),
            Requester = OutgoingEmailRequester.Command("send-1"),
            Principal = OutgoingEmailPrincipal.Of(CallerIdentity),
            Recipients =
            [
                settled
                    ? OutgoingRecipientOutcome.Answered(
                        OutgoingRecipient.Create(address, OutgoingRecipientRole.To),
                        OutgoingRecipientStatus.Refused,
                        replyCode: 550,
                        answeredAt: Recorded)
                    : OutgoingRecipientOutcome.Unanswered(
                        OutgoingRecipient.Create(address, OutgoingRecipientRole.To)),
            ],
            Stage = stage,
            MimeByteLength = 512,
            AttemptCount = attemptCount,
            RecordedAt = Recorded,
            StageChangedAt = Recorded,
            AvailableAt = Recorded,

            // `send_email` refuses to schedule a send, so a record a caller queued never names a time to leave at, and
            // neither tool over one publishes it.
            DueAt = null,
            LastFailure = lastFailure,
            LastReplyCode = settled ? 550 : null,
            Filings = [],
            LastFilingFailure = null,
        };
    }
}
