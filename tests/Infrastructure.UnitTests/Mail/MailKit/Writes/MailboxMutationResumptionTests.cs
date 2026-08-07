// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
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

/// <summary>Proves that a mutation resumed from a recorded stage continues from it rather than starting over.</summary>
/// <remarks>
/// Every test here arranges the state a crash would have left: a journal already at the stage the previous attempt
/// reached. What the crash actually was does not matter to the session — the record is the only thing it reads — so the
/// arrangement is the record and the assertion is which commands the second attempt issued.
/// </remarks>
public sealed class MailboxMutationResumptionTests
{
    private static readonly RemoteFolderPath Archive = RemoteFolderPath.Create(ArchivePath, '/');

    private static readonly RemoteEmailPlacement RecordedPlacement = RemoteEmailPlacement.Reported(
        ImapUidValidity.Create(11U),
        ImapUid.Create(7U));

    /// <summary>The crash between the copy and the expunge: the copy landed, so repeating it would leave two messages.</summary>
    [Fact]
    public async Task RelocateAsync_ResumedAfterAConfirmedCopy_RemovesTheSourceWithoutCopyingAgain()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();
        var journal = new RecordingMailboxMutationJournal(
            MailboxMutationStage.PlacementConfirmed,
            RecordedPlacement);

        // Act
        var placement = await session.RelocateAsync(
            CreateOccurrenceId(42U),
            Archive,
            journal,
            CancellationToken.None);

        // Assert
        await openFolder.DidNotReceive().CopyToAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
        await openFolder.Received(1).StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request => request != null && request.Flags == MessageFlags.Deleted),
            Arg.Any<CancellationToken>());
        await openFolder.Received(1).ExpungeAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 42U),
            Arg.Any<CancellationToken>());
        Assert.Equal(RecordedPlacement, placement);
    }

    /// <summary>The crash between the flag and the expunge, which is the one stage further along the same sequence.</summary>
    [Fact]
    public async Task RelocateAsync_ResumedAfterTheSourceWasFlagged_ExpungesWithoutFlaggingAgain()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();
        var journal = new RecordingMailboxMutationJournal(
            MailboxMutationStage.SourceFlaggedDeleted,
            RecordedPlacement);

        // Act
        var placement = await session.RelocateAsync(
            CreateOccurrenceId(42U),
            Archive,
            journal,
            CancellationToken.None);

        // Assert
        await openFolder.DidNotReceive().CopyToAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IStoreFlagsRequest>(),
            Arg.Any<CancellationToken>());
        await openFolder.Received(1).ExpungeAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(RecordedPlacement, placement);
    }

    /// <summary>
    /// <c>MOVE</c> removes the source as part of the same command, so a relocation that reached a confirmed placement on
    /// a server advertising it is finished. Issuing the fallback's tail here would demand a UIDPLUS the server need not
    /// have, and report a completed relocation as unsupported.
    /// </summary>
    [Fact]
    public async Task RelocateAsync_ResumedOnAMoveCapableServerAfterAConfirmedPlacement_IssuesNothing()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.Move };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();
        var journal = new RecordingMailboxMutationJournal(
            MailboxMutationStage.PlacementConfirmed,
            RecordedPlacement);

        // Act
        var placement = await session.RelocateAsync(
            CreateOccurrenceId(42U),
            Archive,
            journal,
            CancellationToken.None);

        // Assert
        await openFolder.DidNotReceive().MoveToAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
        await openFolder.DidNotReceive().ExpungeAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(RecordedPlacement, placement);
    }

    /// <summary>A copy is the whole of its own mutation, so a confirmed placement leaves nothing to reissue.</summary>
    [Fact]
    public async Task CopyAsync_ResumedAfterAConfirmedPlacement_ReturnsTheRecordedPlacementWithoutCopying()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();
        var journal = new RecordingMailboxMutationJournal(
            MailboxMutationStage.PlacementConfirmed,
            RecordedPlacement);

        // Act
        var placement = await session.CopyAsync(
            CreateOccurrenceId(42U),
            Archive,
            journal,
            CancellationToken.None);

        // Assert
        await openFolder.DidNotReceive().CopyToAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IMailFolder>(),
            Arg.Any<CancellationToken>());
        Assert.Equal(RecordedPlacement, placement);
    }

    /// <summary>A delete resumed after its flag landed reissues only the expunge.</summary>
    [Fact]
    public async Task DeleteAsync_ResumedAfterTheSourceWasFlagged_ExpungesWithoutFlaggingAgain()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();
        var journal = new RecordingMailboxMutationJournal(MailboxMutationStage.SourceFlaggedDeleted);

        // Act
        await session.DeleteAsync(CreateOccurrenceId(42U), journal, CancellationToken.None);

        // Assert
        await openFolder.DidNotReceive().StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IStoreFlagsRequest>(),
            Arg.Any<CancellationToken>());
        await openFolder.Received(1).ExpungeAsync(
            Arg.Is<IList<UniqueId>>(uids => uids != null && uids.Count == 1 && uids[0].Id == 42U),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A `\Seen` store is idempotent for one UID, so its record exists for provenance and never carries a stage a
    /// resumed attempt would skip. Repeating it reaches the same flag state.
    /// </summary>
    [Fact]
    public async Task SetSeenAsync_Always_AnnouncesNoStageAndStoresTheFlag()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = ImapCapabilities.UidPlus };
        var openFolder = CreateWritableFolder();
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();
        var journal = new RecordingMailboxMutationJournal();

        // Act
        await session.SetSeenAsync(CreateOccurrenceId(42U), isSeen: true, journal, CancellationToken.None);

        // Assert
        Assert.Empty(journal.AnnouncedStages);
        await openFolder.Received(1).StoreAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IStoreFlagsRequest>(request => request != null && request.Flags == MessageFlags.Seen),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The placement is announced before the command that would place the email, never after it. That ordering is the
    /// whole crash-safety mechanism: a process that dies while the copy is in flight has to leave a record saying so.
    /// </summary>
    [Theory]
    [InlineData(ImapCapabilities.Move)]
    [InlineData(ImapCapabilities.UidPlus)]
    public async Task RelocateAsync_OnEitherProtocolPath_AnnouncesThePlacementBeforeIssuingIt(
        ImapCapabilities capabilities)
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient { Capabilities = capabilities };
        var announcedBeforeTheCommand = new List<MailboxMutationStage>();
        var openFolder = CreateWritableFolder();
        var journal = new RecordingMailboxMutationJournal();
        openFolder.When(folder => folder.CopyToAsync(
                Arg.Any<IList<UniqueId>>(),
                Arg.Any<IMailFolder>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => announcedBeforeTheCommand.AddRange(journal.AnnouncedStages));
        openFolder.When(folder => folder.MoveToAsync(
                Arg.Any<IList<UniqueId>>(),
                Arg.Any<IMailFolder>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => announcedBeforeTheCommand.AddRange(journal.AnnouncedStages));
        await using var harness = CreateHarness(resilience, client, openFolder);
        await using var session = await harness.OpenSessionAsync();

        // Act
        await session.RelocateAsync(CreateOccurrenceId(42U), Archive, journal, CancellationToken.None);

        // Assert
        Assert.Equal([MailboxMutationStage.PlacementIssued], announcedBeforeTheCommand);
        Assert.Contains(MailboxMutationStage.PlacementConfirmed, journal.AnnouncedStages);
    }
}
