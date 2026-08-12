// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
using Xunit;

namespace MailFathom.Domain.UnitTests.Mutations.Audit;

/// <summary>Covers what one finished mutation states in the history it leaves behind.</summary>
public sealed class MailboxMutationAuditEntryTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset CompletedAt = RecordedAt.AddMinutes(4);

    private static readonly RemoteFolderPath Inbox = RemoteFolderPath.Create("INBOX");

    private static readonly RemoteFolderPath Archive = RemoteFolderPath.Create("Archive", '/');

    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7(RecordedAt));

    private static readonly MailboxMutationRequester Requester = MailboxMutationRequester.Rule("file-newsletters", "3");

    private static readonly MailFolderResolution SourceFolder = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        Inbox);

    private static readonly ImapUidValidity DestinationUidValidity = ImapUidValidity.Create(9001);

    private static readonly ImapUid PlacedUid = ImapUid.Create(77);

    private static readonly MailboxMutationAuditEntryId EntryId =
        MailboxMutationAuditEntryId.Create(Guid.CreateVersion7(CompletedAt));

    /// <summary>Everything a person needs to reconstruct a relocation without reading the mail is on the entry.</summary>
    [Fact]
    public void Of_CompletedRelocation_StatesTheActInFull()
    {
        // Arrange
        var record = CompletedRelocation();

        // Act
        var entry = MailboxMutationAuditEntry.Of(EntryId, record, SourceFolder, CompletedAt);

        // Assert
        Assert.Equal(
            (MailboxMutation.Relocate,
                LocalEmail,
                Inbox,
                ImapUidValidity.Create(1),
                ImapUid.Create(41),
                (RemoteFolderPath?)Archive,
                (ImapUid?)PlacedUid,
                Requester,
                RecordedAt,
                CompletedAt,
                MailboxMutationAuditOutcome.Performed,
                (MailFathomErrorCode?)null),
            (entry.Mutation,
                entry.StoredEmailId,
                entry.SourceFolderPath,
                entry.SourceUidValidity,
                entry.SourceUid,
                entry.DestinationFolderPath,
                entry.Placement.Uid,
                entry.Requester,
                entry.RequestedAt,
                entry.CompletedAt,
                entry.Outcome,
                entry.Failure));
    }

    /// <summary>A delete names no destination and no flag direction, and still names the folder the mail was taken out of.</summary>
    [Fact]
    public void Of_CompletedDelete_NamesTheSourceAndNoDestination()
    {
        // Arrange
        var record = CompletedRelocation() with
        {
            Request = MailboxMutationRequest.Delete(
                LocalEmail,
                SourceOccurrence(),
                Requester,
                AuthoredDeleteEmailDisposition.RetainLocalCopy),
            Placement = RemoteEmailPlacement.NotReported(),
        };

        // Act
        var entry = MailboxMutationAuditEntry.Of(EntryId, record, SourceFolder, CompletedAt);

        // Assert
        Assert.Equal(
            (MailboxMutation.Delete, Inbox, (RemoteFolderPath?)null, (bool?)null),
            (entry.Mutation, entry.SourceFolderPath, entry.DestinationFolderPath, entry.DesiredSeenState));
    }

    /// <summary>A flag change records which way it was asked for, because reading it back is the whole question.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Of_CompletedSeenStateChange_RecordsTheDirection(bool isSeen)
    {
        // Arrange
        var record = CompletedRelocation() with
        {
            Request = MailboxMutationRequest.SetSeen(LocalEmail, SourceOccurrence(), Requester, isSeen),
            Placement = RemoteEmailPlacement.NotReported(),
        };

        // Act
        var entry = MailboxMutationAuditEntry.Of(EntryId, record, SourceFolder, CompletedAt);

        // Assert
        Assert.Equal(
            (MailboxMutation.SetSeen, (bool?)isSeen),
            (entry.Mutation, entry.DesiredSeenState));
    }

    /// <summary>A copy records where the second occurrence landed, which is what tells it from the relocation it looks like.</summary>
    [Fact]
    public void Of_CompletedCopy_RecordsThePlacementTheServerNamed()
    {
        // Arrange
        var record = CompletedRelocation() with
        {
            Request = MailboxMutationRequest.Copy(LocalEmail, SourceOccurrence(), Requester, Archive),
        };

        // Act
        var entry = MailboxMutationAuditEntry.Of(EntryId, record, SourceFolder, CompletedAt);

        // Assert
        Assert.Equal(
            (MailboxMutation.Copy, (RemoteFolderPath?)Archive, (ImapUidValidity?)DestinationUidValidity, (ImapUid?)PlacedUid),
            (entry.Mutation, entry.DestinationFolderPath, entry.Placement.UidValidity, entry.Placement.Uid));
    }

    /// <summary>A change that was given up on says so, and carries the code it was given up on for.</summary>
    [Fact]
    public void Of_AbandonedMutation_CarriesTheFailureItEndedOn()
    {
        // Arrange
        var record = CompletedRelocation() with
        {
            Stage = MailboxMutationStage.Abandoned,
            LastFailure = MailFathomErrorCode.MailboxMutationDestinationMissing,
        };

        // Act
        var entry = MailboxMutationAuditEntry.Of(EntryId, record, SourceFolder, CompletedAt);

        // Assert
        Assert.Equal(
            (MailboxMutationAuditOutcome.Abandoned, (MailFathomErrorCode?)MailFathomErrorCode.MailboxMutationDestinationMissing),
            (entry.Outcome, entry.Failure));
    }

    /// <summary>A change that succeeded after a failed attempt records the ending, not what it survived on the way.</summary>
    [Fact]
    public void Of_CompletedMutationThatFailedOnce_CarriesNoFailure()
    {
        // Arrange
        var record = CompletedRelocation() with
        {
            LastFailure = MailFathomErrorCode.MailboxUnavailable,
        };

        // Act
        var entry = MailboxMutationAuditEntry.Of(EntryId, record, SourceFolder, CompletedAt);

        // Assert
        Assert.Equal(
            (MailboxMutationAuditOutcome.Performed, (MailFathomErrorCode?)null),
            (entry.Outcome, entry.Failure));
    }

    /// <summary>A mutation still in flight states no ending, so no entry is written from one.</summary>
    [Theory]
    [InlineData(MailboxMutationStage.Recorded)]
    [InlineData(MailboxMutationStage.PlacementIssued)]
    [InlineData(MailboxMutationStage.PlacementConfirmed)]
    [InlineData(MailboxMutationStage.SourceFlaggedDeleted)]
    public void Of_MutationThatHasNotEnded_IsRefused(MailboxMutationStage stage)
    {
        // Arrange
        var record = CompletedRelocation() with { Stage = stage };

        // Act
        var refusal = Record.Exception(() =>
            MailboxMutationAuditEntry.Of(EntryId, record, SourceFolder, CompletedAt));

        // Assert
        Assert.IsType<ArgumentException>(refusal);
    }

    /// <summary>A binding that is not the one the change was aimed at would put the act in the wrong folder.</summary>
    [Fact]
    public void Of_FolderBindingTheOccurrenceDoesNotName_IsRefused()
    {
        // Arrange
        var otherFolder = MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("later"), Archive);

        // Act
        var refusal = Record.Exception(() =>
            MailboxMutationAuditEntry.Of(EntryId, CompletedRelocation(), otherFolder, CompletedAt));

        // Assert
        Assert.IsType<ArgumentException>(refusal);
    }

    /// <summary>The entry holds identifiers, folder paths, and MailFathom's own names — and nothing that could be mail.</summary>
    /// <remarks>
    /// Asserted against the declared members rather than against one entry's values, because what has to stay true is
    /// that no subject, address, body fragment, or filename is ever added here. A member arriving without a decision
    /// fails this and forces one.
    /// </remarks>
    [Fact]
    public void DeclaredMembers_AreOnlyIdentitiesTimestampsAndOutcome()
    {
        // Arrange
        string[] expectedMembers =
        [
            nameof(MailboxMutationAuditEntry.Id),
            nameof(MailboxMutationAuditEntry.MutationRecordId),
            nameof(MailboxMutationAuditEntry.AccountId),
            nameof(MailboxMutationAuditEntry.StoredEmailId),
            nameof(MailboxMutationAuditEntry.Mutation),
            nameof(MailboxMutationAuditEntry.SourceFolderPath),
            nameof(MailboxMutationAuditEntry.SourceUidValidity),
            nameof(MailboxMutationAuditEntry.SourceUid),
            nameof(MailboxMutationAuditEntry.DestinationFolderPath),
            nameof(MailboxMutationAuditEntry.Placement),
            nameof(MailboxMutationAuditEntry.DesiredSeenState),
            nameof(MailboxMutationAuditEntry.Requester),
            nameof(MailboxMutationAuditEntry.RequestedAt),
            nameof(MailboxMutationAuditEntry.CompletedAt),
            nameof(MailboxMutationAuditEntry.Outcome),
            nameof(MailboxMutationAuditEntry.Failure),
        ];

        // Act
        var declaredMembers = typeof(MailboxMutationAuditEntry)
            .GetProperties()
            .Select(property => property.Name)
            .Where(name => !string.Equals(name, "EqualityContract", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

        // Assert
        Assert.Equal(expectedMembers.Order(StringComparer.Ordinal), declaredMembers);
    }

    private static EmailOccurrenceId SourceOccurrence() => EmailOccurrenceId.Create(
        MailAccountId.Create("work"),
        SourceFolder.Id,
        ImapUidValidity.Create(1),
        ImapUid.Create(41));

    private static MailboxMutationRecord CompletedRelocation() => new()
    {
        Id = MailboxMutationRecordId.Create(Guid.CreateVersion7(RecordedAt)),
        Request = MailboxMutationRequest.Relocate(LocalEmail, SourceOccurrence(), Requester, Archive),
        Stage = MailboxMutationStage.Completed,
        IsAudited = true,
        RequiresSourceRemoval = true,
        Placement = RemoteEmailPlacement.Reported(DestinationUidValidity, PlacedUid),
        AttemptCount = 1,
        RecordedAt = RecordedAt,
        StageChangedAt = CompletedAt,
        LastFailure = null,
        PlacementObservedAt = null,
        SourceRemovalObservedAt = null,
    };
}
