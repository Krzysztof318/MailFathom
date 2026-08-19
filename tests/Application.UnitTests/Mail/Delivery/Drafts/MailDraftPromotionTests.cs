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
        var record = await promotion.PromoteAsync(
            draft.Id,
            OutgoingEmailRequester.Command("mfctl-9c31"),
            CancellationToken.None);

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
        var first = await promotion.PromoteAsync(
            draft.Id,
            OutgoingEmailRequester.Command("mfctl-9c31"),
            CancellationToken.None);

        // Act
        var second = await promotion.PromoteAsync(
            draft.Id,
            OutgoingEmailRequester.Command("mfctl-0000"),
            CancellationToken.None);

        // Assert
        Assert.Equal(first.Id, second.Id);
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
            () => promotion.PromoteAsync(
                draft.Id,
                OutgoingEmailRequester.Command("mfctl-9c31"),
                CancellationToken.None));

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
            () => promotion.PromoteAsync(
                draft.Id,
                OutgoingEmailRequester.Command("mfctl-9c31"),
                CancellationToken.None));

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
        var refusal = () => promotion.PromoteAsync(
            draft.Id,
            OutgoingEmailRequester.Command("mfctl-9c31"),
            CancellationToken.None);

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
        var refusal = () => promotion.PromoteAsync(
            draft.Id,
            OutgoingEmailRequester.Command("mfctl-9c31"),
            CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<OutgoingMailRefusedException>(refusal);
        Assert.Null(harness.Drafts.Peek(draft.Id)!.PromotedTo);
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
                OutgoingEmailRequester.Command("mfctl-9c31"),
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
        var record = await promotion.PromoteAsync(
            draft.Id,
            OutgoingEmailRequester.Command("mfctl-9c31"),
            CancellationToken.None);
        outgoingEmails.SetStage(record.Id, stage);

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
        OutgoingMailGovernor? governor = null)
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
            new FakeTimeProvider(Moment));

        return new MailDraftPromotion(
            harness.Drafts,
            harness.Contents,
            outbox,
            store,
            retryPolicy,
            bounds ?? Bounds(),
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
        bool addressed = true) =>
        harness.Book.SaveAsync(
            Account,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            new ComposedMailDraft(
                addressed ? [Recipient()] : [],
                InternetMessageId.Mint("example.test"),
                Encoding.ASCII.GetBytes($"Subject: a draft\r\n\r\n{body}").AsMemory()),
            revises: null,
            CancellationToken.None);

    private static OutgoingRecipient Recipient()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "someone@example.test", out var address));

        return OutgoingRecipient.Create(address, OutgoingRecipientRole.To);
    }
}
