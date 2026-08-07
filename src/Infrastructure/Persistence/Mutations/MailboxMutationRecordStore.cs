// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Records every change MailFathom asks a mail server to make, in PostgreSQL, before it is asked for.</summary>
/// <remarks>
/// <para>
/// The write paths use the context enlisted in the caller's session, so a record is only ever written inside the
/// transaction the caller opened; the read path uses the scoped context, because it joins no transaction.
/// </para>
/// <para>
/// The idempotency identity is not checked and then written. The check exists — a request that already has a record
/// reads it back rather than inserting a second — but it is the unique index that decides, because two callers can pass
/// any application-level check between reading and writing and only the constraint closes that window. A loser is
/// reported as an optimistic conflict, and the retry finds the winner's row.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailboxMutationRecordStore(MailFathomDbContext readContext, TimeProvider timeProvider)
    : IMailboxMutationRecordStore
{
    /// <inheritdoc />
    public async Task<MailboxMutationRecord> OpenAsync(
        IPersistenceSession session,
        MailboxMutationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(request);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var folder = await MailFolderEntityResolver.GetRequiredAsync(
            writeContext,
            request.Occurrence.AccountId,
            request.Occurrence.FolderResolutionId,
            cancellationToken);

        var existing = await FindByIdentityAsync(writeContext, request, folder.Id, cancellationToken);
        if (existing is not null)
        {
            return ToRecord(existing, folder);
        }

        var storedEmail = await writeContext.StoredEmails.FindAsync([request.StoredEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                $"No stored email carries the identifier {request.StoredEmailId}, so no mutation can be recorded against it.");

        var recordedAt = timeProvider.GetUtcNow();
        var entity = new MailboxMutationEntity
        {
            Id = Guid.CreateVersion7(recordedAt),
            StoredEmailId = storedEmail.Id,
            StoredEmail = storedEmail,
            MailboxAccountId = request.Occurrence.AccountId.Value,
            MailFolderId = folder.Id,
            MailFolder = folder,
            UidValidity = request.Occurrence.UidValidity.Value,
            Uid = request.Occurrence.Uid.Value,
            Mutation = request.Mutation.Name,
            RequesterOrigin = request.Requester.Origin,
            RequesterIdentity = request.Requester.Identity,
            DestinationFolderPath = request.DestinationPath?.Value,
            DestinationHierarchyDelimiter = request.DestinationPath?.HierarchyDelimiter?.ToString(),
            DesiredSeenState = request.DesiredSeenState,
            Stage = MailboxMutationStage.Recorded,
            AttemptCount = 0,
            RecordedAt = recordedAt,
            StageChangedAt = recordedAt,
        };

        writeContext.MailboxMutations.Add(entity);

        return ToRecord(entity, folder);
    }

    /// <inheritdoc />
    public async Task<int> CountAttemptAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, recordId, cancellationToken);

        entity.AttemptCount++;

        return entity.AttemptCount;
    }

    /// <inheritdoc />
    public async Task AdvanceAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        MailboxMutationStage stage,
        RemoteEmailPlacement? placement,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, recordId, cancellationToken);

        RequireForwardMovement(entity, stage);

        entity.Stage = stage;
        entity.StageChangedAt = timeProvider.GetUtcNow();

        if (placement is not null)
        {
            entity.PlacementUidValidity = placement.UidValidity?.Value;
            entity.PlacementUid = placement.Uid?.Value;
        }
    }

    /// <inheritdoc />
    public async Task RecordFailureAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, recordId, cancellationToken);

        entity.LastFailureCode = failure.Value;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxMutationRecord>> ReadOutstandingAsync(
        MailAccountId accountId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var accountValue = accountId.Value;

        // The binding is joined rather than copied onto the row, because it is what turns a folder key back into the
        // alias and generation an occurrence identity is made of.
        var entities = await readContext.MailboxMutations
            .AsNoTracking()
            .Include(mutation => mutation.MailFolder)
            .Where(mutation => mutation.MailboxAccountId == accountValue &&
                mutation.Stage != MailboxMutationStage.Completed)
            .OrderBy(mutation => mutation.RecordedAt)
            .ThenBy(mutation => mutation.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return [.. entities.Select(entity => ToRecord(entity, entity.MailFolder))];
    }

    /// <summary>Refuses a stage that would pull the record backwards or move it out of a terminal one.</summary>
    /// <remarks>
    /// The record is what a resumed attempt reads, so a late write from an attempt that lost its race must not undo
    /// progress a later one made. A terminal stage is refused separately from the ordering, because the two terminal
    /// members are ordered against each other and neither may follow the other.
    /// </remarks>
    private static void RequireForwardMovement(MailboxMutationEntity entity, MailboxMutationStage stage)
    {
        var isTerminal = entity.Stage is MailboxMutationStage.Completed or MailboxMutationStage.Abandoned;

        if (isTerminal || stage <= entity.Stage)
        {
            throw new InvalidOperationException(
                $"Mailbox mutation record {entity.Id} is at stage {entity.Stage} and cannot be moved to {stage}.");
        }
    }

    private static async Task<MailboxMutationEntity?> FindByIdentityAsync(
        MailFathomDbContext writeContext,
        MailboxMutationRequest request,
        long folderId,
        CancellationToken cancellationToken)
    {
        var uidValidity = request.Occurrence.UidValidity.Value;
        var uid = request.Occurrence.Uid.Value;
        var origin = request.Requester.Origin;
        var identity = request.Requester.Identity;
        var mutationName = request.Mutation.Name;

        // Looked up by the idempotency identity rather than by the key, so the change-tracker pass is explicit: a
        // request opened earlier in this same uncommitted session would be invisible to a query.
        return await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            writeContext.MailboxMutations,
            writeContext.MailboxMutations,
            mutation => mutation.MailFolderId == folderId &&
                mutation.UidValidity == uidValidity &&
                mutation.Uid == uid &&
                mutation.RequesterOrigin == origin &&
                mutation.RequesterIdentity == identity &&
                mutation.Mutation == mutationName,
            cancellationToken);
    }

    private static MailboxMutationRecord ToRecord(MailboxMutationEntity entity, MailFolderEntity folder)
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
                occurrence,
                mutation,
                MailboxMutationRequester.Create(entity.RequesterOrigin, entity.RequesterIdentity),
                ToDestinationPath(entity),
                entity.DesiredSeenState),
            Stage = entity.Stage,
            Placement = ToPlacement(entity),
            AttemptCount = entity.AttemptCount,
            RecordedAt = entity.RecordedAt,
            StageChangedAt = entity.StageChangedAt,
            LastFailure = ToFailure(entity),
        };
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

    private static RemoteEmailPlacement ToPlacement(MailboxMutationEntity entity) =>
        entity is { PlacementUidValidity: { } uidValidity, PlacementUid: { } uid }
            ? RemoteEmailPlacement.Reported(ImapUidValidity.Create(uidValidity), ImapUid.Create(uid))
            : RemoteEmailPlacement.NotReported();

    /// <summary>Reads back the code of the failure the last attempt ended in.</summary>
    /// <remarks>
    /// A number this build does not recognize is a row written by one that allocated a code since. It is diagnostic
    /// detail rather than something acted on, so it is reported as absent instead of failing the read of every other
    /// mutation the account has.
    /// </remarks>
    private static MailFathomErrorCode? ToFailure(MailboxMutationEntity entity)
    {
        if (entity.LastFailureCode is not { } failureCode)
        {
            return null;
        }

        return MailFathomErrorCode.TryParse(failureCode, out var failure) ? failure : null;
    }

    private static async Task<MailboxMutationEntity> RequireEntityAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        // A primary-key lookup, so FindAsync already resolves an insert this session may still be holding.
        return await writeContext.MailboxMutations.FindAsync([recordId.Value], cancellationToken)
            ?? throw new InvalidOperationException($"No mailbox mutation record carries the identifier {recordId}.");
    }
}
