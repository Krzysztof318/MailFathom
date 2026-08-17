// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.UnitTests.TestDoubles;
using MailKit;
using MailKit.Net.Imap;
using NSubstitute;
using Xunit;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapSessionTestContext;
using static MailFathom.Infrastructure.UnitTests.TestDoubles.MailKitImapWriteSessionTestContext;

namespace MailFathom.Infrastructure.UnitTests.Mail.MailKit.Writes;

public sealed class MailKitImapWriteSessionTests
{
    private static readonly RemoteFolderPath Archive = RemoteFolderPath.Create(ArchivePath, '/');

    /// <summary>The flag this exists to write, in both directions, and nothing else on the message moves with it.</summary>
    [Theory]
    [InlineData(true, StoreAction.Add)]
    [InlineData(false, StoreAction.Remove)]
    public async Task SetFlaggedAsync_EitherDirection_StoresOnlyTheFlaggedFlag(bool isFlagged, StoreAction expected)
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.SetFlaggedAsync(
            CreateOccurrenceId(42U),
            isFlagged,
            new RecordingMailboxMutationJournal(),
            CancellationToken.None);

        // Assert
        await openFolder.Received(1).StoreAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 42U),
            Arg.Is<IStoreFlagsRequest>(request =>
                request != null
                && request.Action == expected
                && request.Flags == MessageFlags.Flagged
                && (request.Keywords == null || request.Keywords.Count == 0)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The two flags are separate answers to separate questions, so writing one may never move the other — this is the
    /// write-side counterpart of the invariant every read path is held to.
    /// </summary>
    [Fact]
    public async Task SetFlaggedAsync_Always_LeavesTheSeenFlagAlone()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.SetFlaggedAsync(
            CreateOccurrenceId(42U),
            isFlagged: true,
            new RecordingMailboxMutationJournal(),
            CancellationToken.None);

        // Assert
        await openFolder.DidNotReceive().StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request => request != null && request.Flags.HasFlag(MessageFlags.Seen)),
            Arg.Any<CancellationToken>());

        // The control: the same observation reports a `\Seen` store when one is genuinely issued.
        await session.SetSeenAsync(
            CreateOccurrenceId(42U),
            isSeen: true,
            new RecordingMailboxMutationJournal(),
            CancellationToken.None);
        await openFolder.Received(1).StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request => request != null && request.Flags.HasFlag(MessageFlags.Seen)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An addition asks for what it was given and reads nothing first, because the store is idempotent for one UID.</summary>
    [Fact]
    public async Task AddKeywordsAsync_Always_StoresTheKeywordsWithoutReadingTheMessage()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.AddKeywordsAsync(
            CreateOccurrenceId(42U),
            AuthoredMailKeywords.Create(["$Todo", "$Waiting"]),
            new RecordingMailboxMutationJournal(),
            CancellationToken.None);

        // Assert
        await openFolder.Received(1).StoreAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 42U),
            Arg.Is<IStoreFlagsRequest>(request =>
                request != null
                && request.Action == StoreAction.Add
                && request.Flags == MessageFlags.None
                && request.Keywords != null
                && request.Keywords.Count == 2
                && request.Keywords.Contains("$Todo")
                && request.Keywords.Contains("$Waiting")),
            Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().FetchAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IFetchRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A removal takes off what it names and leaves every other keyword where it was.</summary>
    [Fact]
    public async Task RemoveKeywordsAsync_Always_StoresTheRemovalOfExactlyWhatItNames()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.RemoveKeywordsAsync(
            CreateOccurrenceId(42U),
            AuthoredMailKeywords.Create(["$Todo"]),
            new RecordingMailboxMutationJournal(),
            CancellationToken.None);

        // Assert
        await openFolder.Received(1).StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request =>
                request != null
                && request.Action == StoreAction.Remove
                && request.Keywords != null
                && request.Keywords.Count == 1
                && request.Keywords.Contains("$Todo")),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A replacement is the surplus removed and the named set added, never the <c>STORE FLAGS</c> its name suggests:
    /// that command replaces a message's whole flag set and would clear <c>\Seen</c> while writing a label.
    /// </summary>
    [Fact]
    public async Task SetKeywordsAsync_AMessageCarryingOthers_RemovesTheSurplusAndAddsTheNamedSet()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        AnswerWithCarriedKeywords(openFolder, "$Todo", "$Stale");
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.SetKeywordsAsync(
            CreateOccurrenceId(42U),
            AuthoredMailKeywords.Create(["$Todo", "$Done"]),
            new RecordingMailboxMutationJournal(),
            CancellationToken.None);

        // Assert
        await openFolder.Received(1).StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request =>
                request != null
                && request.Action == StoreAction.Remove
                && request.Keywords != null
                && request.Keywords.Count == 1
                && request.Keywords.Contains("$Stale")),
            Arg.Any<CancellationToken>());
        await openFolder.Received(1).StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request =>
                request != null
                && request.Action == StoreAction.Add
                && request.Keywords != null
                && request.Keywords.Count == 2
                && request.Keywords.Contains("$Done")
                && request.Keywords.Contains("$Todo")),
            Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request => request != null && request.Action == StoreAction.Set),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Naming no keyword is how a replacement clears them all, and it still issues no addition of nothing.</summary>
    [Fact]
    public async Task SetKeywordsAsync_NamingNone_RemovesEveryCarriedKeywordAndAddsNothing()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        AnswerWithCarriedKeywords(openFolder, "$Todo", "$Stale");
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.SetKeywordsAsync(
            CreateOccurrenceId(42U),
            AuthoredMailKeywords.None,
            new RecordingMailboxMutationJournal(),
            CancellationToken.None);

        // Assert
        await openFolder.Received(1).StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request =>
                request != null
                && request.Action == StoreAction.Remove
                && request.Keywords != null
                && request.Keywords.Count == 2),
            Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request => request != null && request.Action == StoreAction.Add),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A message already carrying exactly the named set needs no removal, and none is sent.</summary>
    [Fact]
    public async Task SetKeywordsAsync_NothingSurplus_IssuesNoRemoval()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        AnswerWithCarriedKeywords(openFolder, "$todo");
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.SetKeywordsAsync(
            CreateOccurrenceId(42U),
            AuthoredMailKeywords.Create(["$Todo"]),
            new RecordingMailboxMutationJournal(),
            CancellationToken.None);

        // Assert
        await openFolder.DidNotReceive().StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request => request != null && request.Action == StoreAction.Remove),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A folder that will not keep a keyword between sessions takes the store, reports success, and loses the label —
    /// which reaches an operator as a rule that runs and changes nothing they can find. It is refused instead.
    /// </summary>
    [Fact]
    public async Task AddKeywordsAsync_AFolderThatKeepsNoNewKeyword_IsRefusedBeforeAnythingIsStored()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder(keepsAnyKeyword: false, keptKeywords: "$Junk");
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxMutationUnsupportedException>(() => session.AddKeywordsAsync(
            CreateOccurrenceId(42U),
            AuthoredMailKeywords.Create(["$Todo"]),
            new RecordingMailboxMutationJournal(),
            CancellationToken.None));

        // Assert
        Assert.Equal(MailboxMutation.AddKeywords, refusal.Mutation);
        await openFolder.DidNotReceive().StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IStoreFlagsRequest>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A folder listing the keyword by name keeps it, so naming only those it already keeps is not refused.</summary>
    [Fact]
    public async Task AddKeywordsAsync_AFolderKeepingTheNamedKeyword_StoresIt()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder(keepsAnyKeyword: false, keptKeywords: "$Junk");
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.AddKeywordsAsync(
            CreateOccurrenceId(42U),
            AuthoredMailKeywords.Create(["$junk"]),
            new RecordingMailboxMutationJournal(),
            CancellationToken.None);

        // Assert
        await openFolder.Received(1).StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request => request != null && request.Action == StoreAction.Add),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Taking a keyword off needs nothing of the folder, because removing one that is not there already succeeds.</summary>
    [Fact]
    public async Task RemoveKeywordsAsync_AFolderThatKeepsNoNewKeyword_IsNotRefused()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder(keepsAnyKeyword: false);
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.RemoveKeywordsAsync(
            CreateOccurrenceId(42U),
            AuthoredMailKeywords.Create(["$Todo"]),
            new RecordingMailboxMutationJournal(),
            CancellationToken.None);

        // Assert
        await openFolder.Received(1).StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request => request != null && request.Action == StoreAction.Remove),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A folder selected read-only refuses every command below, so the session is useless without this.</summary>
    [Fact]
    public async Task OpenForWritingAsync_Always_SelectsTheFolderForWriting()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();

        // Act
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Assert
        await openFolder.Received(1).OpenAsync(FolderAccess.ReadWrite, Arg.Any<CancellationToken>());
    }

    /// <summary>The one command RFC 6851 exists for, used wherever the server offers it.</summary>
    [Fact]
    public async Task RelocateAsync_ServerAdvertisesMove_IssuesTheMoveAndReportsWhereTheEmailLanded()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        AnswerWithCopyUid(openFolder, sourceUid: 42U, destinationUid: 7U);
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        var placement = await session.RelocateAsync(CreateOccurrenceId(42U), Archive, new RecordingMailboxMutationJournal(), CancellationToken.None);

        // Assert
        await openFolder.Received(1).MoveToAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 42U),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().CopyToAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
        Assert.True(placement.IsReported);
        Assert.Equal(7U, placement.Uid?.Value);
        Assert.Equal(11U, placement.UidValidity?.Value);
    }

    /// <summary>
    /// The fallback is the main path rather than the exceptional one, and it is written out here rather than delegated
    /// to MailKit's own <c>MoveTo</c>, whose fallback expunges the whole folder on a server without UIDPLUS.
    /// </summary>
    [Fact]
    public async Task RelocateAsync_ServerWithoutMove_CopiesFlagsDeletedAndExpungesOnlyThatEmail()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        AnswerWithCopyUid(openFolder, sourceUid: 42U, destinationUid: 7U);
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        var placement = await session.RelocateAsync(CreateOccurrenceId(42U), Archive, new RecordingMailboxMutationJournal(), CancellationToken.None);

        // Assert
        await openFolder.Received(1).CopyToAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 42U),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
        await openFolder.Received(1).StoreAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 42U),
            Arg.Is<IStoreFlagsRequest>(request => request != null && request.Action == StoreAction.Add && request.Flags == MessageFlags.Deleted),
            Arg.Any<CancellationToken>());
        await openFolder.Received(1).ExpungeAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 42U),
            Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().ExpungeAsync(Arg.Any<CancellationToken>());
        Assert.Equal(7U, placement.Uid?.Value);
    }

    /// <summary>
    /// Without UID EXPUNGE there is no way to remove one message, so the relocation is refused before the copy rather
    /// than after it: a refusal that copied first would leave a duplicate in the destination folder.
    /// </summary>
    [Fact]
    public async Task RelocateAsync_ServerWithNeitherMoveNorUidPlus_RefusesWithoutCopyingAnything()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.None };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxMutationUnsupportedException>(
            () => session.RelocateAsync(CreateOccurrenceId(42U), Archive, new RecordingMailboxMutationJournal(), CancellationToken.None));

        // Assert
        Assert.Equal(MailboxMutation.Relocate, refusal.Mutation);
        Assert.Equal(MailFathomErrorCode.MailboxMutationUnsupported, refusal.ErrorCode);
        await openFolder.DidNotReceive().CopyToAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().ExpungeAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A destination folder the server does not have arrives from MailKit as a plain exception carrying a remote path
    /// and nothing about what was being attempted. Translating it is what lets the change be given up on at once, and
    /// it is what keeps that path out of a message an operator reads.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlacingMutations_DestinationFolderTheServerDoesNotHave_AreRefusedBeforeAnythingIsIssued(bool isCopy)
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        client.AbsentFolderPaths.Add(ArchivePath);
        var openFolder = CreateWritableFolder();
        var journal = new RecordingMailboxMutationJournal();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxDestinationFolderMissingException>(
            () => isCopy
                ? session.CopyAsync(CreateOccurrenceId(42U), Archive, journal, CancellationToken.None)
                : session.RelocateAsync(CreateOccurrenceId(42U), Archive, journal, CancellationToken.None));

        // Assert
        Assert.Equal(MailFathomErrorCode.MailboxMutationDestinationMissing, refusal.ErrorCode);
        Assert.DoesNotContain(ArchivePath, refusal.Message, StringComparison.Ordinal);
        Assert.Equal(MailboxMutationStage.Recorded, journal.Stage);
        await openFolder.DidNotReceive().CopyToAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().MoveToAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Both halves of the placement come out of the <c>COPYUID</c> response, which is the only place the destination's
    /// UIDVALIDITY is available: the folder was resolved by path and never selected, so it reports zero. Reading it
    /// there would raise a failure while describing a relocation that had already moved the message — the worst
    /// possible moment for a non-atomic mutation to report a failure it did not have.
    /// </summary>
    [Fact]
    public async Task RelocateAsync_WithADestinationFolderThatWasNeverSelected_TakesTheUidValidityFromTheResponse()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        AnswerWithCopyUid(openFolder, sourceUid: 42U, destinationUid: 7U, destinationUidValidity: 4242U);
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        var placement = await session.RelocateAsync(CreateOccurrenceId(42U), Archive, new RecordingMailboxMutationJournal(), CancellationToken.None);

        // Assert
        Assert.Equal(4242U, placement.UidValidity?.Value);
        Assert.Equal(7U, placement.Uid?.Value);
    }

    /// <summary>Half an identity is not one, so a UID without a usable validity is reported as no placement at all.</summary>
    [Fact]
    public async Task RelocateAsync_WhenTheResponseNamesAUidWithoutAValidity_ReportsNoPlacementRatherThanFailing()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        AnswerWithCopyUid(openFolder, sourceUid: 42U, destinationUid: 7U, destinationUidValidity: 0U);
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        var placement = await session.RelocateAsync(CreateOccurrenceId(42U), Archive, new RecordingMailboxMutationJournal(), CancellationToken.None);

        // Assert
        Assert.False(placement.IsReported);
    }

    /// <summary>A server that completes the change without a COPYUID response says nothing about where the email is.</summary>
    [Fact]
    public async Task RelocateAsync_ServerNamesNoCopyUid_ReportsThePlacementAsUnnamedRatherThanGuessing()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        var placement = await session.RelocateAsync(CreateOccurrenceId(42U), Archive, new RecordingMailboxMutationJournal(), CancellationToken.None);

        // Assert
        Assert.False(placement.IsReported);
        Assert.Null(placement.Uid);
        Assert.Null(placement.UidValidity);
    }

    [Fact]
    public async Task DeleteAsync_ServerAdvertisesUidPlus_FlagsDeletedAndExpungesOnlyThatEmail()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.DeleteAsync(CreateOccurrenceId(42U), new RecordingMailboxMutationJournal(), CancellationToken.None);

        // Assert
        await openFolder.Received(1).StoreAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 42U),
            Arg.Is<IStoreFlagsRequest>(request => request != null && request.Action == StoreAction.Add && request.Flags == MessageFlags.Deleted),
            Arg.Any<CancellationToken>());
        await openFolder.Received(1).ExpungeAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 42U),
            Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().ExpungeAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A bare EXPUNGE removes every message anybody has flagged deleted, including messages another client flagged and
    /// MailFathom has never seen. Reporting the missing extension is the only answer that is not data loss.
    /// </summary>
    [Fact]
    public async Task DeleteAsync_ServerWithoutUidPlus_RefusesRatherThanExpungingTheWholeFolder()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        var refusal = await Assert.ThrowsAsync<MailboxMutationUnsupportedException>(
            () => session.DeleteAsync(CreateOccurrenceId(42U), new RecordingMailboxMutationJournal(), CancellationToken.None));

        // Assert
        Assert.Equal(MailboxMutation.Delete, refusal.Mutation);
        await openFolder.DidNotReceive().ExpungeAsync(Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IStoreFlagsRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, StoreAction.Add)]
    [InlineData(false, StoreAction.Remove)]
    public async Task SetSeenAsync_EitherDirection_WritesOnlyTheSeenFlag(bool isSeen, StoreAction expectedAction)
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.SetSeenAsync(CreateOccurrenceId(42U), isSeen, new RecordingMailboxMutationJournal(), CancellationToken.None);

        // Assert
        await openFolder.Received(1).StoreAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 42U),
            Arg.Is<IStoreFlagsRequest>(request => request != null && request.Action == expectedAction && request.Flags == MessageFlags.Seen),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CopyAsync_Always_LeavesTheSourceEmailWhereItIs()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        AnswerWithCopyUid(openFolder, sourceUid: 42U, destinationUid: 7U);
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        var placement = await session.CopyAsync(CreateOccurrenceId(42U), Archive, new RecordingMailboxMutationJournal(), CancellationToken.None);

        // Assert
        await openFolder.Received(1).CopyToAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 42U),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IStoreFlagsRequest>(),
            Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().ExpungeAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<CancellationToken>());
        Assert.Equal(7U, placement.Uid?.Value);
    }

    /// <summary>
    /// The regression test the never-marks-mail-read guarantee needs on this side of the line. Writing is now
    /// permitted, so what has to be proven is that only the operation whose purpose is to set <c>\Seen</c> ever does:
    /// the three mutations here each write flags or move messages, and none of them may touch that one.
    /// </summary>
    [Fact]
    public async Task RelocateDeleteAndCopy_OnAServerWithoutMove_NeverWriteTheSeenFlag()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.RelocateAsync(CreateOccurrenceId(42U), Archive, new RecordingMailboxMutationJournal(), CancellationToken.None);
        await session.DeleteAsync(CreateOccurrenceId(43U), new RecordingMailboxMutationJournal(), CancellationToken.None);
        await session.CopyAsync(CreateOccurrenceId(44U), Archive, new RecordingMailboxMutationJournal(), CancellationToken.None);

        // Assert
        await openFolder.DidNotReceive().StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request => request != null && request.Flags.HasFlag(MessageFlags.Seen)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The UIDVALIDITY half of the same guard, which is the one the whole non-atomicity hazard rests on: a folder
    /// recreated under a recovered connection hands the same UIDs to completely different emails, so an occurrence
    /// carrying the right account and the right folder binding but a stale UIDVALIDITY must not be acted on. The
    /// account and folder here deliberately match, so only the UIDVALIDITY comparison can refuse it.
    /// </summary>
    [Fact]
    public async Task RelocateAsync_OccurrenceFromAnEarlierUidValidity_IsRefusedBeforeAnyCommand()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder(uidValidity: 7U);
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();
        var staleOccurrence = CreateOccurrenceId(42U, uidValidity: 9U);

        // Act
        await Assert.ThrowsAsync<ArgumentException>(
            () => session.RelocateAsync(staleOccurrence, Archive, new RecordingMailboxMutationJournal(), CancellationToken.None));

        // Assert
        await openFolder.DidNotReceive().MoveToAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().CopyToAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A session covers one selection, and a UID from another folder names a different email in this one.</summary>
    [Fact]
    public async Task RelocateAsync_OccurrenceFromAnotherFolder_IsRefusedBeforeAnyCommand()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move | ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();
        var foreignOccurrence = EmailOccurrenceId.Create(
            PrimaryAccount,
            ArchiveFolder.Id,
            ImapUidValidity.Create(7U),
            ImapUid.Create(42U));

        // Act
        await Assert.ThrowsAsync<ArgumentException>(
            () => session.RelocateAsync(foreignOccurrence, Archive, new RecordingMailboxMutationJournal(), CancellationToken.None));

        // Assert
        await openFolder.DidNotReceive().MoveToAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
    }
}
