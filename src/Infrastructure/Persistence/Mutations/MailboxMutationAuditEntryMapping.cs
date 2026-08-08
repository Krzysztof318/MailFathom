// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Mutations.Audit;
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

    /// <summary>Rebuilds the entry one stored row states.</summary>
    /// <param name="entity">The stored row.</param>
    /// <returns>The entry that row states.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the row names a mutation or a folder path that no longer parses.</exception>
    internal static MailboxMutationAuditEntry ToEntry(MailboxMutationAuditEntryEntity entity)
    {
        if (!MailboxMutation.TryParseName(entity.Mutation, out var mutation))
        {
            throw new InvalidOperationException(
                $"Mailbox mutation audit entry {entity.Id} names '{entity.Mutation}', which is not a permitted mutation.");
        }

        return new MailboxMutationAuditEntry
        {
            Id = MailboxMutationAuditEntryId.Create(entity.Id),
            MutationRecordId = MailboxMutationRecordId.Create(entity.MutationRecordId),
            AccountId = MailAccountId.Create(entity.MailboxAccountId),
            StoredEmailId = StoredEmailId.Create(entity.StoredEmailId),
            Mutation = mutation,
            SourceFolderPath = ToFolderPath(
                entity.Id,
                entity.SourceFolderPath,
                entity.SourceHierarchyDelimiter),
            SourceUidValidity = ImapUidValidity.Create(entity.SourceUidValidity),
            SourceUid = ImapUid.Create(entity.SourceUid),
            DestinationFolderPath = ToOptionalFolderPath(
                entity.Id,
                entity.DestinationFolderPath,
                entity.DestinationHierarchyDelimiter),
            Placement = ToPlacement(entity),
            DesiredSeenState = entity.DesiredSeenState,
            Requester = MailboxMutationRequester.Create(entity.RequesterOrigin, entity.RequesterIdentity),
            RequestedAt = entity.RequestedAt,
            CompletedAt = entity.CompletedAt,
            Outcome = entity.Outcome,
            Failure = ToFailure(entity),
        };
    }

    /// <summary>Restores a stored folder path exactly as it was written.</summary>
    /// <remarks>
    /// The text is not trimmed on the way back, for the reason it was not trimmed on the way in: IMAP permits a quoted
    /// mailbox name bounded by a space, and normalizing one would name a different mailbox or none at all.
    /// </remarks>
    private static RemoteFolderPath ToFolderPath(Guid entryId, string storedPath, string? storedDelimiter)
    {
        return RemoteFolderPath.TryCreate(
            storedPath,
            storedDelimiter is { Length: > 0 } delimiter ? delimiter[0] : null,
            out var folderPath)
            ? folderPath
            : throw new InvalidOperationException(
                $"Mailbox mutation audit entry {entryId} carries a folder path that names no folder.");
    }

    /// <summary>Restores a folder path only a relocation or a copy carries.</summary>
    private static RemoteFolderPath? ToOptionalFolderPath(Guid entryId, string? storedPath, string? storedDelimiter) =>
        storedPath is null ? null : ToFolderPath(entryId, storedPath, storedDelimiter);

    private static RemoteEmailPlacement ToPlacement(MailboxMutationAuditEntryEntity entity) =>
        entity is { PlacementUidValidity: { } uidValidity, PlacementUid: { } uid }
            ? RemoteEmailPlacement.Reported(ImapUidValidity.Create(uidValidity), ImapUid.Create(uid))
            : RemoteEmailPlacement.NotReported();

    /// <summary>Reads back the code an abandoned change was given up on for.</summary>
    /// <remarks>
    /// A number this build does not recognize is a row written by one that allocated a code since. It is diagnostic
    /// detail rather than something acted on, so it is reported as absent instead of failing the read of the whole page.
    /// </remarks>
    private static MailFathomErrorCode? ToFailure(MailboxMutationAuditEntryEntity entity)
    {
        if (entity.FailureCode is not { } failureCode)
        {
            return null;
        }

        return MailFathomErrorCode.TryParse(failureCode, out var failure) ? failure : null;
    }
}
