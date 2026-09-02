// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.SharedSources.UnitTests;

/// <summary>Covers the draft store two suites write their drafts through.</summary>
/// <remarks>
/// The movements it refuses are what make it worth testing rather than trusting. A double that quietly revised a draft
/// somebody had given up, or promoted one twice, would let a suite pass against a state the real store never produces —
/// and the suites using it are the ones proving what a draft may become.
/// </remarks>
public sealed class InMemoryMailDraftStoreTests
{
    private static readonly DateTimeOffset Moment = new(2026, 8, 20, 9, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountIdentity Account =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("work"));

    private static readonly IPersistenceSession Session = new IgnoredPersistenceSession();

    private static readonly MailFolderResolution Drafts = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("DRAFTS"),
        RemoteFolderPath.Create("Drafts"));

    private static readonly AppendedMailCopy Appended = new(
        RemoteEmailPlacement.Reported(ImapUidValidity.Create(1), ImapUid.Create(7)),
        InternetMessageId: null);

    [Fact]
    public async Task OpenAsync_AMessageSomebodyWrote_HoldsItAtItsFirstRevision()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();

        // Act
        var draft = await OpenAsync(store);

        // Assert
        Assert.Equal(1, draft.Revision);
        Assert.Equal(Account, draft.Account);
        Assert.Equal(Moment, draft.ComposedAt);
        Assert.Equal(draft, store.Peek(draft.Id));
        Assert.Single(store.Drafts);
    }

    [Fact]
    public async Task ReviseAsync_ADraftBeingWritten_AdvancesItToTheNextRevisionUnderTheSameIdentity()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var draft = await OpenAsync(store);

        // Act
        var revised = await store.ReviseAsync(
            Session,
            draft.Id,
            [],
            mimeByteLength: 32,
            Moment.AddMinutes(5),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(draft.Id, revised.Id);
        Assert.Equal(2, revised.Revision);
        Assert.Equal(Moment.AddMinutes(5), revised.RevisedAt);
        Assert.Single(store.Drafts);
    }

    /// <summary>The record a caller read before a write goes on saying what it said, which is how the real store behaves.</summary>
    [Fact]
    public async Task ReviseAsync_ARecordReadEarlier_LeavesThatRecordSayingWhatItSaid()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var draft = await OpenAsync(store);

        // Act
        await store.ReviseAsync(Session, draft.Id, [], 32, Moment, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, draft.Revision);
    }

    /// <summary>A draft that has been given up is not revised, which is the refusal the real store gives.</summary>
    [Fact]
    public async Task ReviseAsync_ADraftAlreadyGivenUp_RefusesRatherThanWritingAnotherRevision()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var draft = await OpenAsync(store);

        await store.RecordDiscardedAsync(Session, draft.Id, Moment, TestContext.Current.CancellationToken);

        // Act
        var refusal = () => store.ReviseAsync(
            Session,
            draft.Id,
            [],
            32,
            Moment,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(refusal);
    }

    /// <summary>One revision is appended once, which is what stops a resumed attempt from putting a second copy in the folder.</summary>
    [Fact]
    public async Task RecordAppendIssuedAsync_ARevisionAlreadyAppended_RefusesRatherThanIssuingASecondCopy()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var draft = await OpenAsync(store);

        await store.RecordAppendIssuedAsync(Session, draft.Id, Drafts, Moment, TestContext.Current.CancellationToken);

        // Act
        var refusal = () => store.RecordAppendIssuedAsync(
            Session,
            draft.Id,
            Drafts,
            Moment,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(refusal);
        Assert.Equal(MailDraftStage.AppendIssued, store.Peek(draft.Id)!.Stage);
    }

    /// <summary>A confirmation settles an append that went out, so a revision nothing was issued for has nothing to confirm.</summary>
    [Fact]
    public async Task RecordAppendConfirmedAsync_ARevisionWithNoIssuedCopy_RefusesRatherThanRecordingAnAppendNobodyMade()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var draft = await OpenAsync(store);

        // Act
        var refusal = () => store.RecordAppendConfirmedAsync(
            Session,
            draft.Id,
            Appended,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(refusal);
    }

    /// <summary>Confirming twice is the same refusal, because the first confirmation left no copy awaiting one.</summary>
    [Fact]
    public async Task RecordAppendConfirmedAsync_ACopyAlreadyConfirmed_RefusesRatherThanConfirmingItTwice()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var draft = await OpenAsync(store);

        await store.RecordAppendIssuedAsync(Session, draft.Id, Drafts, Moment, TestContext.Current.CancellationToken);
        await store.RecordAppendConfirmedAsync(Session, draft.Id, Appended, TestContext.Current.CancellationToken);

        // Act
        var refusal = () => store.RecordAppendConfirmedAsync(
            Session,
            draft.Id,
            Appended,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(refusal);
        Assert.Equal(MailDraftStage.Filed, store.Peek(draft.Id)!.Stage);
    }

    /// <summary>A copy is settled as withdrawn or as abandoned, and a caller writing any other stage fails here rather than only against PostgreSQL.</summary>
    [Theory]
    [InlineData(MailDraftCopyStage.Issued)]
    [InlineData(MailDraftCopyStage.Standing)]
    public async Task RecordCopySettledAsync_AStageNoSettlementProduces_IsRefusedAsAnArgument(MailDraftCopyStage stage)
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var draft = await OpenAsync(store);

        await store.RecordAppendIssuedAsync(Session, draft.Id, Drafts, Moment, TestContext.Current.CancellationToken);
        await store.RecordAppendConfirmedAsync(Session, draft.Id, Appended, TestContext.Current.CancellationToken);

        // Act
        var refusal = () => store.RecordCopySettledAsync(
            Session,
            draft.Id,
            draft.Revision,
            stage,
            Moment,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(refusal);
        Assert.Equal(MailDraftCopyStage.Standing, store.Peek(draft.Id)!.CurrentCopy!.Stage);
    }

    /// <summary>The settled instant is written once, so a resumed removal does not restamp a copy the first attempt took out.</summary>
    [Fact]
    public async Task RecordCopySettledAsync_ACopySettledTwice_KeepsTheInstantTheFirstSettlementWrote()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var draft = await OpenAsync(store);

        await store.RecordAppendIssuedAsync(Session, draft.Id, Drafts, Moment, TestContext.Current.CancellationToken);
        await store.RecordAppendConfirmedAsync(Session, draft.Id, Appended, TestContext.Current.CancellationToken);

        // Act
        await store.RecordCopySettledAsync(
            Session,
            draft.Id,
            draft.Revision,
            MailDraftCopyStage.Withdrawn,
            Moment,
            TestContext.Current.CancellationToken);
        await store.RecordCopySettledAsync(
            Session,
            draft.Id,
            draft.Revision,
            MailDraftCopyStage.Abandoned,
            Moment.AddHours(1),
            TestContext.Current.CancellationToken);

        // Assert
        var copy = store.Peek(draft.Id)!.FindCopy(draft.Revision)!;
        Assert.Equal(MailDraftCopyStage.Abandoned, copy.Stage);
        Assert.Equal(Moment, copy.SettledAt);
    }

    [Fact]
    public async Task FindAsync_ADraftNobodyHolds_AnswersNothing()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();

        // Act
        var found = await store.FindAsync(
            MailDraftId.Create(Guid.CreateVersion7(Moment)),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(found);
    }

    /// <summary>A draft names the send it became, and the mark is written once so a second promotion cannot rename it.</summary>
    [Fact]
    public async Task RecordPromotedAsync_ADraftPromotedTwice_KeepsTheRecordTheFirstPromotionWrote()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var draft = await OpenAsync(store);
        var first = OutgoingEmailId.Create(Guid.CreateVersion7(Moment));
        var second = OutgoingEmailId.Create(Guid.CreateVersion7(Moment.AddMinutes(1)));

        // Act
        await store.RecordPromotedAsync(Session, draft.Id, first, TestContext.Current.CancellationToken);
        await store.RecordPromotedAsync(Session, draft.Id, second, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(first, store.Peek(draft.Id)!.PromotedTo);
        Assert.Equal(
            store.Peek(draft.Id),
            await store.FindPromotedToAsync(first, TestContext.Current.CancellationToken));
    }

    /// <summary>The state a sequential test cannot otherwise reach: two promotions that both find nothing promoted.</summary>
    [Fact]
    public async Task ForgetPromotion_ADraftAlreadyPromoted_LeavesItReadingAsUnpromoted()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var draft = await OpenAsync(store);

        await store.RecordPromotedAsync(
            Session,
            draft.Id,
            OutgoingEmailId.Create(Guid.CreateVersion7(Moment)),
            TestContext.Current.CancellationToken);

        // Act
        store.ForgetPromotion(draft.Id);

        // Assert
        Assert.Null(store.Peek(draft.Id)!.PromotedTo);
    }

    /// <summary>Giving a draft up is written once, so a resumed attempt does not restamp the moment it happened.</summary>
    [Fact]
    public async Task RecordDiscardedAsync_ADraftGivenUpTwice_KeepsTheInstantTheFirstAttemptWrote()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var draft = await OpenAsync(store);

        // Act
        await store.RecordDiscardedAsync(Session, draft.Id, Moment, TestContext.Current.CancellationToken);
        await store.RecordDiscardedAsync(
            Session,
            draft.Id,
            Moment.AddHours(1),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Moment, store.Peek(draft.Id)!.DiscardedAt);
    }

    [Fact]
    public async Task RemoveAsync_ADraftItHolds_LeavesNothingHeldUnderThatIdentifier()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var draft = await OpenAsync(store);

        // Act
        await store.RemoveAsync(Session, draft.Id, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(store.Drafts);
        Assert.Null(store.Peek(draft.Id));
    }

    /// <summary>A pass reads the drafts of one account that still owe the mail server something, and nobody else's.</summary>
    [Fact]
    public async Task ReadOutstandingAsync_DraftsOfSeveralAccounts_AnswersTheOnesOfTheAccountAsked()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var mine = await OpenAsync(store);
        await OpenAsync(
            store,
            MailAccountIdentity.Create(SyntheticMailOwner.Deployment, MailAccountId.Create("personal")));

        // Act
        var outstanding = await store.ReadOutstandingAsync(
            Account,
            maxCount: 10,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([mine.Id], outstanding.Select(draft => draft.Id));
    }

    /// <summary>The failure and the divergence are written beside the draft rather than over it, and neither moves a stage.</summary>
    [Fact]
    public async Task RecordFailureAsync_AnAttemptThatDidNotSettleTheCopy_LeavesTheDraftWhereItWas()
    {
        // Arrange
        var store = new InMemoryMailDraftStore();
        var draft = await OpenAsync(store);

        // Act
        await store.RecordFailureAsync(
            draft.Id,
            MailFathomErrorCode.OutgoingEmailFilingDestinationUnavailable,
            TestContext.Current.CancellationToken);
        await store.RecordDivergenceAsync(
            draft.Id,
            MailDraftDivergenceReason.DestinationChanged,
            Moment,
            TestContext.Current.CancellationToken);

        // Assert
        var recorded = store.Peek(draft.Id)!;
        Assert.Equal(
            MailFathomErrorCode.OutgoingEmailFilingDestinationUnavailable,
            recorded.LastFailure);
        Assert.Equal(MailDraftDivergenceReason.DestinationChanged, recorded.Divergence?.Reason);
        Assert.Equal(MailDraftStage.Composed, recorded.Stage);
    }

    private static Task<MailDraftRecord> OpenAsync(
        InMemoryMailDraftStore store,
        MailAccountIdentity? account = null) =>
        store.OpenAsync(
            Session,
            account ?? Account,
            OutgoingEmailRequester.Command("one-act"),
            [],
            mimeByteLength: 16,
            Moment,
            TestContext.Current.CancellationToken);
}
