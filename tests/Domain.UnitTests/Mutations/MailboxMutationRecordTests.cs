// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Domain.UnitTests.Mutations;

/// <summary>Covers the join a synchronization run makes between what it discovered and what MailFathom recorded.</summary>
public sealed class MailboxMutationRecordTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly RemoteFolderPath Inbox = RemoteFolderPath.Create("INBOX");

    private static readonly RemoteFolderPath Archive = RemoteFolderPath.Create("Archive", '/');

    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7(RecordedAt));

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Rule("file-newsletters", 3);

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
            Request = MailboxMutationRequest.Copy(LocalEmail, SourceOccurrence(), Requester, Archive),
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
            Request = MailboxMutationRequest.Copy(LocalEmail, SourceOccurrence(), Requester, Archive),
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
            Request = MailboxMutationRequest.Delete(LocalEmail, SourceOccurrence(), Requester),
        };
        var setSeen = CompletedRelocation() with
        {
            Request = MailboxMutationRequest.SetSeen(LocalEmail, SourceOccurrence(), Requester, isSeen: true),
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
            Request = MailboxMutationRequest.SetSeen(LocalEmail, SourceOccurrence(), Requester, isSeen: true),
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
            Request = MailboxMutationRequest.Delete(LocalEmail, SourceOccurrence(), Requester),
            Placement = RemoteEmailPlacement.NotReported(),
        };
        var setSeen = delete with
        {
            Request = MailboxMutationRequest.SetSeen(LocalEmail, SourceOccurrence(), Requester, isSeen: true),
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
        Request = MailboxMutationRequest.Relocate(LocalEmail, SourceOccurrence(), Requester, Archive),
        Stage = MailboxMutationStage.Completed,
        RequiresSourceRemoval = true,
        Placement = RemoteEmailPlacement.Reported(DestinationUidValidity, PlacedUid),
        AttemptCount = 1,
        RecordedAt = RecordedAt,
        StageChangedAt = RecordedAt,
        LastFailure = null,
        PlacementObservedAt = null,
        SourceRemovalObservedAt = null,
    };

    private static MailboxMutationRecord CompletedSeenStateChange(bool isSeen) => CompletedRelocation() with
    {
        Request = MailboxMutationRequest.SetSeen(LocalEmail, SourceOccurrence(), Requester, isSeen),
        RequiresSourceRemoval = false,
        Placement = RemoteEmailPlacement.NotReported(),
    };
}
