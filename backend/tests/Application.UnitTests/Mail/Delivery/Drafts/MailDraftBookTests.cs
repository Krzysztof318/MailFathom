// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent;
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

/// <summary>Covers the one way a draft is written, revised, or given up, and what each of those owes the mailbox.</summary>
public sealed class MailDraftBookTests
{
    /// <summary>The literal the screened deployment's detector reports, which stands in for a credential in a draft.</summary>
    private const string ScreenedMarker = "AKIAEXAMPLEKEY";

    private static readonly MailAccountId Account = MailAccountId.Create("work");

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

    /// <summary>An edit stores the new message over the old one and leaves the owner one draft in the folder.</summary>
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
        var result = await harness.Book.DiscardAsync(draft.Id, CancellationToken.None);

        // Assert
        Assert.Equal(MailDraftFilingOutcome.Discarded, result.Outcome);
        Assert.Equal([(ImapUidValidity.Create(1), ImapUid.Create(1))], harness.Withdrawn);
        Assert.Empty(harness.Drafts.Drafts);
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
            () => harness.Book.DiscardAsync(foreign, CancellationToken.None));

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
            () => harness.Book.DiscardAsync(draft.Id, CancellationToken.None));

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
                OutgoingEmailRequester.Command("mfctl-4f2a"),
                Composed("second version"),
                draft.Id,
                CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailDraftNotFound, refusal.ErrorCode);
        Assert.Equal(1, harness.Drafts.Peek(draft.Id)!.Revision);
    }

    /// <summary>The sending grant does not carry the drafting one, because no permission here implies another.</summary>
    /// <remarks>
    /// The pair of refusals is what makes the two halves of authoring separable at all. A deployment that granted
    /// sending alone meant an agent to send the messages it was asked for, and writing into the owner's own drafts
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
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed("first version"),
            revises: null,
            CancellationToken.None);

        // Act
        await harness.Book.SaveAsync(
            Account,
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
    /// message, and no copy in the owner's drafts folder.
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

    private static Task<MailDraftRecord> SaveAsync(MailDraftHarness harness, string body) =>
        harness.Book.SaveAsync(
            Account,
            OutgoingEmailRequester.Command("mfctl-4f2a"),
            Composed(body),
            revises: null,
            CancellationToken.None);

    private static ComposedMailDraft Composed(string body, IReadOnlyList<MailDraftRecipient>? recipients = null) =>
        new(
            recipients ?? [Recipient()],
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
