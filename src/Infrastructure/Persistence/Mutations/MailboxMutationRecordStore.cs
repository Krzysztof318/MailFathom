// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
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
internal sealed class MailboxMutationRecordStore(
    MailFathomDbContext readContext,
    IMailboxMutationAuditSettingsReader auditSettingsReader,
    TimeProvider timeProvider)
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
            return MailboxMutationRecordMapping.ToRecord(existing, folder);
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
            LocalDisposition = request.LocalDisposition,

            // Resolved here, with the row, so a trail switched on or off while this mutation is in flight decides
            // nothing about a change already begun.
            AuditTrailEnabled = auditSettingsReader.GetAuditSettings(request.Occurrence.AccountId).IsEnabled,
            Stage = MailboxMutationStage.Recorded,
            RequiresSourceRemoval = false,
            AttemptCount = 0,
            RecordedAt = recordedAt,
            StageChangedAt = recordedAt,
        };

        writeContext.MailboxMutations.Add(entity);

        return MailboxMutationRecordMapping.ToRecord(entity, folder);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Read through the scoped context, because it joins no transaction, and narrowed by the local email first: the
    /// foreign key onto the email is indexed, and the handful of rows one email ever carries is what the two remaining
    /// comparisons run over. The mutation is compared by its stored name and the origin by the text its conversion
    /// writes, which is what keeps the predicate translatable rather than evaluated in this process.
    /// </remarks>
    public Task<bool> HasRecordAsync(
        StoredEmailId storedEmailId,
        MailboxMutation mutation,
        MailboxMutationOrigin origin,
        CancellationToken cancellationToken)
    {
        if (!mutation.IsSpecified)
        {
            throw new ArgumentException("A mutation record is read by a permitted mutation.", nameof(mutation));
        }

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(
                nameof(origin),
                origin,
                "The mutation requester origin is not one this system declares.");
        }

        var emailId = storedEmailId.Value;
        var mutationName = mutation.Name;

        return readContext.MailboxMutations
            .AsNoTracking()
            .AnyAsync(
                record => record.StoredEmailId == emailId
                    && record.Mutation == mutationName
                    && record.RequesterOrigin == origin,
                cancellationToken);
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
    public async Task RecordPlacementIssuedAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        bool requiresSourceRemoval,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, recordId, cancellationToken);

        RequireForwardMovement(entity, MailboxMutationStage.PlacementIssued);

        entity.Stage = MailboxMutationStage.PlacementIssued;
        entity.RequiresSourceRemoval = requiresSourceRemoval;
        entity.StageChangedAt = timeProvider.GetUtcNow();
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
    public async Task<IReadOnlyList<OutstandingMailboxMutation>> ReadOutstandingAsync(
        MailAccountId accountId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var accountValue = accountId.Value;

        // The binding is joined rather than copied onto the row, because it is what turns a folder key back into the
        // alias and generation an occurrence identity is made of, and the remote path a resumed attempt selects.
        var entities = await readContext.MailboxMutations
            .AsNoTracking()
            .Include(mutation => mutation.MailFolder)
            .Where(mutation => mutation.MailboxAccountId == accountValue &&
                mutation.Stage != MailboxMutationStage.Completed)
            .OrderBy(mutation => mutation.RecordedAt)
            .ThenBy(mutation => mutation.Id)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return
        [
            .. entities.Select(entity => new OutstandingMailboxMutation(
                MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolder),
                MailFolderEntityResolver.ToResolution(entity.MailFolder))),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxMutationLifecycleCount>> ReadLifecycleCountsAsync(
        MailAccountId accountId,
        CancellationToken cancellationToken)
    {
        var accountValue = accountId.Value;

        // Grouped by the stored stage rather than by the lifecycle, because the lifecycle is derived by a domain method
        // the provider cannot translate. Collapsing the three converging stages happens once the rows are here, over an
        // answer that is at most one row per mutation per stage.
        var groupedStages = await readContext.MailboxMutations
            .AsNoTracking()
            .Where(mutation => mutation.MailboxAccountId == accountValue &&
                mutation.Stage != MailboxMutationStage.Completed)
            .GroupBy(mutation => new { mutation.Mutation, mutation.Stage })
            .Select(group => new
            {
                group.Key.Mutation,
                group.Key.Stage,
                Count = group.Count(),
                OldestRecordedAt = group.Min(mutation => mutation.RecordedAt),
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. groupedStages
                .Select(group => new
                {
                    Mutation = ParseMutationOrThrow(group.Mutation),
                    Lifecycle = MailboxMutationLifecycle.Of(group.Stage),
                    group.Count,
                    group.OldestRecordedAt,
                })
                .GroupBy(group => new { group.Mutation, group.Lifecycle })
                .Select(group => new MailboxMutationLifecycleCount(
                    group.Key.Mutation,
                    group.Key.Lifecycle,
                    group.Sum(stage => stage.Count),
                    group.Min(stage => stage.OldestRecordedAt))),
        ];
    }

    /// <summary>Reads a stored mutation name back, refusing one this build does not permit.</summary>
    /// <remarks>
    /// A name that no longer parses is the same defect here as it is when a whole record is rebuilt, and it is refused
    /// the same way: a count broken down by a mutation nothing performs would report work that cannot exist.
    /// </remarks>
    private static MailboxMutation ParseMutationOrThrow(string mutationName) =>
        MailboxMutation.TryParseName(mutationName, out var mutation)
            ? mutation
            : throw new InvalidOperationException(
                $"Mailbox mutation records name '{mutationName}', which is not a permitted mutation.");

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
