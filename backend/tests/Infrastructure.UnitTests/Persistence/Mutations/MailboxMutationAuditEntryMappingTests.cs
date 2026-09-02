// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Mutations;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Mutations;

/// <summary>Covers which stored audit rows this build can present, and which it declines rather than approximates.</summary>
/// <remarks>
/// The trail is read a page at a time and paginated by position, so a row this build cannot interpret has to be a
/// reported answer rather than an exception: thrown out of the mapping it would fail the whole page and every page after
/// it, which would wedge an account's history from the first row a later build wrote.
/// </remarks>
public sealed class MailboxMutationAuditEntryMappingTests
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A row this build wrote reads back as the act it recorded.</summary>
    [Fact]
    public void TryToEntry_RowThisBuildWrote_RebuildsTheEntry()
    {
        // Arrange
        var entity = StoredRelocation();

        // Act
        var rebuilt = MailboxMutationAuditEntryMapping.TryToEntry(entity, out var entry);

        // Assert
        Assert.True(rebuilt);
        Assert.Equal(
            (MailboxMutation.Relocate,
                RemoteFolderPath.Create("INBOX"),
                (RemoteFolderPath?)RemoteFolderPath.Create("Archive", '/'),
                MailboxMutationAuditOutcome.Performed),
            (entry!.Mutation, entry.SourceFolderPath, entry.DestinationFolderPath, entry.Outcome));
    }

    /// <summary>A mutation kind this build does not permit is a later build's row, and it costs its own place and nothing else.</summary>
    [Fact]
    public void TryToEntry_RowNamingAMutationThisBuildDoesNotPermit_IsDeclined()
    {
        // Arrange
        var entity = StoredRelocation();
        entity.Mutation = "archive-forever";

        // Act
        var rebuilt = MailboxMutationAuditEntryMapping.TryToEntry(entity, out var entry);

        // Assert
        Assert.False(rebuilt);
        Assert.Null(entry);
    }

    /// <summary>A stored path that names no folder is declined the same way, rather than throwing out of the page.</summary>
    [Fact]
    public void TryToEntry_RowCarryingAPathThatNamesNoFolder_IsDeclined()
    {
        // Arrange
        var entity = StoredRelocation();
        entity.SourceFolderPath = "  ";

        // Act
        var rebuilt = MailboxMutationAuditEntryMapping.TryToEntry(entity, out var entry);

        // Assert
        Assert.False(rebuilt);
        Assert.Null(entry);
    }

    /// <summary>A failure code this build has not allocated is diagnostic detail, so it is dropped rather than declining the row.</summary>
    [Fact]
    public void TryToEntry_RowNamingAFailureCodeThisBuildHasNotAllocated_KeepsTheEntryWithoutIt()
    {
        // Arrange
        var entity = StoredRelocation();
        entity.Outcome = MailboxMutationAuditOutcome.Abandoned;
        entity.FailureCode = 99999;

        // Act
        var rebuilt = MailboxMutationAuditEntryMapping.TryToEntry(entity, out var entry);

        // Assert
        Assert.True(rebuilt);
        Assert.Equal(
            (MailboxMutationAuditOutcome.Abandoned, (MailFathomErrorCode?)null),
            (entry!.Outcome, entry.Failure));
    }

    private static MailboxMutationAuditEntryEntity StoredRelocation() => new()
    {
        Id = Guid.CreateVersion7(RecordedAt),
        MutationRecordId = Guid.CreateVersion7(RecordedAt),
        OwnerId = SyntheticMailOwner.Deployment.Value,
        MailboxAccountId = "work",
        StoredEmailId = Guid.CreateVersion7(RecordedAt),
        Mutation = MailboxMutation.Relocate.Name,
        SourceFolderPath = "INBOX",
        SourceHierarchyDelimiter = null,
        SourceUidValidity = 1,
        SourceUid = 41,
        DestinationFolderPath = "Archive",
        DestinationHierarchyDelimiter = "/",
        RequesterOrigin = MailboxMutationOrigin.Rule,
        RequesterIdentity = "file-newsletters@3",
        RequestedAt = RecordedAt,
        CompletedAt = RecordedAt.AddMinutes(4),
        Outcome = MailboxMutationAuditOutcome.Performed,
    };
}
