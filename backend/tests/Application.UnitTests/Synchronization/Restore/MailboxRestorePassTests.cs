// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Drain;
using MailFathom.Application.Synchronization.Restore;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Transport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Synchronization.Restore;

public sealed class MailboxRestorePassTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("personal");

    private static readonly MailFolderResolution Inbox = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        RemoteFolderPath.Create("INBOX"));

    private static readonly MailFolderResolution Archive = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("archive"),
        RemoteFolderPath.Create("Archive"));

    private static readonly MailFolderAlias Unbound = MailFolderAlias.Create("unbound");

    private static readonly DateTimeOffset RunInstant = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset ArrivedAt = new(2026, 8, 1, 9, 30, 0, TimeSpan.Zero);

    private static readonly MailTransportSecurityPolicy TransportPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    [Fact]
    public async Task RestoreAsync_AccountMirrorsItsSource_PutsNothingBack()
    {
        // Arrange
        var context = new RestoreContext(MailAccountCustodyState.Mirrored)
            .AwaitingAppendOf(Drained(Inbox.Alias));

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.AppendedCount);
        Assert.Empty(context.Store.IssuedAppends);
        await context.WriteSessionFactory.DidNotReceive().OpenForWritingAsync(
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderResolution>(),
            Arg.Any<MailTransportSecurityPolicy>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_AccountIsStillBeingHeld_PutsNothingBack()
    {
        // Arrange
        var context = new RestoreContext(Held)
            .AwaitingAppendOf(Drained(Inbox.Alias));

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.AppendedCount);
        Assert.Empty(context.Store.IssuedAppends);
    }

    [Fact]
    public async Task RestoreAsync_MessageTheDrainTookOff_AppendsItWithItsFlagsKeywordsAndArrival()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingAppendOf(Drained(Inbox.Alias, seen: true, flagged: true, keywords: ["$Forwarded"]));

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.AppendedCount);
        Assert.Equal(0, report.UnansweredAppendCount);
        Assert.Equal([Inbox], context.OpenedFolders);

        var state = Assert.Single(context.AppendedStates);
        Assert.True(state.IsSeen);
        Assert.True(state.IsFlagged);
        Assert.Equal(["$FORWARDED"], state.Keywords.Values);
        Assert.Equal([ArrivedAt], context.AppendedInternalDates);
    }

    /// <summary>The occurrence the source named is what makes the append known to have happened.</summary>
    [Fact]
    public async Task RestoreAsync_SourceNamedWhereItPutTheCopy_WritesThatOccurrenceAndSettlesTheRecord()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingAppendOf(Drained(Inbox.Alias));

        // Act
        await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var occurrence = Assert.Single(context.Store.ConfirmedOccurrences);
        Assert.Equal(Inbox.Id, occurrence.FolderResolutionId);
        Assert.Equal(ImapUidValidity.Create(7), occurrence.UidValidity);
        Assert.Equal(ImapUid.Create(41), occurrence.Uid);
        Assert.Empty(await context.Pass.ReadUnansweredAppendsAsync(Account, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// An <c>APPEND</c> is not idempotent, so the record goes in before the command does: the folder may hold the copy
    /// and nothing it shows afterwards tells one MailFathom appended apart from one somebody else put there.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_SourceNeverAnsweredTheAppend_LeavesTheRecordStandingAsAnUnknownOutcome()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingAppendOf(Drained(Inbox.Alias))
            .WithUnreachableSource();

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.AppendedCount);
        Assert.Equal(1, report.UnansweredAppendCount);
        Assert.Equal(1, report.Failures[MailboxRestoreFailure.SourceUnavailable]);
        Assert.Single(context.Store.IssuedAppends);
        Assert.Single(await context.Pass.ReadUnansweredAppendsAsync(Account, TestContext.Current.CancellationToken));
    }

    /// <summary>A server that accepted the append and named nowhere left the same unknown outcome behind.</summary>
    [Fact]
    public async Task RestoreAsync_SourceNamedNowhereItPutTheCopy_LeavesTheRecordStandingRatherThanGuessing()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingAppendOf(Drained(Inbox.Alias))
            .WithSourceThatNamesNoPlacement();

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.AppendedCount);
        Assert.Equal(1, report.UnansweredAppendCount);
        Assert.Empty(report.Failures);
        Assert.Empty(context.Store.ConfirmedOccurrences);
        Assert.Single(await context.Pass.ReadUnansweredAppendsAsync(Account, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// The folder is part of the occurrence, so a message somebody moved while the account was held takes the identity
    /// of the folder it is in now rather than the one its row bound before the drain took it off.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_MessageWasMovedWhileTheAccountWasHeld_WritesTheOccurrenceOfTheFolderItWentBackInto()
    {
        // Arrange
        var context = new RestoreContext(Restoring).AwaitingAppendOf(Drained(Archive.Alias));

        // Act
        await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([Archive], context.OpenedFolders);
        Assert.Equal(Archive.Id, Assert.Single(context.Store.ConfirmedOccurrences).FolderResolutionId);
    }

    /// <summary>
    /// Synchronization meeting the appended copy as an arrival stores it beside the message it is a copy of, so the
    /// occurrence is taken and the message may now be on the source twice — which is an operator's to establish.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_SomethingElseAlreadyHoldsTheOccurrence_LeavesTheRecordStandingRatherThanAppendingAgain()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingAppendOf(Drained(Inbox.Alias))
            .WithTheOccurrenceAlreadyHeld();

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.AppendedCount);
        Assert.Equal(1, report.UnansweredAppendCount);
        Assert.False(report.EndedTheRestore);
        Assert.Single(await context.Pass.ReadUnansweredAppendsAsync(Account, TestContext.Current.CancellationToken));
    }

    /// <summary>A second <c>APPEND</c> is a second message in somebody's folder rather than a repeat of the first.</summary>
    [Fact]
    public async Task RestoreAsync_AnAppendForThatMessageIsAlreadyStanding_IssuesNoSecondOne()
    {
        // Arrange
        var drained = Drained(Inbox.Alias);
        var context = new RestoreContext(Restoring)
            .AwaitingAppendOf(drained)
            .WithAppendStandingFor(drained);

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.AppendedCount);
        Assert.Empty(context.Store.IssuedAppends);
        await context.WriteSession.DidNotReceive().AppendRestoredAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<RestoredEmailState>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RestoreAsync_AnAppendIsStandingUnanswered_KeepsTheAccountRestoring()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingAppendOf(Drained(Inbox.Alias))
            .WithUnreachableSource();

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(report.EndedTheRestore);
        Assert.Equal(MailAccountCustodyPhase.Restoring, context.Custody.StateOf(Account)!.Phase);
    }

    [Fact]
    public async Task RestoreAsync_TheLastMessageWentBack_TakesTheAccountToMirroring()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingAppendOf(Drained(Inbox.Alias));

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(report.EndedTheRestore);
        Assert.Equal(MailAccountCustodyPhase.Mirrored, context.Custody.StateOf(Account)!.Phase);
    }

    /// <summary>A mailbox the source still has to be emptied of is a mailbox MailFathom is still the truth about.</summary>
    [Fact]
    public async Task RestoreAsync_TheDrainStillOwesASourceRemoval_KeepsTheAccountRestoring()
    {
        // Arrange
        var context = new RestoreContext(Restoring).AwaitingSourceRemoval();

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(report.EndedTheRestore);
        Assert.Equal(MailAccountCustodyPhase.Restoring, context.Custody.StateOf(Account)!.Phase);
    }

    /// <summary>An operator who asked to hold the mailbox again is not somebody this pass hands the source back to.</summary>
    [Fact]
    public async Task RestoreAsync_TheAccountIsAskedToBeHeldAgain_LeavesThePhaseAlone()
    {
        // Arrange
        var context = new RestoreContext(
            new MailAccountCustodyState(MailAccountCustody.HoldMailbox, MailAccountCustodyPhase.Restoring));

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(report.EndedTheRestore);
        Assert.Equal(MailAccountCustodyPhase.Restoring, context.Custody.StateOf(Account)!.Phase);
    }

    /// <summary>
    /// The drain never reached these, so the source still holds them where it always did — and what it owes them is the
    /// read, the star, and the labels somebody gave the message while the account was held.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_MessageTheDrainNeverReached_WritesItsSeenFlaggedAndKeywordStateAsMutationRecords()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingStateWriteOf(StillOnTheSource(Inbox, Inbox.Alias, seen: true, keywords: ["$Label1"]));

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.StateWrittenCount);
        Assert.Equal(
            [MailboxMutation.SetSeen, MailboxMutation.SetFlagged, MailboxMutation.SetKeywords],
            context.Mutations.OpenedRequests.Select(request => request.Mutation));
        Assert.Single(context.Store.StateWritten);
    }

    /// <summary>
    /// A second hold-and-restore cycle owes the source the second hold's state, and a completed mutation record is
    /// kept for good — so for a message nobody moved the occurrence is the same one cycle later and the requester is
    /// the only part of the identity left to tell the two restores apart. Naming the mechanism alone would let the
    /// first cycle's finished records answer this one: nothing would be carried, the walk would stamp past the
    /// message, and the phase would end saying the mailbox was back.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_TheAccountIsRestoredASecondTime_OpensRecordsOfItsOwnRatherThanTheFirstRestores()
    {
        // Arrange
        var message = StillOnTheSource(Inbox, Inbox.Alias, seen: true, keywords: ["$Label1"]);
        var records = new InMemoryMailboxMutationRecordStore();

        var firstRestore = new RestoreContext(RestoringAt(1), mutations: records).AwaitingStateWriteOf(message);
        await firstRestore.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        var openedByTheFirstRestore = records.OpenedRecordCount;
        var secondRestore = new RestoreContext(RestoringAt(2), mutations: records).AwaitingStateWriteOf(message);

        // Act
        var report = await secondRestore.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, openedByTheFirstRestore);
        Assert.Equal(1, report.StateWrittenCount);
        Assert.Equal(openedByTheFirstRestore * 2, records.OpenedRecordCount);
        Assert.Equal(
            ["custody-restore:1", "custody-restore:2"],
            records.OpenedRequests.Select(request => request.Requester.Identity).Distinct());
    }

    /// <summary>
    /// A move somebody made while the account was held reaches the source as an ordinary relocation, and it is opened
    /// last: the converger carries these in the order they were opened, so a move ahead of them would leave the three
    /// <c>UID STORE</c> commands naming a UID the folder no longer holds — which a server answers as success.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_MessageWasMovedWhileTheAccountWasHeld_WritesTheRelocationBehindItsState()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingStateWriteOf(StillOnTheSource(Inbox, Archive.Alias));

        // Act
        await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                MailboxMutation.SetSeen,
                MailboxMutation.SetFlagged,
                MailboxMutation.SetKeywords,
                MailboxMutation.Relocate,
            ],
            context.Mutations.OpenedRequests.Select(request => request.Mutation));

        var relocation = context.Mutations.OpenedRequests[^1];
        Assert.Equal(Archive.RemotePath, relocation.DestinationPath);
    }

    [Fact]
    public async Task RestoreAsync_FolderTheMessageBelongsInIsBoundToNothing_WritesNoRecordsAndCountsWhy()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingStateWriteOf(StillOnTheSource(Inbox, Unbound));

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.StateWrittenCount);
        Assert.Equal(1, report.Failures[MailboxRestoreFailure.FolderUnresolved]);
        Assert.Equal(0, context.Mutations.OpenedRecordCount);
    }

    [Fact]
    public async Task RestoreAsync_FolderTheMessageGoesBackIntoIsBoundToNothing_AppendsNothingAndCountsWhy()
    {
        // Arrange
        var context = new RestoreContext(Restoring).AwaitingAppendOf(Drained(Unbound));

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.AppendedCount);
        Assert.Equal(1, report.Failures[MailboxRestoreFailure.FolderUnresolved]);
        Assert.Empty(context.Store.IssuedAppends);
    }

    /// <summary>Nothing went out, so nothing is unknown: the message keeps no record and the next pass takes it again.</summary>
    [Fact]
    public async Task RestoreAsync_ContentStoreServesNoPayload_WritesNoRecordAndLeavesNothingUnanswered()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingAppendOf(Drained(Inbox.Alias))
            .WithNoStoredPayload();

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.AppendedCount);
        Assert.Equal(0, report.UnansweredAppendCount);
        Assert.Equal(1, report.Failures[MailboxRestoreFailure.ContentUnreadable]);
        Assert.Empty(context.Store.IssuedAppends);
    }

    [Theory]
    [InlineData(nameof(MailboxCredentialRefusedException), MailboxRestoreFailure.SourceRefusedTheCredential)]
    [InlineData(nameof(MailboxDestinationFolderMissingException), MailboxRestoreFailure.FolderMissing)]
    public async Task RestoreAsync_SourceRefusedTheWholeFolder_CountsEveryMessageOfItUnderThatFailure(
        string failure,
        MailboxRestoreFailure expected)
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingAppendOf(Drained(Inbox.Alias), Drained(Inbox.Alias))
            .WithSessionThatCannotBeOpened(failure);

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.AppendedCount);
        Assert.Equal(2, report.Failures[expected]);
    }

    /// <summary>One folder's failure is that folder's, because the other folder's messages were never issued against it.</summary>
    [Fact]
    public async Task RestoreAsync_MessagesOfTwoFolders_OpensOneSessionPerFolder()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingAppendOf(Drained(Inbox.Alias), Drained(Archive.Alias), Drained(Inbox.Alias));

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(3, report.AppendedCount);
        Assert.Equal([Inbox, Archive], context.OpenedFolders);
    }

    [Fact]
    public async Task RestoreAsync_MoreMailThanOneRunPutsBack_StopsAtTheConfiguredBound()
    {
        // Arrange
        var context = new RestoreContext(Restoring, maxPerRun: 2)
            .AwaitingAppendOf(Drained(Inbox.Alias), Drained(Inbox.Alias), Drained(Inbox.Alias));

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, report.AppendedCount);
        Assert.False(report.EndedTheRestore);
    }

    /// <summary>The state half costs no round trip at all, so it is what the bound is spent on first.</summary>
    [Fact]
    public async Task RestoreAsync_TheBoundIsSpentOnTheStateHalf_LeavesTheAppendsToTheNextPass()
    {
        // Arrange
        var context = new RestoreContext(Restoring, maxPerRun: 1)
            .AwaitingStateWriteOf(StillOnTheSource(Inbox, Inbox.Alias))
            .AwaitingAppendOf(Drained(Inbox.Alias));

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.StateWrittenCount);
        Assert.Equal(0, report.AppendedCount);
        Assert.Empty(context.Store.IssuedAppends);
    }

    /// <summary>
    /// A folder playing a virtual role shows the same message as the folder that really holds it, so appending into one
    /// would put a second copy into the other. ADR 0034 refuses the account rather than the folder.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_AccountSynchronizesAFolderPlayingAVirtualRole_PausesTheWholeRestore()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .SynchronizingAVirtualFolder()
            .AwaitingAppendOf(Drained(Inbox.Alias));

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxRestorePause.SynchronizedVirtualFolder, report.Pause);
        Assert.Empty(context.Store.IssuedAppends);
        Assert.Equal(MailAccountCustodyPhase.Restoring, context.Custody.StateOf(Account)!.Phase);
    }

    /// <summary>There is no folder on the source its mail could go back into, and no source path may be derived from a local name.</summary>
    [Fact]
    public async Task RestoreAsync_LocalFolderHoldsMailAndMapsOntoNoSourceFolder_PausesTheWholeRestore()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .WithLocalFolderHoldingMailAndNoMapping()
            .AwaitingAppendOf(Drained(Inbox.Alias));

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(MailboxRestorePause.LocalFolderWithoutMapping, report.Pause);
        Assert.Empty(context.Store.IssuedAppends);
    }

    [Fact]
    public async Task ReadStandingAsync_MailIsAwaitingBothHalves_ReportsEachOfThemSeparately()
    {
        // Arrange
        var drained = Drained(Inbox.Alias);
        var context = new RestoreContext(Restoring)
            .AwaitingAppendOf(drained, Drained(Inbox.Alias))
            .AwaitingStateWriteOf(StillOnTheSource(Inbox, Inbox.Alias))
            .WithAppendStandingFor(drained);

        // Act
        var standing = await context.Pass.ReadStandingAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, standing.AwaitingAppend);
        Assert.Equal(1, standing.AwaitingStateWrite);
        Assert.Equal(1, standing.UnansweredAppends);
        Assert.True(standing.IsOutstanding);
    }

    /// <summary>
    /// The state walk keeps one forward-only position per account, so a candidate it steps over is one nothing ever
    /// comes back for — the account would leave the phase with that message's state never written to its source.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_FolderAStateCandidateGoesIntoIsBoundToNothing_LeavesTheWalkStandingAtIt()
    {
        // Arrange
        var unresolved = StillOnTheSource(Inbox, Unbound);
        var behind = StillOnTheSource(Inbox, Inbox.Alias);
        var context = new RestoreContext(Restoring).AwaitingStateWriteOf(unresolved, behind);

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(context.Store.StateWritten);
        Assert.Equal(0, context.Store.StatePositionOf(Account));
        Assert.Equal(1, report.Failures[MailboxRestoreFailure.FolderUnresolved]);
        Assert.False(report.EndedTheRestore);
    }

    /// <summary>
    /// A keyword a server reported can be one no authored change may name, and raising over it would leave every
    /// later message of the account unreachable behind a walk that can never take its next step.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_MessageCarriesAKeywordNoChangeMayName_WritesItsOtherStateAndOpensNoKeywordRecord()
    {
        // Arrange
        var candidate = StillOnTheSource(Inbox, Inbox.Alias, seen: true, keywords: ["\\Answered"]);
        var context = new RestoreContext(Restoring).AwaitingStateWriteOf(candidate);

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.StateWrittenCount);
        Assert.Equal(1, report.Failures[MailboxRestoreFailure.KeywordsUnwritable]);
        Assert.Contains(candidate.Email, context.Store.StateWritten);
        Assert.DoesNotContain(
            context.Mutations.OpenedRequests,
            request => request.Mutation == MailboxMutation.SetKeywords);
        Assert.Contains(context.Mutations.OpenedRequests, request => request.Mutation == MailboxMutation.SetSeen);
    }

    /// <summary>
    /// A payload that cannot be served has no bytes to append and no later pass can produce any, so counting it as
    /// outstanding for ever would hold the account in the phase with nothing an operator could settle.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_StoredPayloadCannotBeServed_RecordsTheMessageAsOneTheRestoreCannotPutBack()
    {
        // Arrange
        var candidate = Drained(Inbox.Alias);
        var context = new RestoreContext(Restoring).AwaitingAppendOf(candidate).WithNoStoredPayload();

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.Failures[MailboxRestoreFailure.ContentUnreadable]);
        Assert.Equal([candidate.Email], context.Store.Unrestorable);
        Assert.Equal(0, report.UnansweredAppendCount);
        Assert.True(report.EndedTheRestore);
    }

    /// <summary>
    /// The answer to an append and the occurrence it justifies cannot commit together, so a pass that ended between
    /// the two has to be finished rather than turned into an append an operator goes and looks for.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_AnEarlierPassRecordedAPlacementItNeverCarried_WritesTheOccurrenceWithoutAppendingAgain()
    {
        // Arrange
        var candidate = Drained(Inbox.Alias);
        var context = new RestoreContext(Restoring).WithPlacementRecordedFor(candidate);

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.AppendedCount);
        Assert.Empty(context.AppendedStates);
        Assert.Equal(
            EmailOccurrenceId.Create(Account, Inbox.Id, ImapUidValidity.Create(9), ImapUid.Create(91)),
            Assert.Single(context.Store.ConfirmedOccurrences));
        Assert.True(report.EndedTheRestore);
    }

    /// <summary>
    /// The alias binds a different remote folder than the one the command went out against, and two unrelated folders
    /// may advertise the same UIDVALIDITY — so an occurrence built from the binding that holds now would name somebody
    /// else's mail. The placement is released instead, which is what puts the record in front of an operator.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_TheAliasWasRepointedSinceTheAppend_ReleasesThePlacementForAnOperator()
    {
        // Arrange
        var candidate = Drained(Inbox.Alias);
        var context = new RestoreContext(Restoring)
            .WithPlacementRecordedFor(candidate, Inbox.Generation.Next());

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, report.AppendedCount);
        Assert.Empty(context.Store.ConfirmedOccurrences);
        Assert.Equal(1, report.UnansweredAppendCount);
        Assert.False(report.EndedTheRestore);

        // Released rather than merely left: a record still carrying a placement is read as work the next pass
        // finishes and is offered to nobody, so it would hold the phase open with nothing anybody could settle.
        Assert.Single(await context.Pass.ReadUnansweredAppendsAsync(Account, TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// A record the server refused, or one whose attempts ran out, is a call for an operator's attention rather than
    /// work anything will do — and none of them can be withdrawn, so treating one as outstanding would hold the
    /// account in its phase with no act left that clears it.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_EveryMutationRecordIsDeadLettered_StillTakesTheAccountToMirroring()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingStateWriteOf(StillOnTheSource(Inbox, Archive.Alias));

        await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        foreach (var request in context.Mutations.OpenedRequests)
        {
            context.Mutations.Arrange(
                request,
                record => record with { Stage = MailboxMutationStage.Abandoned });
        }

        // Act
        var report = await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(report.EndedTheRestore);
        Assert.Equal(MailAccountCustodyPhase.Mirrored, context.Custody.StateOf(Account)!.Phase);
    }

    /// <summary>
    /// <c>\Answered</c> and <c>\Draft</c> are observations MailFathom records per message, so a copy put back
    /// without them asserts less about the message than the source stated before the drain took it off.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_MessageWasAnsweredAndHeldAsADraft_PutsBothFlagsBackOnTheCopy()
    {
        // Arrange
        var context = new RestoreContext(Restoring)
            .AwaitingAppendOf(Drained(Inbox.Alias, seen: true, answered: true, draft: true));

        // Act
        await context.Pass.RestoreAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var appended = Assert.Single(context.AppendedStates);
        Assert.True(appended.IsSeen);
        Assert.True(appended.IsAnswered);
        Assert.True(appended.IsDraft);
        Assert.False(appended.IsFlagged);
    }

    private static MailAccountCustodyState Held { get; } =
        new(MailAccountCustody.HoldMailbox, MailAccountCustodyPhase.Held);

    private static MailAccountCustodyState Restoring { get; } =
        new(MailAccountCustody.MirrorSource, MailAccountCustodyPhase.Restoring);

    private static MailAccountCustodyState RestoringAt(int generation) =>
        Restoring with { RestoreGeneration = generation };

    private static MailboxRestoreCandidate Drained(
        MailFolderAlias folder,
        bool seen = false,
        bool answered = false,
        bool flagged = false,
        bool draft = false,
        IEnumerable<string>? keywords = null) => new(
        StoredEmailId.Create(Guid.CreateVersion7()),
        folder,
        new RestoredEmailState(seen, answered, flagged, draft, RemoteEmailKeywords.Create(keywords)),
        ArrivedAt);

    private static MailboxRestoredStateCandidate StillOnTheSource(
        MailFolderResolution folder,
        MailFolderAlias destination,
        bool seen = false,
        bool flagged = false,
        IEnumerable<string>? keywords = null) => new(
        StoredEmailId.Create(Guid.CreateVersion7()),
        EmailOccurrenceId.Create(Account, folder.Id, ImapUidValidity.Create(1), ImapUid.Create(11)),
        folder,
        destination,
        new RestoredEmailState(seen, IsAnswered: false, flagged, IsDraft: false, RemoteEmailKeywords.Create(keywords)));

    private sealed class RestoreContext
    {
        private static readonly byte[] Payload = Encoding.ASCII.GetBytes("Subject: stored\r\n\r\nbody\r\n");

        private readonly IMailFolderMappingReader mappings = Substitute.For<IMailFolderMappingReader>();
        private readonly IMailFolderResolutionStore resolutions = Substitute.For<IMailFolderResolutionStore>();
        private readonly InMemoryMailboxDrainStore drain = new();

        private uint nextUid = 41;

        internal RestoreContext(
            MailAccountCustodyState custody,
            int maxPerRun = 100,
            InMemoryMailboxMutationRecordStore? mutations = null)
        {
            this.Custody = InMemoryMailAccountCustodyStore.With(Account, custody);

            // Handed in where a test restores one account twice: everything else a pass touches is rebuilt between
            // two restores of a real deployment — the process, the session, the walk — and the mutation records are
            // the one thing that outlives both, because a completed record is never deleted.
            this.Mutations = mutations ?? new InMemoryMailboxMutationRecordStore();

            var persistenceSession = Substitute.For<IPersistenceSession>();
            persistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
            var sessionFactory = Substitute.For<IPersistenceSessionFactory>();
            sessionFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(persistenceSession);

            this.mappings.FoldersOf(Account).Returns([
                MailFolderMapping.ToRemotePath(Inbox.Alias, Inbox.RemotePath),
                MailFolderMapping.ToRemotePath(Archive.Alias, Archive.RemotePath),
            ]);
            this.resolutions.GetCurrentResolutionAsync(Account, Inbox.Alias, Arg.Any<CancellationToken>())
                .Returns(Inbox);
            this.resolutions.GetCurrentResolutionAsync(Account, Archive.Alias, Arg.Any<CancellationToken>())
                .Returns(Archive);
            this.resolutions.GetCurrentResolutionAsync(Account, Unbound, Arg.Any<CancellationToken>())
                .Returns((MailFolderResolution?)null);

            this.Content = Substitute.For<IEmailContentStore>();
            this.Content.FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
                .Returns(new StoredEmailContent(Payload, Payload.Length, SHA256.HashData(Payload)));

            this.WriteSession = Substitute.For<IMailboxWriteSession>();
            this.WriteSession.AppendRestoredAsync(
                    Arg.Any<ReadOnlyMemory<byte>>(),
                    Arg.Any<RestoredEmailState>(),
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    this.AppendedStates.Add(call.ArgAt<RestoredEmailState>(1));
                    this.AppendedInternalDates.Add(call.ArgAt<DateTimeOffset>(2));

                    return RemoteEmailPlacement.Reported(
                        ImapUidValidity.Create(7),
                        ImapUid.Create(this.nextUid++));
                });

            this.WriteSessionFactory = Substitute.For<IMailboxWriteSessionFactory>();
            this.WriteSessionFactory.OpenForWritingAsync(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<MailFolderResolution>(),
                    Arg.Any<MailTransportSecurityPolicy>(),
                    Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    this.OpenedFolders.Add(call.ArgAt<MailFolderResolution>(1));

                    return this.WriteSession;
                });

            var transportSecurity = Substitute.For<IMailTransportSecurityPolicyReader>();
            transportSecurity.GetPolicy(Account).Returns(TransportPolicy);

            var clock = new FakeTimeProvider(RunInstant);

            this.Pass = new MailboxRestorePass(
                this.Custody,
                this.Store,
                this.drain,
                this.Mutations,
                this.Content,
                this.WriteSessionFactory,
                this.resolutions,
                transportSecurity,
                this.mappings,
                new OptimisticConcurrencyRetryPolicy(
                    sessionFactory,
                    new PersistenceConcurrencyOptions { MaximumCommitAttempts = 1 },
                    clock),
                new MailboxSynchronizationOptions { MaxRestoredEmailsPerRun = maxPerRun },
                clock);
        }

        internal InMemoryMailAccountCustodyStore Custody { get; }

        internal InMemoryMailboxRestoreStore Store { get; } = new();

        internal InMemoryMailboxMutationRecordStore Mutations { get; }

        internal IEmailContentStore Content { get; }

        internal IMailboxWriteSession WriteSession { get; }

        internal IMailboxWriteSessionFactory WriteSessionFactory { get; }

        internal MailboxRestorePass Pass { get; }

        internal List<MailFolderResolution> OpenedFolders { get; } = [];

        internal List<RestoredEmailState> AppendedStates { get; } = [];

        internal List<DateTimeOffset> AppendedInternalDates { get; } = [];

        internal RestoreContext AwaitingAppendOf(params MailboxRestoreCandidate[] candidates)
        {
            this.Store.AwaitingAppendOf(Account, candidates);

            return this;
        }

        internal RestoreContext AwaitingStateWriteOf(params MailboxRestoredStateCandidate[] candidates)
        {
            this.Store.AwaitingStateWriteOf(Account, candidates);

            return this;
        }

        internal RestoreContext WithAppendStandingFor(MailboxRestoreCandidate candidate)
        {
            this.Store.WithAppendStandingFor(Account, new MailboxRestoreAppend(
                MailboxRestoreAppendId.New(),
                candidate.Email,
                candidate.SourceFolderAlias,
                MailFolderResolutionGeneration.First,
                RunInstant));

            return this;
        }

        internal RestoreContext AwaitingSourceRemoval()
        {
            this.drain.AwaitingRemovalOf(new MailboxSourceRemoval(
                MailboxSourceRemovalId.New(),
                EmailOccurrenceId.Create(Account, Inbox.Id, ImapUidValidity.Create(1), ImapUid.Create(31)),
                Inbox));

            return this;
        }

        /// <summary>Leaves behind what a pass that ended between a fully answered append and its occurrence leaves.</summary>
        /// <param name="candidate">The message the interrupted pass appended.</param>
        /// <param name="generation">Which binding of the alias that append went out against; the current one by default.</param>
        internal RestoreContext WithPlacementRecordedFor(
            MailboxRestoreCandidate candidate,
            MailFolderResolutionGeneration? generation = null)
        {
            this.Store.WithPlacementRecordedFor(
                Account,
                new MailboxRestoreAppend(
                    MailboxRestoreAppendId.New(),
                    candidate.Email,
                    candidate.SourceFolderAlias,
                    generation ?? MailFolderResolutionGeneration.First,
                    RunInstant),
                ImapUidValidity.Create(9),
                ImapUid.Create(91));

            return this;
        }

        internal RestoreContext WithTheOccurrenceAlreadyHeld()
        {
            this.Store.OccurrenceIsAlreadyHeld = true;

            return this;
        }

        internal RestoreContext WithNoStoredPayload()
        {
            this.Content.FindStoredContentAsync(Arg.Any<StoredEmailId>(), Arg.Any<CancellationToken>())
                .Returns((StoredEmailContent?)null);

            return this;
        }

        internal RestoreContext WithUnreachableSource()
        {
            this.WriteSession.AppendRestoredAsync(
                    Arg.Any<ReadOnlyMemory<byte>>(),
                    Arg.Any<RestoredEmailState>(),
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<CancellationToken>())
                .ThrowsAsync(new MailboxUnavailableException(
                    Account,
                    Inbox.Alias,
                    new TimeoutException("The source did not answer.")));

            return this;
        }

        internal RestoreContext WithSourceThatNamesNoPlacement()
        {
            this.WriteSession.AppendRestoredAsync(
                    Arg.Any<ReadOnlyMemory<byte>>(),
                    Arg.Any<RestoredEmailState>(),
                    Arg.Any<DateTimeOffset>(),
                    Arg.Any<CancellationToken>())
                .Returns(RemoteEmailPlacement.NotReported());

            return this;
        }

        internal RestoreContext WithSessionThatCannotBeOpened(string failure)
        {
            Exception thrown = failure == nameof(MailboxCredentialRefusedException)
                ? new MailboxCredentialRefusedException(
                    Account,
                    new InvalidOperationException("The server refused the credential."))
                : new MailboxDestinationFolderMissingException(
                    Account,
                    Inbox.Alias,
                    MailboxMutation.Relocate,
                    new InvalidOperationException("The folder is gone."));

            this.WriteSessionFactory.OpenForWritingAsync(
                    Arg.Any<MailAccountId>(),
                    Arg.Any<MailFolderResolution>(),
                    Arg.Any<MailTransportSecurityPolicy>(),
                    Arg.Any<CancellationToken>())
                .ThrowsAsync(thrown);

            return this;
        }

        internal RestoreContext SynchronizingAVirtualFolder()
        {
            this.mappings.FoldersOf(Account).Returns([
                MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("all"), MailFolderSpecialUse.All),
            ]);

            return this;
        }

        internal RestoreContext WithLocalFolderHoldingMailAndNoMapping()
        {
            this.Store.UnmappedFoldersHoldingMail = 1;

            return this;
        }
    }
}
