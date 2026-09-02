// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Delivery;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Rebuilds the domain record one stored mutation row describes.</summary>
/// <remarks>
/// It is shared rather than owned by one store because two adapters read the same rows for different questions — the
/// performer's, which asks what a mutation still owes, and synchronization's, which asks whether an occurrence it just
/// met is one MailFathom created. A second mapping would be a second reading of the same row, and the two would drift.
/// </remarks>
internal static class MailboxMutationRecordMapping
{
    /// <summary>Rebuilds the record a row and its folder binding describe.</summary>
    /// <param name="entity">The stored row.</param>
    /// <param name="folder">The binding the source occurrence was read under, which turns a folder key back into an alias and generation.</param>
    /// <returns>The record that row states.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the row names a mutation or a destination folder path that no longer parses.</exception>
    internal static MailboxMutationRecord ToRecord(MailboxMutationEntity entity, MailFolderEntity folder)
    {
        if (!MailboxMutation.TryParseName(entity.Mutation, out var mutation))
        {
            throw new InvalidOperationException(
                $"Mailbox mutation record {entity.Id} names '{entity.Mutation}', which is not a permitted mutation.");
        }

        var occurrence = EmailOccurrenceId.Create(
            MailAccountId.Create(entity.MailboxAccountId),
            new MailFolderResolutionId(
                MailFolderAlias.Create(folder.Alias),
                MailFolderResolutionGeneration.Create(folder.ResolutionGeneration)),
            ImapUidValidity.Create(entity.UidValidity),
            ImapUid.Create(entity.Uid));

        return new MailboxMutationRecord
        {
            Id = MailboxMutationRecordId.Create(entity.Id),
            Request = MailboxMutationRequest.Create(
                StoredEmailId.Create(entity.StoredEmailId),
                MailOwnerId.Create(entity.OwnerId),
                occurrence,
                mutation,
                MailboxMutationRequester.Create(entity.RequesterOrigin, entity.RequesterIdentity),
                ToDestinationPath(entity),
                entity.DesiredSeenState,
                entity.DesiredFlaggedState,
                ToKeywords(entity),
                ToLocalDisposition(entity, mutation)),
            Stage = entity.Stage,
            IsAudited = entity.AuditTrailEnabled,
            RequiresSourceRemoval = entity.RequiresSourceRemoval,
            Placement = StoredRemotePlacement.Of(entity.PlacementUidValidity, entity.PlacementUid),
            AttemptCount = entity.AttemptCount,
            RecordedAt = entity.RecordedAt,
            StageChangedAt = entity.StageChangedAt,
            LastFailure = StoredFailureCode.ToErrorCode(entity.LastFailureCode),
            PlacementObservedAt = entity.PlacementObservedAt,
            SourceRemovalObservedAt = entity.SourceRemovalObservedAt,
        };
    }

    /// <summary>Restores what the change decided about the local copy, refusing a delete row that decided nothing.</summary>
    /// <remarks>
    /// <para>
    /// A delete always decided one, and the absence is a defect rather than a value to supply, because every disposition
    /// destroys something a different one keeps. Reading a missing one as the default would silently retain mail an
    /// operator configured away, and reading it as the erasure would destroy mail nobody agreed to lose, so the row is
    /// refused instead.
    /// </para>
    /// <para>
    /// A relocation carries one only when it moved the message to a folder nothing mirrors, which is where its local
    /// copy stops being on its way somewhere and becomes mail that has left the mirrored mailbox. The value is read back
    /// rather than recomputed, for the reason it was written down: it is what the owner authored the change under, and
    /// reconciliation applies it in a later run the account may by then be configured differently for.
    /// </para>
    /// </remarks>
    private static AuthoredDeleteEmailDisposition? ToLocalDisposition(
        MailboxMutationEntity entity,
        MailboxMutation mutation)
    {
        if (mutation == MailboxMutation.Delete)
        {
            return entity.LocalDisposition
                ?? throw new InvalidOperationException(
                    $"Mailbox mutation record {entity.Id} deletes an email and names no local disposition.");
        }

        return mutation == MailboxMutation.Relocate ? entity.LocalDisposition : null;
    }

    /// <summary>Restores the keywords a keyword mutation named, exactly as they were stored.</summary>
    /// <remarks>
    /// <para>
    /// The null column and the empty array are kept apart, because they say different things: no keyword mutation at
    /// all, and a replacement that clears every keyword. Reading the empty array as absence would turn the second into
    /// the first and leave a stored request unperformable.
    /// </para>
    /// <para>
    /// A row carrying a keyword no <c>STORE</c> could name fails the read rather than being filtered down to the ones
    /// that would work. Silently issuing a narrower change than the one that was written down is the outcome this
    /// refuses; a row can only be in that state by having been edited by hand, and a mutation that stops visibly is
    /// what an operator can act on.
    /// </para>
    /// </remarks>
    private static AuthoredMailKeywords? ToKeywords(MailboxMutationEntity entity)
    {
        if (entity.Keywords is not { } keywords)
        {
            return null;
        }

        if (AuthoredMailKeywords.TryCreate(keywords, out var authored))
        {
            return authored;
        }

        // Which of the two failed decides what an operator does about the row, so the message says which rather than
        // reporting the commoner one for both.
        var cause = keywords.Any(keyword => !AuthoredMailKeywords.IsWritable(keyword))
            ? "names a keyword that no mail server can be asked to store"
            : $"names more than the {RemoteEmailKeywords.MaximumKeywords} keywords one email keeps";

        throw new InvalidOperationException($"Mailbox mutation record {entity.Id} {cause}.");
    }

    /// <summary>Restores the destination folder a relocation or a copy named, exactly as it was stored.</summary>
    /// <remarks>
    /// The text is not trimmed on the way back, for the reason it was not trimmed on the way in: IMAP permits a quoted
    /// mailbox name bounded by a space, and normalizing one would name a different mailbox or none at all.
    /// </remarks>
    private static RemoteFolderPath? ToDestinationPath(MailboxMutationEntity entity)
    {
        if (entity.DestinationFolderPath is null)
        {
            return null;
        }

        return RemoteFolderPath.TryCreate(
            entity.DestinationFolderPath,
            entity.DestinationHierarchyDelimiter is { Length: > 0 } delimiter ? delimiter[0] : null,
            out var destinationPath)
            ? destinationPath
            : throw new InvalidOperationException(
                $"Mailbox mutation record {entity.Id} carries a destination folder path that names no folder.");
    }
}
