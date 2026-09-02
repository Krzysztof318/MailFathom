// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Jobs;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Drafts;

/// <summary>Covers the three acts an owner takes on one of their own drafts, and whose drafts each may reach.</summary>
/// <remarks>
/// The claim under test is the same one three times: an identifier becomes a draft the caller's own owner holds before
/// anything acts on it, and a draft another owner holds answers exactly as one nobody holds. What each act then does is
/// the book's and the promotion's own, and is covered where those are.
/// </remarks>
public sealed class OwnerMailDraftsTests
{
    private static readonly DateTimeOffset Moment = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, Work);

    private static readonly MailAccountIdentity TheirAccount =
        MailAccountIdentity.Create(SyntheticMailOwner.Another, Work);

    private static readonly ReadOnlyMemory<byte> ComposedMime =
        Encoding.ASCII.GetBytes("Subject: a draft\r\n\r\nHello.").AsMemory();

    /// <summary>Giving up one of this owner's own drafts reaches the book and takes the copies out of the folder.</summary>
    [Fact]
    public async Task DiscardAsync_ADraftThisOwnerHolds_GivesItUp()
    {
        // Arrange
        var harness = Harness();
        var draft = await SaveAsync(harness, Account);
        var drafts = OwnerDraftsOver(harness);

        // Act
        var result = await drafts.DiscardAsync(draft.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(draft.Id, result.DraftId);
        Assert.Empty(harness.Drafts.Drafts);
    }

    /// <summary>A draft another owner holds is refused as one nobody holds, and is left exactly as it was.</summary>
    [Fact]
    public async Task DiscardAsync_ADraftAnotherOwnerHolds_IsRefusedAndLeavesItStanding()
    {
        // Arrange
        var harness = Harness();
        var theirs = await SaveAsync(harness, TheirAccount);
        var drafts = OwnerDraftsOver(harness);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => drafts.DiscardAsync(theirs.Id, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.False(harness.Drafts.Peek(theirs.Id)!.IsDiscarded);
    }

    /// <summary>Sending one of this owner's drafts writes the ordinary outgoing record every send is written down as.</summary>
    [Fact]
    public async Task SendAsync_ADraftThisOwnerHolds_QueuesItAsAnOrdinarySend()
    {
        // Arrange
        var harness = Harness();
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var draft = await SaveAsync(harness, Account);
        var drafts = OwnerDraftsOver(harness, outgoingEmails: outgoingEmails);

        // Act
        var queued = await drafts.SendAsync(draft.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(queued.Id, harness.Drafts.Peek(draft.Id)!.PromotedTo);
    }

    /// <summary>A draft another owner holds is refused as one nobody holds, and nothing is queued.</summary>
    [Fact]
    public async Task SendAsync_ADraftAnotherOwnerHolds_IsRefusedAndQueuesNothing()
    {
        // Arrange
        var harness = Harness();
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var theirs = await SaveAsync(harness, TheirAccount);
        var drafts = OwnerDraftsOver(harness, outgoingEmails: outgoingEmails);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => drafts.SendAsync(theirs.Id, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.Null(harness.Drafts.Peek(theirs.Id)!.PromotedTo);
    }

    /// <summary>Builds the owner-facing acts over the harness a test arranged.</summary>
    private static OwnerMailDrafts OwnerDraftsOver(
        MailDraftHarness harness,
        AccessAuthorization? authorization = null,
        InMemoryOutgoingEmailStore? outgoingEmails = null)
    {
        var callerAuthorization = authorization ?? AccessAuthorizations.ForCallerGranted(
            MailFathomPermission.MailDraftsWrite,
            MailFathomPermission.MailSend);

        return new OwnerMailDrafts(
            OwnedMailAccountCatalogs.For(callerAuthorization, SyntheticServedAccount.Of(Work)),
            harness.Drafts,
            harness.Book,
            PromotionOver(harness, outgoingEmails ?? new InMemoryOutgoingEmailStore()),
            callerAuthorization);
    }

    /// <summary>Builds the promotion over the same harness, so a send reads the draft the book wrote.</summary>
    private static MailDraftPromotion PromotionOver(
        MailDraftHarness harness,
        InMemoryOutgoingEmailStore outgoingEmails)
    {
        var retryPolicy = CommittingPolicy();

        var outbox = new MailOutbox(
            outgoingEmails,
            ContentStores.Substituted(),
            retryPolicy,
            new MailOutboxSignal(capacity: 8),
            Substitute.For<IJobStore>(),
            Substitute.For<IOutboxOperationStore>(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend),
            OutgoingMailGovernors.Permitting(),
            OutgoingMailScreenings.Inactive(),
            new FakeTimeProvider(Moment));

        return new MailDraftPromotion(
            harness.Drafts,
            harness.Contents,
            outbox,
            outgoingEmails,
            retryPolicy,
            new OutgoingEmailBounds
            {
                MaxRecipientCount = 16,
                MaxBodyCharacters = 100_000,
                MaxAttachmentCount = 4,
                MaxAttachmentBytes = 1_000_000,
                MaxMessageBytes = 1_000_000,
            },
            AuthoredSendGovernors.Permitting(),
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend));
    }

    /// <summary>Builds a commit policy whose every attempt commits.</summary>
    private static OptimisticConcurrencyRetryPolicy CommittingPolicy()
    {
        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var session = Substitute.For<IPersistenceSession>();
            session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);

            return session;
        });

        return new OptimisticConcurrencyRetryPolicy(
            sessions,
            new PersistenceConcurrencyOptions(),
            new FakeTimeProvider(Moment));
    }

    /// <summary>Builds the draft side of a deployment with its drafts folder mapped, which is what makes a copy possible.</summary>
    private static MailDraftHarness Harness()
    {
        var harness = new MailDraftHarness(
            new FakeTimeProvider(Moment),
            new InMemoryOutgoingEmailStore(),
            MailOutboxSettings.Create(
                maxDeliveriesPerPass: 10,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(7),
                maxAttempts: 5,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(8)),
            MailFathomPermission.MailDraftsWrite,
            MailFathomPermission.MailSend);

        harness.MapDraftsFolder(Work);

        return harness;
    }

    /// <summary>Writes one draft down for one account, which is the arrangement every test here starts from.</summary>
    private static Task<MailDraftRecord> SaveAsync(
        MailDraftHarness harness,
        MailAccountIdentity account) =>
        harness.Book.SaveAsync(
            account,
            OutgoingEmailRequester.Command($"mfctl-{account.Owner.Value:N}"),
            new ComposedMailDraft(
                [Recipient()],
                "a draft",
                InternetMessageId.Mint("example.test"),
                ComposedMime),
            revises: null,
            CancellationToken.None);

    /// <summary>Names one person the draft is addressed to, so a promotion has an envelope to build.</summary>
    private static MailDraftRecipient Recipient()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "someone@example.test", out var address));

        return new MailDraftRecipient(
            OutgoingRecipient.Create(address, OutgoingRecipientRole.To),
            AuthoredRecipientProvenance.NamedByCaller);
    }
}
