// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Drafts;

/// <summary>Covers the one way a draft is written, revised, or given up, and what each of those owes the mailbox.</summary>
public sealed class MailDraftBookTests
{
    /// <summary>The literal the screened deployment's detector reports, which stands in for a credential in a draft.</summary>
    private const string ScreenedMarker = "AKIAEXAMPLEKEY";

    private static readonly MailAccountId Account =
        MailAccountId.Create("work");

    private static readonly MailAccountId OtherAccount =
        MailAccountId.Create("personal");

    private static readonly DateTimeOffset Moment = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A saved draft is stored and appended in one call, and needs nobody to be addressed to.</summary>
    [Fact]
    public async Task SaveAsync_NewDraftAddressedToNobody_StoresItAndAppendsItAnyway()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);

        // Act
        var draft = await harness.Book.SaveAsync(
            Account,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("first version", recipients: []),
            revises: null,
            CancellationToken.None);

        // Assert
        Assert.Empty(draft.Recipients);
        Assert.Equal(MailDraftStage.Filed, draft.Stage);
        Assert.Equal(1, harness.AppendCount);
        Assert.Equal("first version", Encoding.ASCII.GetString(harness.Contents.Peek(draft.Id).Span)[^13..]);
    }

    /// <summary>An edit stores the new message over the old one and leaves the user one draft in the folder.</summary>
    [Fact]
    public async Task SaveAsync_RevisionOfAHeldDraft_ReplacesTheMessageAndTheCopy()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var draft = await SaveAsync(harness, "first version");

        // Act
        var revised = await harness.Book.SaveAsync(
            Account,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("second version"),
            draft.Id,
            CancellationToken.None);

        // Assert
        Assert.Equal(draft.Id, revised.Id);
        Assert.Equal(2, revised.Revision);
        Assert.Equal(MailDraftStage.Filed, revised.Stage);
        Assert.Equal(2, harness.AppendCount);
        Assert.Equal([(ImapUidValidity.Create(1), ImapUid.Create(1))], harness.Withdrawn);
        Assert.Single(harness.Drafts.Drafts);
    }

    /// <summary>Giving up a draft takes back the copy this system appended and removes the record with it.</summary>
    [Fact]
    public async Task DiscardAsync_HeldDraft_WithdrawsOnlyTheOccurrenceItAppended()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var draft = await SaveAsync(harness, "first version");

        // Act
        var result = await harness.Book.DiscardAsync(draft.Id, SyntheticUser.Deployment, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Discarded, result.Outcome);
        Assert.Equal([(ImapUidValidity.Create(1), ImapUid.Create(1))], harness.Withdrawn);
        Assert.Empty(harness.Drafts.Drafts);
    }

    /// <summary>A draft on an account MailFathom holds alone is filed into its local drafts folder, and nothing is appended.</summary>
    [Fact]
    public async Task SaveAsync_NewDraftOnAHeldAccount_FilesItIntoTheLocalDraftsFolderWithoutAnAppend()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var held = harness.HoldAccount(Account);

        // Act
        var draft = await SaveAsync(harness, "first version");

        // Assert
        var filed = Assert.Single(held.Stored);
        var placedIn = held.Folders.Folders.Single(folder => folder.Id == held.Folders.Placements[filed.Email]);

        Assert.Equal(MailDraftStage.Filed, draft.Stage);
        Assert.Equal(filed.Email, draft.FiledEmail);
        Assert.Equal(AppendedMailFlags.Draft, filed.Flags);
        Assert.Null(filed.FiledFrom);
        Assert.Equal(MailFolderSpecialUse.Drafts, placedIn.Role);
        Assert.False(harness.Contents.PeekFiled(filed.Email).IsEmpty);
        Assert.Equal(0, harness.AppendCount);
    }

    /// <summary>Saving a held account's draft again replaces the filed message in the same save, with no append and no withdrawal.</summary>
    [Fact]
    public async Task SaveAsync_RevisionOnAHeldAccount_ReplacesTheFiledMessageWithoutAnAppendOrAWithdrawal()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var held = harness.HoldAccount(Account);
        var draft = await SaveAsync(harness, "first version");

        // Act
        var revised = await harness.Book.SaveAsync(
            Account,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("second version"),
            draft.Id,
            CancellationToken.None);

        // Assert
        Assert.Equal(2, held.Stored.Count);
        Assert.Equal(held.Stored[1].Email, revised.FiledEmail);
        Assert.Equal([held.Stored[0].Email], held.Folders.ErasedEmails);
        Assert.Equal([held.Stored[1].Email], held.Folders.Placements.Keys);
        Assert.Equal(0, harness.AppendCount);
        Assert.Empty(harness.Withdrawn);
    }

    /// <summary>Saving a held account's draft again tells its clients, once the commit lands, of the new message and of the one it replaced.</summary>
    [Fact]
    public async Task SaveAsync_RevisionOnAHeldAccount_AnnouncesTheNewMessageAndTheOneItReplaced()
    {
        // Arrange
        var clock = new FakeTimeProvider(Moment);
        var harness = HarnessOn(clock);
        harness.MapDraftsFolder(Account);
        var held = harness.HoldAccount(Account);
        var channel = new RecordingClientSignalChannel();
        await using var signals = new ClientSignals([channel], clock);
        held.Publisher = signals;
        harness.BeginNewScope();
        var draft = await SaveAsync(harness, "first version");
        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();
        var announcedBefore = channel.Published.Count;

        // Act
        await harness.Book.SaveAsync(
            Account,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("second version"),
            draft.Id,
            CancellationToken.None);
        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        var changed = Assert.Single(channel.Published.Skip(announcedBefore));

        Assert.Equal(ClientSignalKind.MailChanged, changed.Kind);
        Assert.Equal(2, changed.Emails.Count);
        Assert.Contains(held.Stored[1].Email, changed.Emails);
        Assert.Contains(held.Stored[0].Email, changed.Emails);
    }

    /// <summary>A revision filed after the drafts role moved to another source folder names the replaced message in a signal of its own rather than in the new folder's.</summary>
    [Fact]
    public async Task SaveAsync_RevisionOnAHeldAccountAfterTheDraftsRoleMoved_AnnouncesTheReplacedMessageOutsideTheNewFolder()
    {
        // Arrange
        var clock = new FakeTimeProvider(Moment);
        var harness = HarnessOn(clock);
        harness.MapDraftsFolder(Account);
        var held = harness.HoldAccount(Account);
        var channel = new RecordingClientSignalChannel();
        await using var signals = new ClientSignals([channel], clock);
        held.Publisher = signals;
        harness.BeginNewScope();
        var draft = await SaveAsync(harness, "first version");
        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();
        var announcedBefore = channel.Published.Count;
        var movedTo = MailFolderAlias.Create("drafts-moved");
        held.UnmapRoles();
        held.MapRole(MailFolderSpecialUse.Drafts, movedTo.Value);
        harness.BeginNewScope();

        // Act
        await harness.Book.SaveAsync(
            Account,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("second version"),
            draft.Id,
            CancellationToken.None);
        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        var changed = channel.Published
            .Skip(announcedBefore)
            .Where(signal => signal.Kind == ClientSignalKind.MailChanged)
            .ToArray();

        Assert.Equal(2, changed.Length);
        Assert.Equal([held.Stored[1].Email], changed.Single(signal => signal.Folder == movedTo).Emails);
        Assert.Equal([held.Stored[0].Email], changed.Single(signal => signal.Folder != movedTo).Emails);
    }

    /// <summary>Giving up a held account's draft erases the filed message and the record, and reaches no mail server.</summary>
    [Fact]
    public async Task DiscardAsync_DraftOnAHeldAccount_ErasesTheFiledMessageAndTheRecord()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var held = harness.HoldAccount(Account);
        var draft = await SaveAsync(harness, "first version");

        // Act
        var result = await harness.Book.DiscardAsync(draft.Id, SyntheticUser.Deployment, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Discarded, result.Outcome);
        Assert.Equal([held.Stored[0].Email], held.Folders.ErasedEmails);
        Assert.Empty(harness.Drafts.Drafts);
        Assert.Empty(harness.Withdrawn);
    }

    /// <summary>
    /// A draft this system did not write is unreachable from here: nothing is held under an identifier it never
    /// minted, so the refusal comes before any folder is opened and no message in the mailbox is touched.
    /// </summary>
    [Fact]
    public async Task DiscardAsync_DraftThisSystemNeverWrote_IsRefusedWithoutReachingTheMailbox()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        await SaveAsync(harness, "first version");
        var foreign = MailDraftId.Create(Guid.CreateVersion7(Moment));

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.DiscardAsync(foreign, SyntheticUser.Deployment, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.Empty(harness.Withdrawn);
        Assert.Single(harness.Drafts.Drafts);
    }

    /// <summary>
    /// A promoted draft is a queued send that giving the draft up would leave untouched, so removing the record would
    /// answer a caller asking for the message not to exist by sending it anyway and keeping nothing that names where it
    /// came from. The copy stays in the folder for the delivery to take back out.
    /// </summary>
    [Fact]
    public async Task DiscardAsync_DraftAlreadyPromotedToASend_IsRefusedAndLeavesTheCopyStanding()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var harness = Harness(outgoingEmails);
        harness.MapDraftsFolder(Account);
        var draft = await SaveAsync(harness, "first version");
        var send = outgoingEmails.Publish(
            OutgoingEmailRequest.Create(
                Account,
                SyntheticUser.Deployment,
                OutgoingEmailRequester.Draft(draft.Id),
                [.. draft.Recipients.Select(recipient => recipient.Recipient)]),
            mimeByteLength: 64);

        await harness.Drafts.RecordPromotedAsync(
            Substitute.For<IPersistenceSession>(),
            draft.Id,
            send.Id,
            CancellationToken.None);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.DiscardAsync(draft.Id, SyntheticUser.Deployment, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.Empty(harness.Withdrawn);
        Assert.False(harness.Drafts.Peek(draft.Id)!.IsDiscarded);
    }

    /// <summary>Revising something this system does not hold is the same answer, so nothing appends over a stranger's mail.</summary>
    [Fact]
    public async Task SaveAsync_RevisingADraftThisSystemNeverWrote_IsRefusedAndAppendsNothing()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.SaveAsync(
                Account,
                SyntheticUser.Deployment,
                OutgoingEmailRequester.Command("mfctl-4f2a"),
                Composed("second version"),
                MailDraftId.Create(Guid.CreateVersion7(Moment)),
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.Equal(0, harness.AppendCount);
        Assert.Empty(harness.Drafts.Drafts);
    }

    /// <summary>A draft of another account is refused as one nobody holds, so revising reaches no mailbox but its own.</summary>
    [Fact]
    public async Task SaveAsync_RevisingADraftOfAnotherAccount_IsRefused()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var draft = await SaveAsync(harness, "first version");

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.SaveAsync(
                MailAccountId.Create("personal"),
                SyntheticUser.Deployment,
                OutgoingEmailRequester.Command("mfctl-4f2a"),
                Composed("second version"),
                draft.Id,
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.Equal(1, harness.Drafts.Peek(draft.Id)!.Revision);
    }

    /// <summary>
    /// ADR 0014 lets two people be assigned one mailbox, so the account alone stops saying whose draft it is. A draft
    /// another assigned user wrote answers exactly as one of another account does — nobody learns from a revision
    /// which drafts the person beside them is part-way through.
    /// </summary>
    [Fact]
    public async Task SaveAsync_RevisingADraftAnotherUserOfTheSameAccountWrote_IsRefused()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var theirs = await harness.Book.SaveAsync(
            Account,
            SyntheticUser.Another,
            OutgoingEmailRequester.Command("mfctl-7b19"),
            Composed("their first version"),
            revises: null,
            CancellationToken.None);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.SaveAsync(
                Account,
                SyntheticUser.Deployment,
                OutgoingEmailRequester.Command("mfctl-7b19"),
                Composed("second version"),
                theirs.Id,
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.Equal(1, harness.Drafts.Peek(theirs.Id)!.Revision);
    }

    /// <summary>A draft another assigned user wrote is not one this caller may give up, however the caller was wired.</summary>
    /// <remarks>
    /// Asked of the book rather than only of <see cref="UserMailDrafts" />, because a caller taking the book as its
    /// own dependency would otherwise reach a draft it did not write on the drafting grant alone — which is what a
    /// mailbox assigned to two people makes reachable.
    /// </remarks>
    [Fact]
    public async Task DiscardAsync_ADraftAnotherUserOfTheSameAccountWrote_IsRefusedAndLeavesItStanding()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var theirs = await harness.Book.SaveAsync(
            Account,
            SyntheticUser.Another,
            OutgoingEmailRequester.Command("mfctl-3c02"),
            Composed("their draft"),
            revises: null,
            CancellationToken.None);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.DiscardAsync(theirs.Id, SyntheticUser.Deployment, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.False(harness.Drafts.Peek(theirs.Id)!.IsDiscarded);
    }

    /// <summary>The sending grant does not carry the drafting one, because no permission here implies another.</summary>
    /// <remarks>
    /// The pair of refusals is what makes the two halves of authoring separable at all. A deployment that granted
    /// sending alone meant an agent to send the messages it was asked for, and writing into the user's own drafts
    /// folder is a different act on a different folder — so it is refused here rather than admitted as the lesser of
    /// the two.
    /// </remarks>
    [Fact]
    public async Task SaveAsync_CallerHoldingOnlyTheSendingGrant_IsRefusedBeforeAnythingIsWritten()
    {
        // Arrange
        var harness = Harness(MailFathomPermission.MailSend);
        harness.MapDraftsFolder(Account);

        // Act
        var refusal = () => harness.Book.SaveAsync(
            Account,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("first version"),
            revises: null,
            CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(refusal);
        Assert.Empty(harness.Drafts.Drafts);
    }

    /// <summary>Giving a draft up is admitted under the grant that wrote it rather than under the one that would send it.</summary>
    /// <remarks>The draft is never looked for, which is the point: the grant is asked first, so a caller holding the wrong one learns nothing about which drafts exist.</remarks>
    [Fact]
    public async Task DiscardAsync_CallerHoldingOnlyTheSendingGrant_IsRefusedBeforeAnyDraftIsLookedFor()
    {
        // Arrange
        var harness = Harness(MailFathomPermission.MailSend);

        // Act
        var refusal = () => harness.Book.DiscardAsync(
            MailDraftId.Create(Guid.CreateVersion7(Moment)),
            SyntheticUser.Deployment,
            CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(refusal);
    }

    /// <summary>Drafting is its own grant, and reading the mailbox is not it.</summary>
    [Fact]
    public async Task SaveAsync_CallerWithoutTheDraftingGrant_IsRefusedBeforeAnythingIsWritten()
    {
        // Arrange
        var harness = Harness(MailFathomPermission.MailRead);
        harness.MapDraftsFolder(Account);

        // Act
        var refusal = () => harness.Book.SaveAsync(
            Account,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("first version"),
            revises: null,
            CancellationToken.None);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(refusal);
        Assert.Empty(harness.Drafts.Drafts);
    }

    /// <summary>
    /// The message is handed over before anything opens a unit of work, which is what makes the object backend legal
    /// here: joining a session is what opens its transaction, so a placement made while no session exists is one made
    /// with no transaction open across it.
    /// </summary>
    [Fact]
    public async Task SaveAsync_NewDraft_PlacesTheMessageBeforeAnyPersistenceSessionExists()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var sessionsOpenWhenPlaced = -1;
        harness.Contents.Placing = () => sessionsOpenWhenPlaced = harness.PersistenceSessionsOpened;

        // Act
        await harness.Book.SaveAsync(
            Account,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("first version"),
            revises: null,
            CancellationToken.None);

        // Assert
        Assert.Equal(0, sessionsOpenWhenPlaced);
        Assert.True(harness.PersistenceSessionsOpened > 0, "the save opened no session at all, so the ordering claim proves nothing");
    }

    /// <summary>
    /// A conflicted attempt replays the whole unit of work, and the placement is not part of it. Every attempt stages
    /// the same locator over the same object, so the endpoint sees one write however many times the commit is repeated.
    /// </summary>
    [Fact]
    public async Task SaveAsync_APersistenceConflictThenACommit_PlacesTheMessageOnceAcrossBothAttempts()
    {
        // Arrange
        var clock = new FakeTimeProvider(Moment);
        var harness = HarnessOn(clock);
        harness.MapDraftsFolder(Account);
        harness.ConflictOnTheNextCommits(1);

        // Act
        var saving = harness.Book.SaveAsync(
            Account,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("first version"),
            revises: null,
            CancellationToken.None);
        await harness.ConflictObserved;
        clock.Advance(TimeSpan.FromSeconds(1));
        var draft = await saving;

        // Assert
        Assert.Equal(1, harness.Contents.PlacementCount);
        Assert.Equal(2, harness.Contents.WriteCount);
        Assert.Equal("first version", Encoding.ASCII.GetString(harness.Contents.Peek(draft.Id).Span)[^13..]);
    }

    /// <summary>
    /// A revision is placed under a key of its own rather than over the previous one, which is what lets a commit that
    /// never happens leave the row pointing at the previous revision's intact object.
    /// </summary>
    [Fact]
    public async Task SaveAsync_ARevision_PlacesItsOwnMessageRatherThanReusingTheOnesBefore()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var first = await harness.Book.SaveAsync(
            Account,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("first version"),
            revises: null,
            CancellationToken.None);

        // Act
        await harness.Book.SaveAsync(
            Account,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-4f2b"),
            Composed("second version"),
            first.Id,
            CancellationToken.None);

        // Assert
        Assert.Equal(2, harness.Contents.PlacementCount);
    }

    private static MailDraftHarness Harness(params IEnumerable<MailFathomPermission> permissions) =>
        HarnessOn(new FakeTimeProvider(Moment), permissions);

    /// <summary>Builds the harness over a clock the test keeps, which is what a test that has to advance one needs.</summary>
    private static MailDraftHarness HarnessOn(
        TimeProvider clock,
        params IEnumerable<MailFathomPermission> permissions) => new(
        clock,
        new InMemoryOutgoingEmailStore(),
        Settings(),
        permissions);

    /// <summary>
    /// A draft carrying what this deployment screens outgoing mail for leaves nothing behind: no record, no stored
    /// message, and no copy in the user's drafts folder.
    /// </summary>
    [Fact]
    public async Task SaveAsync_DraftCarryingScreenedMaterial_RefusesAndWritesNothing()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);

        using var egress = ScanningSensitiveContentEgress.Finding(ScreenedMarker, new FakeTimeProvider(Moment));

        harness.ScreenWith(OutgoingMailScreenings.Through(egress.Screen));

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.SaveAsync(
                Account,
                SyntheticUser.Deployment,
                OutgoingEmailRequester.Command("mfctl-4f2a"),
                Composed($"the deployment key is {ScreenedMarker}"),
                revises: null,
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingMailContentRefused, refusal.ErrorCode);
        Assert.Contains(MarkerSensitiveContentScanner.Category.Name, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ScreenedMarker, refusal.Message, StringComparison.Ordinal);
        Assert.Empty(harness.Drafts.Drafts);
        Assert.Equal(0, harness.AppendCount);
    }

    /// <summary>
    /// A draft the ceiling cut is refused for the length rather than for a category, and the caller reads a code whose
    /// remedy is its own: write a shorter draft, or have the operator raise the ceiling. It is asserted through the
    /// book because that is where the code reaches a caller.
    /// </summary>
    [Fact]
    public async Task SaveAsync_DraftLongerThanOneScanAnalyzes_RefusesForTheLengthAndWritesNothing()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);

        using var egress = ScanningSensitiveContentEgress.Finding(
            ScreenedMarker,
            new FakeTimeProvider(Moment),
            bounds: SensitiveContentScanBounds.Create(
                maximumAnalyzedCharacters: 16,
                TimeSpan.FromSeconds(15),
                maximumConcurrentScans: 4));

        harness.ScreenWith(OutgoingMailScreenings.Through(egress.Screen));

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.SaveAsync(
                Account,
                SyntheticUser.Deployment,
                OutgoingEmailRequester.Command("mfctl-4f2a"),
                Composed("a draft far longer than this deployment analyzes in one scan"),
                revises: null,
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingMailNotFullyScanned, refusal.ErrorCode);
        Assert.Empty(harness.Drafts.Drafts);
        Assert.Equal(0, harness.AppendCount);
    }

    /// <summary>
    /// A draft is put on a mail server exactly as a send is, so a file nothing could read stops it there too — and with
    /// the same code, because the remedy is the same one and the sentence beside it is what tells the two acts apart.
    /// </summary>
    [Fact]
    public async Task SaveAsync_DraftAttachingAFileNothingCouldRead_RefusesForTheFileAndWritesNoDraft()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);

        using var egress = ScanningSensitiveContentEgress.Finding(ScreenedMarker, new FakeTimeProvider(Moment));

        harness.ScreenWith(OutgoingMailScreenings.Through(
            egress.Screen,
            OutgoingAttachmentRefusal.NotRead));

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.SaveAsync(
                Account,
                SyntheticUser.Deployment,
                OutgoingEmailRequester.Command("mfctl-4f2a"),
                Composed("an ordinary covering note"),
                revises: null,
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingMailAttachmentNotRead, refusal.ErrorCode);
        Assert.Empty(harness.Drafts.Drafts);
        Assert.Equal(0, harness.AppendCount);
    }

    /// <summary>
    /// A revision is a new message and is screened as one, so a draft written before the screen was switched on cannot
    /// carry its way past it one edit at a time.
    /// </summary>
    [Fact]
    public async Task SaveAsync_RevisionCarryingScreenedMaterial_RefusesAndLeavesTheHeldDraftAsItWas()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var draft = await SaveAsync(harness, "first version");

        using var egress = ScanningSensitiveContentEgress.Finding(ScreenedMarker, new FakeTimeProvider(Moment));

        harness.ScreenWith(OutgoingMailScreenings.Through(egress.Screen));

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.SaveAsync(
                Account,
                SyntheticUser.Deployment,
                OutgoingEmailRequester.Command("mfctl-4f2a"),
                Composed($"now with {ScreenedMarker}"),
                draft.Id,
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.OutgoingMailContentRefused, refusal.ErrorCode);
        Assert.Equal(1, harness.AppendCount);
        Assert.Equal(
            "first version",
            Encoding.ASCII.GetString(harness.Contents.Peek(draft.Id).Span)[^13..]);
    }

    /// <summary>A draft carrying nothing the deployment screens for is stored and appended exactly as before.</summary>
    [Fact]
    public async Task SaveAsync_ScreenedDeploymentAndAnOrdinaryDraft_StoresIt()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);

        using var egress = ScanningSensitiveContentEgress.Finding(ScreenedMarker, new FakeTimeProvider(Moment));

        harness.ScreenWith(OutgoingMailScreenings.Through(egress.Screen));

        // Act
        var draft = await SaveAsync(harness, "an ordinary draft");

        // Assert
        Assert.Equal(MailDraftStage.Filed, draft.Stage);
        Assert.Equal(1, harness.AppendCount);
    }

    private static MailDraftHarness Harness(InMemoryOutgoingEmailStore outgoingEmails) => new(
        new FakeTimeProvider(Moment),
        outgoingEmails,
        Settings());

    private static MailOutboxSettings Settings() => MailOutboxSettings.Create(
        maxDeliveriesPerPass: 10,
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(7),
        maxAttempts: 5,
        TimeSpan.FromMinutes(1),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(8));

    /// <summary>
    /// A revision naming another account's draft reads none of its files, so a foreign identifier answers exactly as
    /// one nobody holds rather than by the size of somebody else's attachments.
    /// </summary>
    [Fact]
    public async Task ReadStagedAttachmentsAsync_ADraftAnotherAccountHolds_IsRefusedBeforeAnyOctetIsRead()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var theirs = await harness.Book.SaveAsync(
            OtherAccount,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-9c31"),
            Composed("their first version"),
            revises: null,
            CancellationToken.None);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.ReadStagedAttachmentsAsync(
                Account,
                SyntheticUser.Deployment,
                theirs.Id,
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
    }

    /// <summary>
    /// The files are the other half of the same claim: a draft of somebody the mailbox is also assigned to reads no
    /// octet of theirs, so the refusal is the same size and the same code as one nobody holds.
    /// </summary>
    [Fact]
    public async Task ReadStagedAttachmentsAsync_ADraftAnotherUserOfTheSameAccountHolds_IsRefusedBeforeAnyOctetIsRead()
    {
        // Arrange
        var harness = Harness();
        harness.MapDraftsFolder(Account);
        var theirs = await harness.Book.SaveAsync(
            Account,
            SyntheticUser.Another,
            OutgoingEmailRequester.Command("mfctl-2d70"),
            Composed("their first version"),
            revises: null,
            CancellationToken.None);

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.ReadStagedAttachmentsAsync(
                Account,
                SyntheticUser.Deployment,
                theirs.Id,
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
    }

    /// <summary>A draft nobody holds is refused the same way, which is what makes the two indistinguishable.</summary>
    [Fact]
    public async Task ReadStagedAttachmentsAsync_ADraftNobodyHolds_IsRefusedTheSameWay()
    {
        // Arrange
        var harness = Harness();

        // Act
        var refusal = await Assert.ThrowsAsync<MailDraftRefusedException>(
            () => harness.Book.ReadStagedAttachmentsAsync(
                Account,
                SyntheticUser.Deployment,
                MailDraftId.Create(Guid.CreateVersion7()),
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
    }

    private static Task<MailDraftRecord> SaveAsync(MailDraftHarness harness, string body) =>
        harness.Book.SaveAsync(
            Account,
            SyntheticUser.Deployment,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed(body),
            revises: null,
            CancellationToken.None);

    private static ComposedMailDraft Composed(string body, IReadOnlyList<MailDraftRecipient>? recipients = null) =>
        new(
            recipients ?? [Recipient()],
            "a draft",
            InternetMessageId.Mint("example.test"),
            Encoding.ASCII.GetBytes($"Subject: a draft\r\n\r\n{body}").AsMemory());

    private static MailDraftRecipient Recipient()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "someone@example.test", out var address));

        return new MailDraftRecipient(
            OutgoingRecipient.Create(address, OutgoingRecipientRole.To),
            AuthoredRecipientProvenance.NamedByCaller);
    }
}
