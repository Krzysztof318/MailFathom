// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Governance;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Drafts;

/// <summary>Covers where a draft stops being one, and everything that leaves it standing instead.</summary>
public sealed class MailDraftPromotionTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly DateTimeOffset Moment = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A promoted draft becomes an ordinary send carrying the very bytes the drafts folder shows.</summary>
    [Fact]
    public async Task PromoteAsync_AddressedDraft_QueuesTheStoredMessageUnchanged()
    {
        // Arrange
        var harness = Harness();
        var draft = await SaveAsync(harness, "the message as written");
        var contentStore = Substitute.For<IEmailContentStore>();
        var promotion = PromotionOver(harness, contentStore: contentStore);

        // Act
        var record = await promotion.PromoteAsync(draft.Id, CancellationToken.None);

        // Assert
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
        Assert.Equal(draft.MimeByteLength, record.MimeByteLength);
        var stored = harness.Contents.Peek(draft.Id).ToArray();
        await contentStore.Received(1).SaveOutgoingContentAsync(
            Arg.Any<IPersistenceSession>(),
            record.Id,
            Arg.Is<ReadOnlyMemory<byte>>(mime => mime.ToArray().SequenceEqual(stored)),
            Arg.Any<CancellationToken>());
        Assert.Equal(record.Id, harness.Drafts.Peek(draft.Id)!.PromotedTo);
    }

    /// <summary>Asking twice queues one message, because the draft already names the record the first ask wrote.</summary>
    [Fact]
    public async Task PromoteAsync_SameDraftTwice_AnswersWithTheRecordItAlreadyProduced()
    {
        // Arrange
        var harness = Harness();
        var draft = await SaveAsync(harness, "the message as written");
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var promotion = PromotionOver(harness, outgoingEmails);
        var first = await promotion.PromoteAsync(draft.Id, CancellationToken.None);

        // Act
        var second = await promotion.PromoteAsync(draft.Id, CancellationToken.None);

        // Assert
        Assert.Equal(first.Id, second.Id);
    }

    /// <summary>
    /// A draft is given up by the very delivery its promotion produced, and taking its copy out of the folder is a
    /// network round trip after that mark. A caller retrying across that window is asking about a message that was
    /// sent, so it is told which record carries it rather than that no such draft exists.
    /// </summary>
    [Fact]
    public async Task PromoteAsync_DraftAlreadyGivenUpByItsOwnDelivery_StillAnswersWithTheRecordItProduced()
    {
        // Arrange
        var harness = Harness();
        var draft = await SaveAsync(harness, "the message as written");
        var promotion = PromotionOver(harness);
        var first = await promotion.PromoteAsync(draft.Id, CancellationToken.None);
        await harness.Drafts.RecordDiscardedAsync(
            Substitute.For<IPersistenceSession>(),
            draft.Id,
            Moment,
            CancellationToken.None);

        // Act
        var retried = await promotion.PromoteAsync(draft.Id, CancellationToken.None);

        // Assert
        Assert.Equal(first.Id, retried.Id);
    }

    /// <summary>
    /// Two callers arriving together both find the draft unpromoted, because a read cannot see a write that has not
    /// happened yet. What makes their two asks one message is the request's identity, which is the draft itself.
    /// </summary>
    [Fact]
    public async Task PromoteAsync_TwoCallersBothFindingTheDraftUnpromoted_QueueOneMessage()
    {
        // Arrange
        var harness = Harness();
        var draft = await SaveAsync(harness, "the message as written");
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var promotion = PromotionOver(harness, outgoingEmails);
        var first = await promotion.PromoteAsync(draft.Id, CancellationToken.None);
        harness.Drafts.ForgetPromotion(draft.Id);

        // Act
        var second = await promotion.PromoteAsync(draft.Id, CancellationToken.None);

        // Assert
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(
            [OutgoingEmailRequester.Draft(draft.Id), OutgoingEmailRequester.Draft(draft.Id)],
            outgoingEmails.OpenRequests.Select(request => request.Requester));
    }

    /// <summary>A draft addressed to nobody has no envelope to build, so it is refused rather than queued.</summary>
    [Fact]
    public async Task PromoteAsync_DraftAddressedToNobody_IsRefusedAndLeavesTheDraftStanding()
    {
        // Arrange
        var harness = Harness();
        var draft = await SaveAsync(harness, "the message as written", addressed: false);
        var promotion = PromotionOver(harness);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => promotion.PromoteAsync(draft.Id, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotAddressed, refusal.ErrorCode);
        Assert.Null(harness.Drafts.Peek(draft.Id)!.PromotedTo);
    }

    /// <summary>
    /// The size this deployment sends is asked at the moment the message would leave, so a draft written while the
    /// bound was larger is refused rather than sent past it — and the draft its author wrote is still there.
    /// </summary>
    [Fact]
    public async Task PromoteAsync_MessageLongerThanTheDeploymentNowSends_IsRefusedAndLeavesTheDraftStanding()
    {
        // Arrange
        var harness = Harness();
        var draft = await SaveAsync(harness, "the message as written");
        var promotion = PromotionOver(harness, bounds: Bounds(maxMessageBytes: 4));

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => promotion.PromoteAsync(draft.Id, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.AuthoredMailBoundExceeded, refusal.ErrorCode);
        Assert.Null(harness.Drafts.Peek(draft.Id)!.PromotedTo);
        Assert.Single(harness.Drafts.Drafts);
    }

    /// <summary>
    /// The recipient policy is asked at promotion too, so a draft written before it tightened cannot leave — and the
    /// refusal costs its author nothing, because the draft and its copy are exactly where they were.
    /// </summary>
    [Fact]
    public async Task PromoteAsync_RecipientTheDeploymentNowRefuses_IsRefusedAndLeavesTheDraftStanding()
    {
        // Arrange
        var harness = Harness();
        var draft = await SaveAsync(harness, "the message as written");
        Assert.True(OutgoingRecipientRule.TryCreateForDomain("example.test", out var denied));
        var promotion = PromotionOver(
            harness,
            governor: OutgoingMailGovernors.Governing(
                recipientPolicy: OutgoingRecipientPolicy.Create([], [denied])));

        // Act
        var refusal = () => promotion.PromoteAsync(draft.Id, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<OutgoingMailRefusedException>(refusal);
        Assert.Null(harness.Drafts.Peek(draft.Id)!.PromotedTo);
        Assert.Equal(MailDraftStage.Filed, harness.Drafts.Peek(draft.Id)!.Stage);
    }

    /// <summary>A deployment that may not send refuses a promotion outright, and the draft is untouched.</summary>
    [Fact]
    public async Task PromoteAsync_DeploymentThatMayNotSend_IsRefusedAndLeavesTheDraftStanding()
    {
        // Arrange
        var harness = Harness();
        var draft = await SaveAsync(harness, "the message as written");
        var promotion = PromotionOver(
            harness,
            governor: OutgoingMailGovernors.Governing(refusal: OutgoingSendRefusalReason.DeploymentIsReadOnly));

        // Act
        var refusal = () => promotion.PromoteAsync(draft.Id, CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<OutgoingMailRefusedException>(refusal);
        Assert.Null(harness.Drafts.Peek(draft.Id)!.PromotedTo);
    }

    /// <summary>
    /// What a caller may be talked into is a bound on the caller rather than on the deployment, so a draft is one more
    /// way of asking and is counted as one. A caller that has filled its period is refused the promotion, and the draft
    /// it wrote is exactly where it was.
    /// </summary>
    [Fact]
    public async Task PromoteAsync_CallerThatFilledItsOwnPeriod_IsRefusedAndLeavesTheDraftStanding()
    {
        // Arrange
        var harness = Harness();
        var first = await SaveAsync(harness, "the first message");
        var second = await SaveAsync(harness, "the second message");
        var ledger = new AuthoredSendUsageLedger(
            AuthoredSendCeilings.Create(TimeSpan.FromDays(1), maxMessagesPerCaller: 1, maxRecipientsPerCaller: 0),
            new FakeTimeProvider(Moment));
        var promotion = PromotionOver(harness, sendGovernor: AuthoredSendGovernors.Governing(ledger: ledger));
        await promotion.PromoteAsync(first.Id, CancellationToken.None);

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => promotion.PromoteAsync(second.Id, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingMailCeilingReached, refusal.ErrorCode);
        Assert.Null(harness.Drafts.Peek(second.Id)!.PromotedTo);
    }

    /// <summary>
    /// The posture on a recipient nothing here vouches for is asked where the message would leave, so drafting is not a
    /// way past it. The address itself stays out of the refusal, as it does on every other surface.
    /// </summary>
    [Fact]
    public async Task PromoteAsync_RecipientNothingVouchesFor_IsRefusedWithoutWritingARecord()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var harness = Harness(outgoingEmails);
        var draft = await SaveAsync(harness, "the message as written");
        var promotion = PromotionOver(
            harness,
            outgoingEmails,
            sendGovernor: AuthoredSendGovernors.Governing(
                settings: new AuthoredSendSettings(UnvouchedRecipientPosture.Refuse)));

        // Act
        var refusal = await Assert.ThrowsAsync<OutgoingMailRefusedException>(
            () => promotion.PromoteAsync(draft.Id, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingRecipientUnvouched, refusal.ErrorCode);
        Assert.DoesNotContain("example.test", refusal.Message, StringComparison.Ordinal);
        Assert.Empty(outgoingEmails.OpenRequests);
        Assert.Null(harness.Drafts.Peek(draft.Id)!.PromotedTo);
    }

    /// <summary>
    /// The draft kept where each address came from, so an address the answered message's own headers named is still not
    /// the caller's word months later. A reply held as a draft is therefore promoted where a stranger the caller named
    /// itself would be refused.
    /// </summary>
    [Fact]
    public async Task PromoteAsync_RecipientDerivedFromTheAnsweredEmail_IsPromotedUnderTheStrictPosture()
    {
        // Arrange
        var harness = Harness();
        var draft = await SaveAsync(
            harness,
            "the answer as written",
            recipient: Recipient(AuthoredRecipientProvenance.DerivedFromAnsweredEmail));
        var promotion = PromotionOver(
            harness,
            sendGovernor: AuthoredSendGovernors.Governing(
                settings: new AuthoredSendSettings(UnvouchedRecipientPosture.Refuse)));

        // Act
        var record = await promotion.PromoteAsync(draft.Id, CancellationToken.None);

        // Assert
        Assert.Equal(OutgoingEmailStage.Recorded, record.Stage);
        Assert.Equal(record.Id, harness.Drafts.Peek(draft.Id)!.PromotedTo);
    }

    /// <summary>
    /// A promotion is a message leaving under somebody's grant, so it reaches the trail the other sending acts reach,
    /// naming the act that dispatched it and how many of its recipients nothing here vouched for.
    /// </summary>
    [Fact]
    public async Task PromoteAsync_AddressedDraft_RecordsTheSendAsAPromotedDraft()
    {
        // Arrange
        var harness = Harness();
        var draft = await SaveAsync(harness, "the message as written");
        var auditor = Substitute.For<IAuthoredSendAuditor>();
        var promotion = PromotionOver(harness, sendGovernor: AuthoredSendGovernors.Governing(auditor: auditor));

        // Act
        var record = await promotion.PromoteAsync(draft.Id, CancellationToken.None);

        // Assert
        await auditor.Received(1).RecordAuthoredSendAsync(
            Arg.Is<AuthoredSend>(send =>
                send != null
                && send.Act == AuthoredSendAct.PromotedDraft
                && send.OutgoingEmailId == record.Id
                && send.AccountId == Account
                && send.RecipientCount == 1
                && send.UnvouchedRecipientCount == 1),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Two callers promoting one draft queue one message, so the trail carries one entry: auditing the second would
    /// report a message as having left twice to whoever reads it for a send they did not expect.
    /// </summary>
    [Fact]
    public async Task PromoteAsync_TwoCallersBothFindingTheDraftUnpromoted_RecordTheSendOnce()
    {
        // Arrange
        var harness = Harness();
        var draft = await SaveAsync(harness, "the message as written");
        var auditor = Substitute.For<IAuthoredSendAuditor>();
        var promotion = PromotionOver(harness, sendGovernor: AuthoredSendGovernors.Governing(auditor: auditor));
        await promotion.PromoteAsync(draft.Id, CancellationToken.None);
        harness.Drafts.ForgetPromotion(draft.Id);

        // Act
        await promotion.PromoteAsync(draft.Id, CancellationToken.None);

        // Assert
        await auditor.Received(1).RecordAuthoredSendAsync(
            Arg.Any<AuthoredSend>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The mark that names the send on the draft is written after the record is already durable, so a mark that never
    /// commits leaves a message on its way. It is audited before that mark for exactly this reason: the retry it leads
    /// to finds the draft unpromoted, is answered with the record that already exists, and would therefore never be the
    /// call that recorded it.
    /// </summary>
    [Fact]
    public async Task PromoteAsync_MarkThatNeverCommits_StillRecordsTheSendThatLeft()
    {
        // Arrange
        var harness = Harness();
        var draft = await SaveAsync(harness, "the message as written");
        var auditor = Substitute.For<IAuthoredSendAuditor>();
        var promotion = PromotionOver(
            harness,
            sendGovernor: AuthoredSendGovernors.Governing(auditor: auditor),
            draftMarkPolicy: ConflictingPolicy());

        // Act
        await Assert.ThrowsAsync<PersistenceConcurrencyConflictException>(
            () => promotion.PromoteAsync(draft.Id, CancellationToken.None));

        // Assert
        await auditor.Received(1).RecordAuthoredSendAsync(
            Arg.Is<AuthoredSend>(send => send != null && send.Act == AuthoredSendAct.PromotedDraft),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Nothing is held under an identifier this system never minted, so a promotion of one is refused.</summary>
    [Fact]
    public async Task PromoteAsync_DraftThisSystemNeverWrote_IsRefused()
    {
        // Arrange
        var harness = Harness();
        var promotion = PromotionOver(harness);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => promotion.PromoteAsync(
                MailDraftId.Create(Guid.CreateVersion7(Moment)),
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
    }

    /// <summary>A delivered send gives the draft up, and a send that has not left leaves it exactly where it was.</summary>
    [Theory]
    [InlineData(OutgoingEmailStage.Sent, 1, 0)]
    [InlineData(OutgoingEmailStage.Recorded, 0, 1)]
    [InlineData(OutgoingEmailStage.Refused, 0, 1)]
    public async Task SettlePromotedAsync_SendAtOneStage_GivesTheDraftUpOnlyOnceItHasLeft(
        OutgoingEmailStage stage,
        int expectedWithdrawals,
        int expectedDraftsHeld)
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var harness = Harness(outgoingEmails);
        var draft = await SaveAsync(harness, "the message as written");
        var promotion = PromotionOver(harness, outgoingEmails);
        var record = await promotion.PromoteAsync(draft.Id, CancellationToken.None);
        outgoingEmails.Arrange(record.Id, stage);

        // Act
        var results = await harness.Pass.SettlePromotedAsync(record.Id, CancellationToken.None);

        // Assert
        Assert.Equal(expectedWithdrawals, harness.Withdrawn.Count);
        Assert.Equal(expectedDraftsHeld, harness.Drafts.Drafts.Count);
        Assert.Equal(expectedWithdrawals, results.Count);
    }

    private static MailDraftHarness Harness(InMemoryOutgoingEmailStore? outgoingEmails = null)
    {
        var harness = new MailDraftHarness(
            new FakeTimeProvider(Moment),
            outgoingEmails ?? new InMemoryOutgoingEmailStore(),
            MailOutboxSettings.Create(
                maxDeliveriesPerPass: 10,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(7),
                maxAttempts: 5,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(8)));

        harness.MapDraftsFolder(Account);

        return harness;
    }

    private static MailDraftPromotion PromotionOver(
        MailDraftHarness harness,
        InMemoryOutgoingEmailStore? outgoingEmails = null,
        IEmailContentStore? contentStore = null,
        OutgoingEmailBounds? bounds = null,
        OutgoingMailGovernor? governor = null,
        AuthoredSendGovernor? sendGovernor = null,
        OptimisticConcurrencyRetryPolicy? draftMarkPolicy = null)
    {
        var store = outgoingEmails ?? new InMemoryOutgoingEmailStore();
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        var retryPolicy = new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            new PersistenceConcurrencyOptions(),
            TimeProvider.System);

        var outbox = new MailOutbox(
            store,
            contentStore ?? Substitute.For<IEmailContentStore>(),
            retryPolicy,
            new MailOutboxSignal(capacity: 8),
            Substitute.For<IJobStore>(),
            Substitute.For<IOutboxOperationStore>(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend),
            governor ?? OutgoingMailGovernors.Permitting(),
            OutgoingMailScreenings.Inactive(),
            new FakeTimeProvider(Moment));

        return new MailDraftPromotion(
            harness.Drafts,
            harness.Contents,
            outbox,
            store,
            draftMarkPolicy ?? retryPolicy,
            bounds ?? Bounds(),
            sendGovernor ?? AuthoredSendGovernors.Permitting(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend));
    }

    private static OutgoingEmailBounds Bounds(long maxMessageBytes = 1_000_000) => new()
    {
        MaxRecipientCount = 16,
        MaxBodyCharacters = 100_000,
        MaxAttachmentCount = 4,
        MaxAttachmentBytes = 1_000_000,
        MaxMessageBytes = maxMessageBytes,
    };

    private static Task<MailDraftRecord> SaveAsync(
        MailDraftHarness harness,
        string body,
        bool addressed = true,
        MailDraftRecipient? recipient = null) =>
        harness.Book.SaveAsync(
            Account,
            OutgoingEmailRequester.Command($"mfctl-{Guid.CreateVersion7(Moment)}"),
            new ComposedMailDraft(
                addressed ? [recipient ?? Recipient()] : [],
                InternetMessageId.Mint("example.test"),
                Encoding.ASCII.GetBytes($"Subject: a draft\r\n\r\n{body}").AsMemory()),
            revises: null,
            CancellationToken.None);

    /// <summary>Builds a commit policy whose every attempt conflicts, which exhausts it and raises.</summary>
    private static OptimisticConcurrencyRetryPolicy ConflictingPolicy()
    {
        var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
        sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>())
                .Returns(PersistenceCommitResult.ConcurrencyConflict);

            return session;
        });

        // One attempt, so the policy is exhausted by the first conflict. A second would await a jittered delay drawn
        // from the system clock, which the claim here — that the audit was written before the mark — needs nothing of.
        return new OptimisticConcurrencyRetryPolicy(
            sessionFactory,
            new PersistenceConcurrencyOptions { MaximumCommitAttempts = 1 },
            TimeProvider.System);
    }

    private static MailDraftRecipient Recipient(
        AuthoredRecipientProvenance provenance = AuthoredRecipientProvenance.NamedByCaller)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "someone@example.test", out var address));

        return new MailDraftRecipient(
            OutgoingRecipient.Create(address, OutgoingRecipientRole.To),
            provenance);
    }
}
