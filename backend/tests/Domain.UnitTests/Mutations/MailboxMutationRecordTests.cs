// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Domain.UnitTests.Mutations;

/// <summary>Covers the join a synchronization run makes between what it discovered and what MailFathom recorded.</summary>
public sealed class MailboxMutationRecordTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly RemoteFolderPath Inbox = RemoteFolderPath.Create("INBOX");

    private static readonly RemoteFolderPath Archive = RemoteFolderPath.Create("Archive", '/');

    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7(RecordedAt));

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Rule("file-newsletters", "3");

    private static readonly ImapUidValidity DestinationUidValidity = ImapUidValidity.Create(9001);

    private static readonly ImapUid PlacedUid = ImapUid.Create(77);

    /// <summary>The last reading of an occurrence's flags taken before the store this class builds went out.</summary>
    private static readonly DateTimeOffset ObservedBeforeTheStore = RecordedAt.AddMinutes(-1);

    /// <summary>A discovered occurrence the server itself named as the destination of a completed relocation is that relocation's own.</summary>
    [Fact]
    public void IsPlacementOf_TheOccurrenceCopyUidNamed_IsRecognized()
    {
        // Arrange
        var record = CompletedRelocation();

        // Act
        var isPlacement = record.IsPlacementOf(Archive, DestinationUidValidity, PlacedUid);

        // Assert
        Assert.True(isPlacement);
    }

    /// <summary>A server that named no placement is joined to nothing, because the only alternative is a guess.</summary>
    [Fact]
    public void IsPlacementOf_RelocationWhoseServerReportedNoPlacement_MatchesNothing()
    {
        // Arrange
        var record = CompletedRelocation() with { Placement = RemoteEmailPlacement.NotReported() };

        // Act
        var isPlacement = record.IsPlacementOf(Archive, DestinationUidValidity, PlacedUid);

        // Assert
        Assert.False(isPlacement);
    }

    /// <summary>A destination folder recreated between the placement and the discovery renumbers its UIDs, so the recorded one names nothing.</summary>
    [Fact]
    public void IsPlacementOf_DestinationFolderRenumberedSinceThePlacement_MatchesNothing()
    {
        // Arrange
        var record = CompletedRelocation();

        // Act
        var isPlacement = record.IsPlacementOf(Archive, ImapUidValidity.Create(9002), PlacedUid);

        // Assert
        Assert.False(isPlacement);
    }

    /// <summary>The same UID in another folder is another message, so the destination path is part of the join.</summary>
    [Fact]
    public void IsPlacementOf_TheRecordedUidInAnotherFolder_MatchesNothing()
    {
        // Arrange
        var record = CompletedRelocation();

        // Act
        var isPlacement = record.IsPlacementOf(RemoteFolderPath.Create("Later", '/'), DestinationUidValidity, PlacedUid);

        // Assert
        Assert.False(isPlacement);
    }

    /// <summary>A sequence that still owes a command has the email in both folders, so nothing is carried across yet.</summary>
    [Theory]
    [InlineData(MailboxMutationStage.PlacementIssued)]
    [InlineData(MailboxMutationStage.PlacementConfirmed)]
    [InlineData(MailboxMutationStage.SourceFlaggedDeleted)]
    [InlineData(MailboxMutationStage.Abandoned)]
    public void IsPlacementOf_RelocationThatHasNotCompleted_MatchesNothing(MailboxMutationStage stage)
    {
        // Arrange
        var record = CompletedRelocation() with { Stage = stage };

        // Act
        var isPlacement = record.IsPlacementOf(Archive, DestinationUidValidity, PlacedUid);

        // Assert
        Assert.False(isPlacement);
    }

    /// <summary>A placement already recognized is not offered a second local email to carry.</summary>
    [Fact]
    public void IsPlacementOf_PlacementAlreadyObserved_MatchesNothing()
    {
        // Arrange
        var record = CompletedRelocation() with { PlacementObservedAt = RecordedAt.AddMinutes(1) };

        // Act
        var isPlacement = record.IsPlacementOf(Archive, DestinationUidValidity, PlacedUid);

        // Assert
        Assert.False(isPlacement);
    }

    /// <summary>A copy places an email too, and whether that is one local email or two is not this join's decision.</summary>
    [Fact]
    public void IsPlacementOf_ACopyIntoTheSameFolder_MatchesNothing()
    {
        // Arrange
        var record = CompletedRelocation() with
        {
            Request = MailboxMutationRequest.Copy(LocalEmail, SyntheticMailOwner.Deployment, SourceOccurrence(), Requester, Archive),
        };

        // Act
        var isPlacement = record.IsPlacementOf(Archive, DestinationUidValidity, PlacedUid);

        // Assert
        Assert.False(isPlacement);
    }

    /// <summary>Whose act an arrival was is the same question for a copy as for a relocation, whatever the two then do with the local row.</summary>
    [Fact]
    public void AccountsForPlacementAt_TheOccurrenceACopyPlaced_IsRecognized()
    {
        // Arrange
        var record = CompletedRelocation() with
        {
            Request = MailboxMutationRequest.Copy(LocalEmail, SyntheticMailOwner.Deployment, SourceOccurrence(), Requester, Archive),
        };

        // Act
        var accountsForPlacement = record.AccountsForPlacementAt(Archive, DestinationUidValidity, PlacedUid);

        // Assert
        Assert.True(accountsForPlacement);
    }

    /// <summary>A delete and a flag change put the email nowhere, so no discovery is ever theirs.</summary>
    [Fact]
    public void AccountsForPlacementAt_AMutationThatPlacesNothing_MatchesNothing()
    {
        // Arrange
        var delete = CompletedRelocation() with
        {
            Request = MailboxMutationRequest.Delete(
                LocalEmail, SyntheticMailOwner.Deployment,
                SourceOccurrence(),
                Requester,
                AuthoredDeleteEmailDisposition.RetainLocalCopy),
        };
        var setSeen = CompletedRelocation() with
        {
            Request = MailboxMutationRequest.SetSeen(LocalEmail, SyntheticMailOwner.Deployment, SourceOccurrence(), Requester, isSeen: true),
        };

        // Act
        var deleteAccountsForPlacement = delete.AccountsForPlacementAt(Archive, DestinationUidValidity, PlacedUid);
        var setSeenAccountsForPlacement = setSeen.AccountsForPlacementAt(Archive, DestinationUidValidity, PlacedUid);

        // Assert
        Assert.False(deleteAccountsForPlacement);
        Assert.False(setSeenAccountsForPlacement);
    }

    /// <summary>The flag standing where a completed store put it is that store completing, not the owner reading the message.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AccountsForSeenStateOf_TheDirectionItAskedFor_IsRecognized(bool isSeen)
    {
        // Arrange
        var record = CompletedSeenStateChange(isSeen);

        // Act
        var accountsForSeenState = record.AccountsForSeenStateOf(SourceOccurrence(), isSeen, ObservedBeforeTheStore);

        // Assert
        Assert.True(accountsForSeenState);
    }

    /// <summary>A store that asked for one direction says nothing about the flag moving the other way.</summary>
    [Fact]
    public void AccountsForSeenStateOf_TheOppositeDirection_MatchesNothing()
    {
        // Arrange
        var record = CompletedSeenStateChange(isSeen: true);

        // Act
        var accountsForSeenState = record.AccountsForSeenStateOf(
            SourceOccurrence(),
            observedSeenState: false,
            ObservedBeforeTheStore);

        // Assert
        Assert.False(accountsForSeenState);
    }

    /// <summary>An occurrence read since the store is a mailbox somebody else has had the chance to change, so the record stops answering for it.</summary>
    /// <remarks>
    /// This is what scopes the suppression to the one change the record describes, and the case it exists for is the
    /// quiet one: an owner who reverts the flag before the first reading leaves nothing for the record to be marked
    /// spent by, and would otherwise have their own later change silenced by it months afterwards.
    /// </remarks>
    [Fact]
    public void AccountsForSeenStateOf_AnOccurrenceObservedSinceTheStore_MatchesNothing()
    {
        // Arrange
        var record = CompletedSeenStateChange(isSeen: true);

        // Act
        var accountsForSeenState = record.AccountsForSeenStateOf(
            SourceOccurrence(),
            observedSeenState: true,
            RecordedAt.AddMinutes(1));

        // Assert
        Assert.False(accountsForSeenState);
    }

    /// <summary>No STORE has gone out, so the flag standing there was put there by somebody else.</summary>
    [Fact]
    public void AccountsForSeenStateOf_AStoreNothingHasBeenIssuedFor_MatchesNothing()
    {
        // Arrange
        var record = CompletedSeenStateChange(isSeen: true) with { Stage = MailboxMutationStage.Recorded };

        // Act
        var accountsForSeenState = record.AccountsForSeenStateOf(
            SourceOccurrence(),
            observedSeenState: true,
            ObservedBeforeTheStore);

        // Assert
        Assert.False(accountsForSeenState);
    }

    /// <summary>A relocation and a delete write no flag, so neither answers for one that moved.</summary>
    [Fact]
    public void AccountsForSeenStateOf_AMutationThatWritesNoFlag_MatchesNothing()
    {
        // Arrange
        var record = CompletedRelocation();

        // Act
        var accountsForSeenState = record.AccountsForSeenStateOf(
            SourceOccurrence(),
            observedSeenState: true,
            ObservedBeforeTheStore);

        // Assert
        Assert.False(accountsForSeenState);
    }

    /// <summary>The source occurrence is written down before any command goes out, so the disappearance needs no COPYUID to be attributed.</summary>
    [Theory]
    [InlineData(MailboxMutationStage.PlacementIssued)]
    [InlineData(MailboxMutationStage.SourceFlaggedDeleted)]
    [InlineData(MailboxMutationStage.Completed)]
    [InlineData(MailboxMutationStage.Abandoned)]
    public void AccountsForRemovalOf_ItsOwnSourceOnceACommandHasGoneOut_IsRecognized(MailboxMutationStage stage)
    {
        // Arrange
        var record = CompletedRelocation() with { Stage = stage };

        // Act
        var accountsForRemoval = record.AccountsForRemovalOf(SourceOccurrence());

        // Assert
        Assert.True(accountsForRemoval);
    }

    /// <summary>Nothing has reached the server yet, so an occurrence vanishing is somebody else's act.</summary>
    [Fact]
    public void AccountsForRemovalOf_AMutationNothingHasBeenIssuedFor_MatchesNothing()
    {
        // Arrange
        var record = CompletedRelocation() with { Stage = MailboxMutationStage.Recorded };

        // Act
        var accountsForRemoval = record.AccountsForRemovalOf(SourceOccurrence());

        // Assert
        Assert.False(accountsForRemoval);
    }

    /// <summary>A folder renumbered under a new UIDVALIDITY names different messages, so the recorded occurrence matches none of them.</summary>
    [Fact]
    public void AccountsForRemovalOf_TheSameUidUnderAnotherUidValidity_MatchesNothing()
    {
        // Arrange
        var record = CompletedRelocation();
        var renumbered = EmailOccurrenceId.Create(
            MailAccountId.Create("work"),
            InboxBinding,
            ImapUidValidity.Create(2),
            ImapUid.Create(41));

        // Act
        var accountsForRemoval = record.AccountsForRemovalOf(renumbered);

        // Assert
        Assert.False(accountsForRemoval);
    }

    /// <summary>A flag change moves no occurrence, so a disappearance beside one is not it completing.</summary>
    [Fact]
    public void AccountsForRemovalOf_ASeenFlagChange_MatchesNothing()
    {
        // Arrange
        var record = CompletedRelocation() with
        {
            Request = MailboxMutationRequest.SetSeen(LocalEmail, SyntheticMailOwner.Deployment, SourceOccurrence(), Requester, isSeen: true),
        };

        // Act
        var accountsForRemoval = record.AccountsForRemovalOf(SourceOccurrence());

        // Assert
        Assert.False(accountsForRemoval);
    }

    /// <summary>The record stays the reason that occurrence is gone, so a window asking twice is answered twice.</summary>
    [Fact]
    public void AccountsForRemovalOf_ARemovalAlreadyObserved_IsStillRecognized()
    {
        // Arrange
        var record = CompletedRelocation() with { SourceRemovalObservedAt = RecordedAt.AddMinutes(1) };

        // Act
        var accountsForRemoval = record.AccountsForRemovalOf(SourceOccurrence());

        // Assert
        Assert.True(accountsForRemoval);
    }

    /// <summary>A relocation owes both halves, so seeing one of them is not the join being done.</summary>
    [Fact]
    public void IsReconciled_RelocationWithOnlyOneHalfObserved_IsNotYetSettled()
    {
        // Arrange
        var record = CompletedRelocation();

        // Act
        var withSourceRemovalOnly = record with { SourceRemovalObservedAt = RecordedAt.AddMinutes(1) };
        var withBothHalves = withSourceRemovalOnly with { PlacementObservedAt = RecordedAt.AddMinutes(2) };

        // Assert
        Assert.False(record.IsReconciled);
        Assert.False(withSourceRemovalOnly.IsReconciled);
        Assert.True(withBothHalves.IsReconciled);
    }

    /// <summary>A delete puts the email nowhere and a flag change moves nothing, so neither waits for a placement that will never be discovered.</summary>
    [Fact]
    public void IsReconciled_MutationsThatPlaceNothing_WaitForNoPlacement()
    {
        // Arrange
        var delete = CompletedRelocation() with
        {
            Request = MailboxMutationRequest.Delete(
                LocalEmail, SyntheticMailOwner.Deployment,
                SourceOccurrence(),
                Requester,
                AuthoredDeleteEmailDisposition.RetainLocalCopy),
            Placement = RemoteEmailPlacement.NotReported(),
        };
        var setSeen = delete with
        {
            Request = MailboxMutationRequest.SetSeen(LocalEmail, SyntheticMailOwner.Deployment, SourceOccurrence(), Requester, isSeen: true),
        };

        // Act
        var deleteObserved = delete with { SourceRemovalObservedAt = RecordedAt.AddMinutes(1) };

        // Assert
        Assert.False(delete.IsReconciled);
        Assert.True(deleteObserved.IsReconciled);

        // A flag change moves no occurrence, so there is nothing for synchronization to come back and meet. Its
        // provenance is settled against the occurrence's own observation rather than against anything on this row.
        Assert.True(setSeen.IsReconciled);
    }

    /// <summary>The lifecycle is what somebody watching a deployment reads, and the three converging stages are one answer to them.</summary>
    [Theory]
    [InlineData(MailboxMutationStage.Recorded)]
    [InlineData(MailboxMutationStage.PlacementIssued)]
    [InlineData(MailboxMutationStage.PlacementConfirmed)]
    [InlineData(MailboxMutationStage.SourceFlaggedDeleted)]
    [InlineData(MailboxMutationStage.Completed)]
    [InlineData(MailboxMutationStage.Abandoned)]
    public void Lifecycle_AnyStage_ReadsTheStageTheRecordCarries(MailboxMutationStage stage)
    {
        // Arrange
        var record = CompletedRelocation() with { Stage = stage };

        // Act
        var lifecycle = record.Lifecycle;

        // Assert
        Assert.Equal(MailboxMutationLifecycle.Of(stage), lifecycle);
    }

    /// <summary>The one stage a retry may not act on is the one convergence has to recognize before it does anything.</summary>
    [Theory]
    [InlineData(MailboxMutationStage.Recorded, false)]
    [InlineData(MailboxMutationStage.PlacementIssued, true)]
    [InlineData(MailboxMutationStage.PlacementConfirmed, false)]
    [InlineData(MailboxMutationStage.SourceFlaggedDeleted, false)]
    [InlineData(MailboxMutationStage.Completed, false)]
    [InlineData(MailboxMutationStage.Abandoned, false)]
    public void HasUnknownOutcome_AnyStage_IsTrueOnlyWhileThePlacementIsUnacknowledged(
        MailboxMutationStage stage,
        bool expected)
    {
        // Arrange
        var record = CompletedRelocation() with { Stage = stage };

        // Act
        var hasUnknownOutcome = record.HasUnknownOutcome;

        // Assert
        Assert.Equal(expected, hasUnknownOutcome);
    }

    /// <summary>
    /// A relocation carried by <c>MOVE</c> removes the source itself, so the source having gone is the server's own
    /// statement that the command ran — which is what settles an outcome nothing may ask the server about twice.
    /// </summary>
    [Fact]
    public void IsUnknownPlacementSettledBySourceRemoval_ANativeRelocationWhoseSourceHasGone_IsSettled()
    {
        // Arrange
        var unacknowledged = UnacknowledgedNativeRelocation();

        // Act
        var settled = unacknowledged with { SourceRemovalObservedAt = RecordedAt.AddMinutes(5) };

        // Assert
        Assert.False(unacknowledged.IsUnknownPlacementSettledBySourceRemoval);
        Assert.True(settled.IsUnknownPlacementSettledBySourceRemoval);
    }

    /// <summary>
    /// A copy and a fallback relocation both leave the source where it was, so nothing about it distinguishes a command
    /// that landed from one that never arrived. Settling either from a disappearance would be a guess.
    /// </summary>
    [Fact]
    public void IsUnknownPlacementSettledBySourceRemoval_ASequenceThatLeavesTheSource_IsNeverSettledByIt()
    {
        // Arrange
        var observedAt = RecordedAt.AddMinutes(5);
        var fallbackRelocation = UnacknowledgedNativeRelocation() with
        {
            RequiresSourceRemoval = true,
            SourceRemovalObservedAt = observedAt,
        };
        var copy = UnacknowledgedNativeRelocation() with
        {
            Request = MailboxMutationRequest.Copy(LocalEmail, SyntheticMailOwner.Deployment, SourceOccurrence(), Requester, Archive),
            SourceRemovalObservedAt = observedAt,
        };

        // Act
        var settledStates = new[]
        {
            fallbackRelocation.IsUnknownPlacementSettledBySourceRemoval,
            copy.IsUnknownPlacementSettledBySourceRemoval,
        };

        // Assert
        Assert.Equal([false, false], settledStates);
    }

    private static MailboxMutationRecord UnacknowledgedNativeRelocation() => CompletedRelocation() with
    {
        Stage = MailboxMutationStage.PlacementIssued,
        RequiresSourceRemoval = false,
        Placement = RemoteEmailPlacement.NotReported(),
    };

    private static MailFolderResolutionId InboxBinding =>
        new(MailFolderAlias.Create(Inbox.Value), MailFolderResolutionGeneration.First);

    private static EmailOccurrenceId SourceOccurrence() => EmailOccurrenceId.Create(
        MailAccountId.Create("work"),
        InboxBinding,
        ImapUidValidity.Create(1),
        ImapUid.Create(41));

    private static MailboxMutationRecord CompletedRelocation() => new()
    {
        Id = MailboxMutationRecordId.Create(Guid.CreateVersion7(RecordedAt)),
        Request = MailboxMutationRequest.Relocate(LocalEmail, SyntheticMailOwner.Deployment, SourceOccurrence(), Requester, Archive),
        Stage = MailboxMutationStage.Completed,
        IsAudited = false,
        RequiresSourceRemoval = true,
        Placement = RemoteEmailPlacement.Reported(DestinationUidValidity, PlacedUid),
        AttemptCount = 1,
        RecordedAt = RecordedAt,
        StageChangedAt = RecordedAt,
        LastFailure = null,
        PlacementObservedAt = null,
        SourceRemovalObservedAt = null,
    };

    /// <summary>The star standing where a completed store put it is that store completing, not the owner starring the message.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AccountsForFlaggedStateOf_TheDirectionItAskedFor_IsRecognized(bool isFlagged)
    {
        // Arrange
        var record = CompletedFlaggedStateChange(isFlagged);

        // Act
        var accountsForFlaggedState = record.AccountsForFlaggedStateOf(
            SourceOccurrence(),
            isFlagged,
            ObservedBeforeTheStore);

        // Assert
        Assert.True(accountsForFlaggedState);
    }

    /// <summary>A store that asked for one direction says nothing about the star moving the other way.</summary>
    [Fact]
    public void AccountsForFlaggedStateOf_TheOppositeDirection_MatchesNothing()
    {
        // Arrange
        var record = CompletedFlaggedStateChange(isFlagged: true);

        // Act
        var accountsForFlaggedState = record.AccountsForFlaggedStateOf(
            SourceOccurrence(),
            observedFlaggedState: false,
            ObservedBeforeTheStore);

        // Assert
        Assert.False(accountsForFlaggedState);
    }

    /// <summary>A reading taken after the store is a mailbox somebody else has had the chance to change, so the record stops answering for it.</summary>
    [Fact]
    public void AccountsForFlaggedStateOf_AnOccurrenceReadSinceTheStore_MatchesNothing()
    {
        // Arrange
        var record = CompletedFlaggedStateChange(isFlagged: true);

        // Act
        var accountsForFlaggedState = record.AccountsForFlaggedStateOf(
            SourceOccurrence(),
            observedFlaggedState: true,
            record.StageChangedAt.AddSeconds(1));

        // Assert
        Assert.False(accountsForFlaggedState);
    }

    /// <summary>Nothing has reached the server for a recorded change, so the star standing there is somebody else's doing.</summary>
    [Fact]
    public void AccountsForFlaggedStateOf_ARecordNoCommandWentOutFor_MatchesNothing()
    {
        // Arrange
        var record = CompletedFlaggedStateChange(isFlagged: true) with { Stage = MailboxMutationStage.Recorded };

        // Act
        var accountsForFlaggedState = record.AccountsForFlaggedStateOf(
            SourceOccurrence(),
            observedFlaggedState: true,
            ObservedBeforeTheStore);

        // Assert
        Assert.False(accountsForFlaggedState);
    }

    /// <summary>A seen-state store writes no star, and a star store writes no seen state; neither answers for the other's value.</summary>
    [Fact]
    public void AccountsForFlaggedStateOf_AMutationThatWritesAnotherValue_MatchesNothing()
    {
        // Arrange
        var seenStateChange = CompletedSeenStateChange(isSeen: true);
        var flaggedStateChange = CompletedFlaggedStateChange(isFlagged: true);

        // Act
        var seenStateAccountsForStar = seenStateChange.AccountsForFlaggedStateOf(
            SourceOccurrence(),
            observedFlaggedState: true,
            ObservedBeforeTheStore);
        var starAccountsForSeenState = flaggedStateChange.AccountsForSeenStateOf(
            SourceOccurrence(),
            observedSeenState: true,
            ObservedBeforeTheStore);

        // Assert
        Assert.False(seenStateAccountsForStar);
        Assert.False(starAccountsForSeenState);
    }

    /// <summary>An addition leaves what the message carried plus what it named, and that reading is the addition completing.</summary>
    [Fact]
    public void AccountsForKeywordsOf_AnAdditionThatExplainsTheWholeReading_IsRecognized()
    {
        // Arrange
        var record = CompletedKeywordChange(MailboxMutation.AddKeywords, "$Todo");

        // Act
        var accountsForKeywords = record.AccountsForKeywordsOf(
            SourceOccurrence(),
            RemoteEmailKeywords.Create(["$Invoice"]),
            RemoteEmailKeywords.Create(["$todo", "$Invoice"]),
            ObservedBeforeTheStore);

        // Assert
        Assert.True(accountsForKeywords);
    }

    /// <summary>An addition says nothing about a reading that does not carry what it asked for.</summary>
    [Fact]
    public void AccountsForKeywordsOf_AnAdditionWhoseKeywordIsAbsent_MatchesNothing()
    {
        // Arrange
        var record = CompletedKeywordChange(MailboxMutation.AddKeywords, "$Todo");

        // Act
        var accountsForKeywords = record.AccountsForKeywordsOf(
            SourceOccurrence(),
            RemoteEmailKeywords.Create(["$Invoice"]),
            RemoteEmailKeywords.Create(["$Invoice"]),
            ObservedBeforeTheStore);

        // Assert
        Assert.False(accountsForKeywords);
    }

    /// <summary>A label the owner attached beside MailFathom's own is a reading no addition here produced.</summary>
    [Fact]
    public void AccountsForKeywordsOf_AnAdditionBesideAKeywordSomebodyElseAttached_MatchesNothing()
    {
        // Arrange
        var record = CompletedKeywordChange(MailboxMutation.AddKeywords, "$Todo");

        // Act
        var accountsForKeywords = record.AccountsForKeywordsOf(
            SourceOccurrence(),
            RemoteEmailKeywords.None,
            RemoteEmailKeywords.Create(["$Todo", "$Invoice"]),
            ObservedBeforeTheStore);

        // Assert
        Assert.False(accountsForKeywords);
    }

    /// <summary>A removal leaves what the message carried without what it named, and that reading is the removal completing.</summary>
    [Fact]
    public void AccountsForKeywordsOf_ARemovalThatExplainsTheWholeReading_IsRecognized()
    {
        // Arrange
        var record = CompletedKeywordChange(MailboxMutation.RemoveKeywords, "$Todo");

        // Act
        var accountsForKeywords = record.AccountsForKeywordsOf(
            SourceOccurrence(),
            RemoteEmailKeywords.Create(["$TODO", "$Invoice"]),
            RemoteEmailKeywords.Create(["$Invoice"]),
            ObservedBeforeTheStore);

        // Assert
        Assert.True(accountsForKeywords);
    }

    /// <summary>A removal whose keyword is still on the message did not produce the reading being judged.</summary>
    [Fact]
    public void AccountsForKeywordsOf_ARemovalWhoseKeywordIsStillCarried_MatchesNothing()
    {
        // Arrange
        var record = CompletedKeywordChange(MailboxMutation.RemoveKeywords, "$Todo");

        // Act
        var accountsForKeywords = record.AccountsForKeywordsOf(
            SourceOccurrence(),
            RemoteEmailKeywords.Create(["$Todo", "$Invoice"]),
            RemoteEmailKeywords.Create(["$TODO"]),
            ObservedBeforeTheStore);

        // Assert
        Assert.False(accountsForKeywords);
    }

    /// <summary>A removal of a keyword the message never carried explains no reading, which is the case a weaker test would swallow.</summary>
    [Fact]
    public void AccountsForKeywordsOf_ARemovalBesideAKeywordSomebodyElseAttached_MatchesNothing()
    {
        // Arrange
        var record = CompletedKeywordChange(MailboxMutation.RemoveKeywords, "$Todo");

        // Act
        var accountsForKeywords = record.AccountsForKeywordsOf(
            SourceOccurrence(),
            RemoteEmailKeywords.None,
            RemoteEmailKeywords.Create(["$Invoice"]),
            ObservedBeforeTheStore);

        // Assert
        Assert.False(accountsForKeywords);
    }

    /// <summary>A replacement stated the whole set, so only the whole set standing as it named is that replacement completing.</summary>
    [Fact]
    public void AccountsForKeywordsOf_AReplacementMatchingTheWholeSet_IsRecognized()
    {
        // Arrange
        var record = CompletedKeywordChange(MailboxMutation.SetKeywords, "$Todo");

        // Act
        var accountsForKeywords = record.AccountsForKeywordsOf(
            SourceOccurrence(),
            RemoteEmailKeywords.Create(["$Invoice"]),
            RemoteEmailKeywords.Create(["$todo"]),
            ObservedBeforeTheStore);

        // Assert
        Assert.True(accountsForKeywords);
    }

    /// <summary>A keyword the replacement never named is somebody else's, whichever way round the difference falls.</summary>
    [Fact]
    public void AccountsForKeywordsOf_AReplacementBesideAKeywordItNeverNamed_MatchesNothing()
    {
        // Arrange
        var record = CompletedKeywordChange(MailboxMutation.SetKeywords, "$Todo");

        // Act
        var accountsForKeywords = record.AccountsForKeywordsOf(
            SourceOccurrence(),
            RemoteEmailKeywords.None,
            RemoteEmailKeywords.Create(["$Todo", "$Invoice"]),
            ObservedBeforeTheStore);

        // Assert
        Assert.False(accountsForKeywords);
    }

    /// <summary>An empty replacement asked for every keyword to go, so a message carrying none is that replacement completing.</summary>
    [Fact]
    public void AccountsForKeywordsOf_AnEmptyReplacementAgainstNoKeywords_IsRecognized()
    {
        // Arrange
        var record = CompletedRelocation() with
        {
            Request = MailboxMutationRequest.SetKeywords(
                LocalEmail, SyntheticMailOwner.Deployment,
                SourceOccurrence(),
                Requester,
                AuthoredMailKeywords.None),
            RequiresSourceRemoval = false,
            Placement = RemoteEmailPlacement.NotReported(),
        };

        // Act
        var accountsForKeywords = record.AccountsForKeywordsOf(
            SourceOccurrence(),
            RemoteEmailKeywords.Create(["$Todo"]),
            RemoteEmailKeywords.None,
            ObservedBeforeTheStore);

        // Assert
        Assert.True(accountsForKeywords);
    }

    /// <summary>A mutation that writes no keyword answers for none, whatever the message ended up carrying.</summary>
    [Fact]
    public void AccountsForKeywordsOf_AMutationThatWritesNoKeyword_MatchesNothing()
    {
        // Arrange
        var record = CompletedSeenStateChange(isSeen: true);

        // Act
        var accountsForKeywords = record.AccountsForKeywordsOf(
            SourceOccurrence(),
            RemoteEmailKeywords.None,
            RemoteEmailKeywords.Create(["$Todo"]),
            ObservedBeforeTheStore);

        // Assert
        Assert.False(accountsForKeywords);
    }

    /// <summary>Nothing has reached the server for a recorded change, so the keywords standing there are somebody else's doing.</summary>
    [Fact]
    public void AccountsForKeywordsOf_ARecordNoCommandWentOutFor_MatchesNothing()
    {
        // Arrange
        var record = CompletedKeywordChange(MailboxMutation.AddKeywords, "$Todo") with
        {
            Stage = MailboxMutationStage.Recorded,
        };

        // Act
        var accountsForKeywords = record.AccountsForKeywordsOf(
            SourceOccurrence(),
            RemoteEmailKeywords.None,
            RemoteEmailKeywords.Create(["$Todo"]),
            ObservedBeforeTheStore);

        // Assert
        Assert.False(accountsForKeywords);
    }

    /// <summary>Only a record nothing has been asked of a server for is one a person may still change their mind about.</summary>
    [Theory]
    [InlineData(MailboxMutationStage.Recorded, true)]
    [InlineData(MailboxMutationStage.PlacementIssued, false)]
    [InlineData(MailboxMutationStage.PlacementConfirmed, false)]
    [InlineData(MailboxMutationStage.SourceFlaggedDeleted, false)]
    [InlineData(MailboxMutationStage.Completed, false)]
    [InlineData(MailboxMutationStage.Abandoned, false)]
    [InlineData(MailboxMutationStage.Cancelled, false)]
    public void IsWithdrawable_AStage_AnswersWhetherNothingHasGoneOutYet(MailboxMutationStage stage, bool expected)
    {
        // Arrange
        var record = CompletedRelocation() with { Stage = stage };

        // Assert
        Assert.Equal(expected, record.IsWithdrawable);
    }

    /// <summary>
    /// A withdrawn record is the second stage nothing was ever issued for, which is what every provenance question
    /// rests on: a record that stopped before a command went out explains no flag, no keyword, and no disappearance.
    /// </summary>
    [Theory]
    [InlineData(MailboxMutationStage.Recorded, false)]
    [InlineData(MailboxMutationStage.Cancelled, false)]
    [InlineData(MailboxMutationStage.PlacementIssued, true)]
    [InlineData(MailboxMutationStage.PlacementConfirmed, true)]
    [InlineData(MailboxMutationStage.SourceFlaggedDeleted, true)]
    [InlineData(MailboxMutationStage.Completed, true)]
    [InlineData(MailboxMutationStage.Abandoned, true)]
    public void MayHaveReachedTheServer_AStage_AnswersWhetherACommandCouldHaveGoneOut(
        MailboxMutationStage stage,
        bool expected)
    {
        // Arrange
        var record = CompletedRelocation() with { Stage = stage };

        // Assert
        Assert.Equal(expected, record.MayHaveReachedTheServer);
    }

    /// <summary>A withdrawn record carries no work, so a pass that treated it as outstanding would be carrying a change nobody wants.</summary>
    [Fact]
    public void IsTerminal_AWithdrawnRecord_IsFinished()
    {
        // Arrange
        var record = CompletedRelocation() with { Stage = MailboxMutationStage.Cancelled };

        // Assert
        Assert.True(record.IsTerminal);
        Assert.Equal(MailboxMutationLifecycle.Cancelled, record.Lifecycle);
    }

    /// <summary>A message that disappeared while a withdrawn move sat unissued disappeared for some other reason.</summary>
    [Fact]
    public void AccountsForRemovalOf_AWithdrawnRelocation_MatchesNothing()
    {
        // Arrange
        var record = CompletedRelocation() with { Stage = MailboxMutationStage.Cancelled };

        // Act
        var accountsForRemoval = record.AccountsForRemovalOf(SourceOccurrence());

        // Assert
        Assert.False(accountsForRemoval);
    }

    /// <summary>A flag standing where a withdrawn store never went out was put there by somebody else.</summary>
    [Fact]
    public void AccountsForSeenStateOf_AWithdrawnStore_MatchesNothing()
    {
        // Arrange
        var record = CompletedSeenStateChange(isSeen: true) with { Stage = MailboxMutationStage.Cancelled };

        // Act
        var accountsForSeenState = record.AccountsForSeenStateOf(
            SourceOccurrence(),
            observedSeenState: true,
            ObservedBeforeTheStore);

        // Assert
        Assert.False(accountsForSeenState);
    }

    private static MailboxMutationRecord CompletedSeenStateChange(bool isSeen) => CompletedRelocation() with
    {
        Request = MailboxMutationRequest.SetSeen(LocalEmail, SyntheticMailOwner.Deployment, SourceOccurrence(), Requester, isSeen),
        RequiresSourceRemoval = false,
        Placement = RemoteEmailPlacement.NotReported(),
    };

    private static MailboxMutationRecord CompletedFlaggedStateChange(bool isFlagged) => CompletedRelocation() with
    {
        Request = MailboxMutationRequest.SetFlagged(LocalEmail, SyntheticMailOwner.Deployment, SourceOccurrence(), Requester, isFlagged),
        RequiresSourceRemoval = false,
        Placement = RemoteEmailPlacement.NotReported(),
    };

    private static MailboxMutationRecord CompletedKeywordChange(MailboxMutation mutation, params string[] keywords)
    {
        var authored = AuthoredMailKeywords.Create(keywords);
        var occurrence = SourceOccurrence();

        var request = mutation == MailboxMutation.AddKeywords
            ? MailboxMutationRequest.AddKeywords(LocalEmail, SyntheticMailOwner.Deployment, occurrence, Requester, authored)
            : mutation == MailboxMutation.RemoveKeywords
                ? MailboxMutationRequest.RemoveKeywords(LocalEmail, SyntheticMailOwner.Deployment, occurrence, Requester, authored)
                : MailboxMutationRequest.SetKeywords(LocalEmail, SyntheticMailOwner.Deployment, occurrence, Requester, authored);

        return CompletedRelocation() with
        {
            Request = request,
            RequiresSourceRemoval = false,
            Placement = RemoteEmailPlacement.NotReported(),
        };
    }
}
