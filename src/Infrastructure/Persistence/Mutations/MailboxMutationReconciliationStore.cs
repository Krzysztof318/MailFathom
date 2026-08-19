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
using Microsoft.Extensions.Logging;

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
internal sealed partial class MailboxMutationReconciliationStore(
    MailFathomDbContext readContext,
    ILogger<MailboxMutationReconciliationStore> logger) : IMailboxMutationReconciliationStore
{
    /// <summary>The stored names of every mutation whose whole effect is a value a <c>FLAGS</c> response reports back.</summary>
    /// <remarks>
    /// Composed from the mutations themselves rather than written out, so a mutation renamed in the one place it is
    /// declared cannot leave a literal here that matches no row. It is an array because that is the shape the Npgsql
    /// provider translates into the <c>= ANY</c> the query wants; a frozen set would be evaluated in this process.
    /// </remarks>
    private static readonly string[] FlagWritingMutationNames =
        [.. MailboxMutation.FlagWriting.Select(static mutation => mutation.Name)];

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
    public async Task<IReadOnlyList<MailboxMutationRecord>> ReadFlagChangesOnAsync(
        MailAccountId accountId,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        DateTimeOffset issuedAfter,
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
        uint[] changedUids = [.. uids.Select(static uid => uid.Value)];
        var perValue = IMailboxMutationReconciliationStore.MaximumFlagChangeRecordsPerValue;

        // One row past the budget, so a value whose tail was cut is told apart from one that exactly fills it. Without
        // the extra row the count alone cannot say which happened, and the warning below would fire on a value that
        // dropped nothing. It cannot say more than that: the bound is the earliest reading across the whole window, so
        // a value's group admits stores older than its own occurrence's last reading, and every comparison the caller
        // makes rejects those. What the count establishes is that stores past the window's bound went unread, not that
        // one of them could have explained the value.
        var probed = perValue + 1;

        // Reached through the prefix of the identity index — folder, UIDVALIDITY, UID — which is why this question needs
        // no index of its own, exactly as reading a disappearance back does not. The stage is excluded here rather than
        // left to the caller because a record written down and not yet issued explains no reading, and it carries the
        // newest stage change in the table: against an occurrence a caller has just triaged, those rows would otherwise
        // fill the budget and drop the completed record that does explain what the server reported.
        var storedValues = await readContext.MailboxMutations
            .AsNoTracking()
            .Where(mutation => mutation.MailboxAccountId == accountValue
                && mutation.MailFolder.Alias == alias
                && mutation.MailFolder.ResolutionGeneration == generation
                && mutation.UidValidity == uidValidityValue
                && changedUids.Contains(mutation.Uid)
                && FlagWritingMutationNames.Contains(mutation.Mutation)
                && mutation.Stage != MailboxMutationStage.Recorded
                && mutation.StageChangedAt > issuedAfter)
            .GroupBy(mutation => new { mutation.Uid, mutation.Mutation })
            .Select(storedValue => new
            {
                // Ranked within the UID and the mutation rather than across the answer, so neither another occurrence
                // nor another value of this one can spend this value's budget. Ranked by the stage change rather than
                // by when the record was written, because that is the column the filter above and every comparison the
                // caller makes are about: a store recorded early and completed late accounts for a later reading than
                // one recorded after it and staged before it.
                Newest = storedValue
                    .OrderByDescending(mutation => mutation.StageChangedAt)
                    .ThenByDescending(mutation => mutation.Id)
                    .Take(probed)
                    .Select(mutation => new { Mutation = mutation, Folder = mutation.MailFolder }),
            })
            .ToArrayAsync(cancellationToken);

        var truncatedValueCount = storedValues.Count(storedValue => storedValue.Newest.Count() > perValue);

        if (truncatedValueCount > 0)
        {
            LogFlagChangeCeilingReached(logger, perValue, truncatedValueCount, accountValue, alias);
        }

        // Handed back oldest first, because that is the order the caller credits a value to the earliest store that
        // explains it. The ordering is restated here rather than trusted from the answer's shape, since a grouped read
        // guarantees the order within a group and nothing about the order between them.
        return
        [
            .. storedValues
                .SelectMany(storedValue => storedValue.Newest
                    .OrderByDescending(entry => entry.Mutation.StageChangedAt)
                    .ThenByDescending(entry => entry.Mutation.Id)
                    .Take(perValue))
                .OrderBy(entry => entry.Mutation.StageChangedAt)
                .ThenBy(entry => entry.Mutation.Id)
                .Select(static entry => MailboxMutationRecordMapping.ToRecord(entry.Mutation, entry.Folder)),
        ];
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

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{TruncatedValueCount} values of changed occurrences of account {AccountId} in folder {FolderAlias} "
            + "carry more stores past this window's age bound than the {Ceiling} an attribution reads for one value of "
            + "one occurrence, so any of those beyond the newest went unread — and a store issued after that "
            + "occurrence's own last reading which went unread with them would leave its value attributed to the "
            + "mailbox owner.")]
    private static partial void LogFlagChangeCeilingReached(
        ILogger logger,
        int ceiling,
        int truncatedValueCount,
        string accountId,
        string folderAlias);
}
