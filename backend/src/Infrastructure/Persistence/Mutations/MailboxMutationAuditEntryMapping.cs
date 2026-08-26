// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
using MailFathom.Infrastructure.Persistence.Delivery;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Turns one audit entry into the row that keeps it, and back.</summary>
internal static class MailboxMutationAuditEntryMapping
{
    /// <summary>Builds the row one entry is kept as.</summary>
    /// <param name="entry">The entry to keep.</param>
    /// <returns>The row to append.</returns>
    internal static MailboxMutationAuditEntryEntity ToEntity(MailboxMutationAuditEntry entry) => new()
    {
        Id = entry.Id.Value,
        MutationRecordId = entry.MutationRecordId.Value,
        MailboxAccountId = entry.AccountId.Value,
        OwnerId = entry.Owner.Value,
        StoredEmailId = entry.StoredEmailId.Value,
        Mutation = entry.Mutation.Name,
        SourceFolderPath = entry.SourceFolderPath.Value,
        SourceHierarchyDelimiter = entry.SourceFolderPath.HierarchyDelimiter?.ToString(),
        SourceUidValidity = entry.SourceUidValidity.Value,
        SourceUid = entry.SourceUid.Value,
        DestinationFolderPath = entry.DestinationFolderPath?.Value,
        DestinationHierarchyDelimiter = entry.DestinationFolderPath?.HierarchyDelimiter?.ToString(),
        PlacementUidValidity = entry.Placement.UidValidity?.Value,
        PlacementUid = entry.Placement.Uid?.Value,
        DesiredSeenState = entry.DesiredSeenState,
        RequesterOrigin = entry.Requester.Origin,
        RequesterIdentity = entry.Requester.Identity,
        RequestedAt = entry.RequestedAt,
        CompletedAt = entry.CompletedAt,
        Outcome = entry.Outcome,
        FailureCode = entry.Failure?.Value,
    };

    /// <summary>Rebuilds the entry one stored row states, or reports a row this build cannot interpret.</summary>
    /// <param name="entity">The stored row.</param>
    /// <param name="entry">The entry that row states, when this build can read it.</param>
    /// <returns><see langword="true" /> when the row was rebuilt; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// <para>
    /// A row is refused rather than approximated when it names a mutation or a folder path this build does not
    /// recognize — which is version skew rather than corruption: a later build that permits a fifth mutation writes
    /// entries this one has no value for, and a rollback then reads them.
    /// </para>
    /// <para>
    /// It is reported rather than thrown for the reason <see cref="StoredFailureCode" /> degrades an unrecognized code:
    /// this trail is read a page at a time and paginated by position, so one unreadable row thrown out of the mapping
    /// would fail the whole page and every page after it. The caller leaves the row out, says so, and walks on.
    /// </para>
    /// </remarks>
    internal static bool TryToEntry(
        MailboxMutationAuditEntryEntity entity,
        [NotNullWhen(true)] out MailboxMutationAuditEntry? entry)
    {
        entry = null;

        if (!MailboxMutation.TryParseName(entity.Mutation, out var mutation)
            || !TryToFolderPath(entity.SourceFolderPath, entity.SourceHierarchyDelimiter, out var sourceFolderPath)
            || !TryToOptionalFolderPath(
                entity.DestinationFolderPath,
                entity.DestinationHierarchyDelimiter,
                out var destinationFolderPath))
        {
            return false;
        }

        entry = new MailboxMutationAuditEntry
        {
            Id = MailboxMutationAuditEntryId.Create(entity.Id),
            MutationRecordId = MailboxMutationRecordId.Create(entity.MutationRecordId),
            AccountId = MailAccountId.Create(entity.MailboxAccountId),
            Owner = MailOwnerId.Create(entity.OwnerId),
            StoredEmailId = StoredEmailId.Create(entity.StoredEmailId),
            Mutation = mutation,
            SourceFolderPath = sourceFolderPath,
            SourceUidValidity = ImapUidValidity.Create(entity.SourceUidValidity),
            SourceUid = ImapUid.Create(entity.SourceUid),
            DestinationFolderPath = destinationFolderPath,
            Placement = StoredRemotePlacement.Of(entity.PlacementUidValidity, entity.PlacementUid),
            DesiredSeenState = entity.DesiredSeenState,
            Requester = MailboxMutationRequester.Create(entity.RequesterOrigin, entity.RequesterIdentity),
            RequestedAt = entity.RequestedAt,
            CompletedAt = entity.CompletedAt,
            Outcome = entity.Outcome,
            Failure = StoredFailureCode.ToErrorCode(entity.FailureCode),
        };

        return true;
    }

    /// <summary>Restores a stored folder path exactly as it was written.</summary>
    /// <remarks>
    /// The text is not trimmed on the way back, for the reason it was not trimmed on the way in: IMAP permits a quoted
    /// mailbox name bounded by a space, and normalizing one would name a different mailbox or none at all.
    /// </remarks>
    private static bool TryToFolderPath(string storedPath, string? storedDelimiter, out RemoteFolderPath folderPath) =>
        RemoteFolderPath.TryCreate(
            storedPath,
            storedDelimiter is { Length: > 0 } delimiter ? delimiter[0] : null,
            out folderPath);

    /// <summary>Restores a folder path only a relocation or a copy carries.</summary>
    private static bool TryToOptionalFolderPath(
        string? storedPath,
        string? storedDelimiter,
        out RemoteFolderPath? folderPath)
    {
        if (storedPath is null)
        {
            folderPath = null;

            return true;
        }

        var parsed = TryToFolderPath(storedPath, storedDelimiter, out var storedFolderPath);
        folderPath = parsed ? storedFolderPath : null;

        return parsed;
    }
}
