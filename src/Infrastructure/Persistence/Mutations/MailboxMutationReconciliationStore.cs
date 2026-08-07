// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Mutations;

/// <summary>Answers, in PostgreSQL, whether an occurrence a synchronization run just met is one MailFathom created.</summary>
/// <remarks>
/// <para>
/// The reads use the scoped context because they join no transaction, and the writes use the context enlisted in the
/// caller's session, so a record only ever says a change was accounted for inside the transaction that accounted for it.
/// </para>
/// <para>
/// Both reads are asked about a whole batch or a whole window at once. A synchronization run meets far more mail than
/// MailFathom has ever moved, so the question has to cost one query per batch rather than one per message — and on an
/// installation that has never written to a mailbox, the partial indexes these queries use hold nothing at all.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailboxMutationReconciliationStore(MailFathomDbContext readContext)
    : IMailboxMutationReconciliationStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxMutationRecord>> ReadPlacementsAtAsync(
        MailAccountId accountId,
        RemoteFolderPath destinationPath,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uids);

        if (uids.Count == 0)
        {
            return [];
        }

        var accountValue = accountId.Value;
        var destinationValue = destinationPath.Value;
        var uidValidityValue = uidValidity.Value;
        string[] placingMutations = [MailboxMutation.Relocate.Name, MailboxMutation.Copy.Name];

        // Nullable so the comparison is against the column as it is stored: a record whose server named no placement
        // holds null there and must match nothing, rather than being coerced into a UID it never reported.
        uint?[] placedUids = [.. uids.Select(static uid => (uint?)uid.Value)];

        var entities = await readContext.MailboxMutations
            .AsNoTracking()
            .Include(mutation => mutation.MailFolder)
            .Where(mutation => mutation.MailboxAccountId == accountValue
                && placingMutations.Contains(mutation.Mutation)
                && mutation.Stage == MailboxMutationStage.Completed
                && mutation.PlacementObservedAt == null
                && mutation.DestinationFolderPath == destinationValue
                && mutation.PlacementUidValidity == uidValidityValue
                && placedUids.Contains(mutation.PlacementUid))
            .OrderBy(mutation => mutation.RecordedAt)
            .ThenBy(mutation => mutation.Id)
            .ToArrayAsync(cancellationToken);

        return [.. entities.Select(static entity => MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolder))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxMutationRecord>> ReadSeenStateChangesOnAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uids);

        if (uids.Count == 0)
        {
            return [];
        }

        var accountValue = accountId.Value;
        var alias = folderResolutionId.Alias.Value;
        var generation = folderResolutionId.Generation.Value;
        var uidValidityValue = uidValidity.Value;
        var setSeenName = MailboxMutation.SetSeen.Name;
        uint[] changedUids = [.. uids.Select(static uid => uid.Value)];

        // Reached through the prefix of the identity index — folder, UIDVALIDITY, UID — which is why this question needs
        // no index of its own, exactly as reading a disappearance back does not.
        var entities = await readContext.MailboxMutations
            .AsNoTracking()
            .Include(mutation => mutation.MailFolder)
            .Where(mutation => mutation.MailboxAccountId == accountValue
                && mutation.MailFolder.Alias == alias
                && mutation.MailFolder.ResolutionGeneration == generation
                && mutation.UidValidity == uidValidityValue
                && changedUids.Contains(mutation.Uid)
                && mutation.Mutation == setSeenName)
            .OrderBy(mutation => mutation.RecordedAt)
            .ThenBy(mutation => mutation.Id)
            .ToArrayAsync(cancellationToken);

        return [.. entities.Select(static entity => MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolder))];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MailboxMutationRecord>> ReadMutationsRemovingAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uids);

        if (uids.Count == 0)
        {
            return [];
        }

        var accountValue = accountId.Value;
        var alias = folderResolutionId.Alias.Value;
        var generation = folderResolutionId.Generation.Value;
        var uidValidityValue = uidValidity.Value;
        string[] removingMutations = [MailboxMutation.Relocate.Name, MailboxMutation.Delete.Name];
        uint[] sourceUids = [.. uids.Select(static uid => uid.Value)];

        var entities = await readContext.MailboxMutations
            .AsNoTracking()
            .Include(mutation => mutation.MailFolder)
            .Where(mutation => mutation.MailboxAccountId == accountValue
                && mutation.MailFolder.Alias == alias
                && mutation.MailFolder.ResolutionGeneration == generation
                && mutation.UidValidity == uidValidityValue
                && sourceUids.Contains(mutation.Uid)
                && removingMutations.Contains(mutation.Mutation))
            .OrderBy(mutation => mutation.RecordedAt)
            .ThenBy(mutation => mutation.Id)
            .ToArrayAsync(cancellationToken);

        return [.. entities.Select(static entity => MailboxMutationRecordMapping.ToRecord(entity, entity.MailFolder))];
    }

    /// <inheritdoc />
    public async Task RecordPlacementObservedAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, recordId, cancellationToken);

        entity.PlacementObservedAt ??= observedAt;

        // The row has just left the source folder, so no later window can select it there and observe the disappearance
        // this record would otherwise keep waiting for. The stage that got here is the server's own statement that the
        // source occurrence is already gone. A copy takes nothing out of its source folder, so it settles nothing there
        // and a column written for it would say a disappearance had been accounted for that never happens.
        if (entity.Mutation == MailboxMutation.Relocate.Name)
        {
            entity.SourceRemovalObservedAt ??= observedAt;
        }
    }

    /// <inheritdoc />
    public async Task RecordSourceRemovalObservedAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var entity = await RequireEntityAsync(session, recordId, cancellationToken);

        entity.SourceRemovalObservedAt ??= observedAt;
    }

    private static async Task<MailboxMutationEntity> RequireEntityAsync(
        IPersistenceSession session,
        MailboxMutationRecordId recordId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        // A primary-key lookup, so FindAsync already resolves a row this session may have loaded or inserted itself.
        return await writeContext.MailboxMutations.FindAsync([recordId.Value], cancellationToken)
            ?? throw new InvalidOperationException($"No mailbox mutation record carries the identifier {recordId}.");
    }
}
