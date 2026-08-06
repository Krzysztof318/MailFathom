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
        var placement = await session.RelocateAsync(CreateOccurrenceId(42U), Archive, CancellationToken.None);

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
        var placement = await session.RelocateAsync(CreateOccurrenceId(42U), Archive, CancellationToken.None);

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
            () => session.RelocateAsync(CreateOccurrenceId(42U), Archive, CancellationToken.None));

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
        var placement = await session.RelocateAsync(CreateOccurrenceId(42U), Archive, CancellationToken.None);

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
        var placement = await session.RelocateAsync(CreateOccurrenceId(42U), Archive, CancellationToken.None);

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
        var placement = await session.RelocateAsync(CreateOccurrenceId(42U), Archive, CancellationToken.None);

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
        await session.DeleteAsync(CreateOccurrenceId(42U), CancellationToken.None);

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
            () => session.DeleteAsync(CreateOccurrenceId(42U), CancellationToken.None));

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
        await session.SetSeenAsync(CreateOccurrenceId(42U), isSeen, CancellationToken.None);

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
        var placement = await session.CopyAsync(CreateOccurrenceId(42U), Archive, CancellationToken.None);

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
        await session.RelocateAsync(CreateOccurrenceId(42U), Archive, CancellationToken.None);
        await session.DeleteAsync(CreateOccurrenceId(43U), CancellationToken.None);
        await session.CopyAsync(CreateOccurrenceId(44U), Archive, CancellationToken.None);

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
            () => session.RelocateAsync(staleOccurrence, Archive, CancellationToken.None));

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
            () => session.RelocateAsync(foreignOccurrence, Archive, CancellationToken.None));

        // Assert
        await openFolder.DidNotReceive().MoveToAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
    }
}
