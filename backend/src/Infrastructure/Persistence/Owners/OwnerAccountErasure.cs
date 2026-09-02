// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MailFathom.Infrastructure.Persistence.Owners;

/// <summary>Removes one owner, their mail accounts, and everything recorded about either.</summary>
/// <remarks>
/// <para>
/// Most of the work is the schema's. The mail graph hangs off <c>mailbox_accounts</c>, which cascades from the owner,
/// so deleting the owner row takes their accounts, their folders, the mail beneath those folders, and everything
/// derived from that mail — contents, search documents, chunks, vectors, classifications, threads, mutations, rule
/// executions, audited citations, and queued work — without anything here naming one of them.
/// </para>
/// <para>
/// What the schema does not reach is the tables that record a mail account as a plain identifier with no foreign key
/// onto one. Those are the seam's own work, and they are enumerated from the model rather than from a list somebody
/// maintains: a table added later that names an account without keying onto one is discharged by the same walk on the
/// day it appears, instead of being remembered about after an erasure request has already been answered. Each
/// statement names the owner on the row itself rather than the accounts that owner holds, which is what the owner
/// column beside every account reference bought: a row recorded against an account that was authorized and has never
/// synchronized — a sealed refresh token is the one that occurs — carries the owner it was written for and is reached
/// by the same statement, where a subquery over the account table would have found no account row and left it behind.
/// </para>
/// <para>
/// The walk runs twice, once before the owner row is deleted and once after. Deleting that row is what stops any
/// further write keyed onto it, and nothing stops a writer that only names a mail account — so the second pass is what
/// reaches the rows such a writer committed while the first was running. What it does not reach is one committed after
/// it, which no statement here can bound: that is quiescing the owner's synchronization and job work, and it belongs
/// with whatever comes to own the running deployment's reconfiguration.
/// </para>
/// <para>
/// The contact book is reached by the cascade rather than by the walk. <c>contacts</c> and <c>contact_addresses</c>
/// record no mail account, so nothing here names either, and the cascade reaches them in two hops rather than one:
/// <c>contacts</c> keys onto the owner row, and <c>contact_addresses</c> keys onto <c>contacts</c> through
/// <c>(ContactId, OwnerId)</c>, so the addresses go with the person and the person goes with the owner — which is what
/// an erasure request owes about a book of third parties this owner assembled.
/// Nothing about it is reported separately, for the same reason nothing else the cascade takes is.
/// </para>
/// </remarks>
internal static class OwnerAccountErasure
{
    private const string AccountIdentifierPropertyName = nameof(MailFolderEntity.MailboxAccountId);

    private const string OwnerPropertyName = nameof(MailFolderEntity.OwnerId);

    /// <summary>Erases one owner, their mail accounts, and everything recorded about either.</summary>
    /// <param name="session">The transaction the whole erasure runs in, so a partial one is never committed.</param>
    /// <param name="ownerId">The owner to remove.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>What was removed, and whether an owner record was there to remove at all.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    [RequiresIntegrationCoverage]
    public static async Task<OwnerErasure> EraseAsync(
        IPersistenceSession session,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // Read before anything is deleted, because everything below reaches the payload rows by cascade and a cascade
        // removes the only pointer to an object without any application code seeing it. An erasure answered to a data
        // subject has to be true of the bucket as well as of the database, so this is the one deletion path that may
        // not leave its objects to the sweep.
        await ReleasedContentObjects.ReleaseForOwnerAsync(session, ownerId, cancellationToken);

        // The owner row is held for the rest of the transaction, so two erasures of one owner are serialized, and a
        // write that keys onto that row — the account insert a first folder binding makes — waits on the foreign-key
        // check until this transaction ends and is then refused against a row that is gone.
        //
        // What it does not hold is a plain read. Under MVCC a row lock blocks no `SELECT`, so a run resolving this
        // owner while the statements below run is handed the identifier at once; what stops that run is its own insert
        // rather than its resolution. And a writer that never touches the owner row at all — an enqueue keyed to a mail
        // account it already held — is outside any lock this statement could take, which is why the walk below runs
        // twice.
        await writeContext.Database
            .SqlQueryRaw<Guid>(OwnerRowLockStatement(writeContext.Model), ownerId)
            .ToListAsync(cancellationToken);

        var tables = TablesTheCascadeDoesNotReach(writeContext.Model);

        var rowsErasedBesideTheCascade = await EraseTablesTheCascadeMissesAsync(
            writeContext,
            tables,
            ownerId,
            cancellationToken);

        var erasedOwners = await writeContext.OwnerAccounts
            .Where(owner => owner.Id == ownerId)
            .ExecuteDeleteAsync(cancellationToken);

        // The same walk again, after the owner row is gone. Everything keyed onto that row is refused from here on, so
        // what the second pass reaches is what a writer holding a mail account of its own committed while the first
        // pass was running — rows the transaction would otherwise leave behind for an owner it reports as erased. It
        // is one repeat rather than a loop: what remains after it is a writer that committed later still, and stopping
        // that is quiescing the owner's synchronization and job work rather than deleting harder.
        rowsErasedBesideTheCascade += await EraseTablesTheCascadeMissesAsync(
            writeContext,
            tables,
            ownerId,
            cancellationToken);

        return new OwnerErasure(erasedOwners > 0, rowsErasedBesideTheCascade);
    }

    /// <summary>Deletes one owner's rows from every table the cascade leaves behind.</summary>
    /// <returns>How many rows the pass removed.</returns>
    private static async Task<int> EraseTablesTheCascadeMissesAsync(
        MailFathomDbContext writeContext,
        IReadOnlyList<IEntityType> tables,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var erased = 0;

        foreach (var entityType in tables)
        {
            // The owner is named on the row rather than through the accounts it holds, so the statement reaches every
            // row written for this owner including one whose account the deployment no longer has a row for. The owner
            // leads the index each of these tables carries, which is the same order the reads narrow in. Everything in
            // it is either a parameter or an identifier the model itself supplied.
            var statement = $$"""
                DELETE FROM {{QuotedTable(entityType)}}
                WHERE {{QuotedColumn(entityType, OwnerPropertyName)}} = {0}
                """;

            erased += await writeContext.Database.ExecuteSqlRawAsync(statement, [ownerId], cancellationToken);
        }

        return erased;
    }

    /// <summary>Names the tables that record a mail account and that deleting the owner would leave behind.</summary>
    /// <param name="model">The model the schema is generated from.</param>
    /// <returns>
    /// The entity types the erasure has to take itself: each names a mail account, none is reached by a cascade from
    /// <c>mailbox_accounts</c>, and none is reached by a cascade from another of them either — so the list is the
    /// smallest set of statements that discharges all of them.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Derived from the model because that is the one description of the schema that cannot be out of date with it. The
    /// unit test over this walk is what keeps the answer readable: it states the tables by name, so a table entering or
    /// leaving the list is a diff somebody reviews rather than a silent change in what an erasure reaches.
    /// </remarks>
    internal static IReadOnlyList<IEntityType> TablesTheCascadeDoesNotReach(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var reachedFromTheOwner = CascadeClosureOf([MailboxAccountEntityTypeOf(model)]);

        IEntityType[] namingAnAccount =
        [
            .. model.GetEntityTypes()
                .Where(entityType => entityType.FindProperty(AccountIdentifierPropertyName) is not null)
                .Where(entityType => !reachedFromTheOwner.Contains(entityType))
                .OrderBy(entityType => entityType.GetTableName(), StringComparer.Ordinal),
        ];

        var reachedFromEachOther = namingAnAccount
            .SelectMany(candidate => CascadeClosureOf([candidate]).Where(reached => reached != candidate))
            .ToHashSet();

        return [.. namingAnAccount.Where(candidate => !reachedFromEachOther.Contains(candidate))];
    }

    /// <summary>Walks the tables a delete of <paramref name="roots" /> reaches through cascading foreign keys.</summary>
    private static HashSet<IEntityType> CascadeClosureOf(IEnumerable<IEntityType> roots)
    {
        var reached = new HashSet<IEntityType>(roots);
        var pending = new Queue<IEntityType>(reached);

        // A walk over a graph rather than work over a sequence, which is why it is a loop: each table reached opens
        // the tables that cascade from it, and a cycle would otherwise be walked forever.
        while (pending.TryDequeue(out var principal))
        {
            var dependents = principal.GetReferencingForeignKeys()
                .Where(foreignKey => foreignKey.DeleteBehavior == DeleteBehavior.Cascade)
                .Select(foreignKey => foreignKey.DeclaringEntityType);

            foreach (var dependent in dependents)
            {
                if (reached.Add(dependent))
                {
                    pending.Enqueue(dependent);
                }
            }
        }

        return reached;
    }

    /// <summary>The statement that holds one owner's row for the rest of the transaction.</summary>
    private static string OwnerRowLockStatement(IModel model)
    {
        var ownerEntityType = PersistedSchemaNames.EntityTypeOf<OwnerAccountEntity>(model);

        var ownerKeyColumn = QuotedColumn(ownerEntityType, nameof(OwnerAccountEntity.Id));

        return $$"""
            SELECT {{ownerKeyColumn}} AS "Value" FROM {{QuotedTable(ownerEntityType)}}
            WHERE {{ownerKeyColumn}} = {0}
            FOR UPDATE
            """;
    }

    private static IEntityType MailboxAccountEntityTypeOf(IModel model) =>
        PersistedSchemaNames.EntityTypeOf<MailboxAccountEntity>(model);

    /// <summary>Quotes a table name the model states, which is where every identifier in the statement comes from.</summary>
    private static string QuotedTable(IEntityType entityType) => PersistedSchemaNames.QuotedTable(entityType);

    /// <summary>Quotes the column one mapped property is stored in.</summary>
    private static string QuotedColumn(IEntityType entityType, string propertyName) =>
        PersistedSchemaNames.QuotedColumn(entityType, propertyName);
}
