// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Removes one user, the mail accounts nobody else is left assigned, and everything recorded about either.</summary>
/// <remarks>
/// <para>
/// The mail graph belongs to the account rather than to the user, so an erasure is two separate removals rather than
/// one. The user's own record goes with the user — their credentials, their contacts, their preferences, their
/// sessions, and the drafts and recurring sends they authored in any account. A mailbox goes only when this user was
/// the last one assigned it, because an account somebody else still reads is that person's mail rather than this
/// one's.
/// </para>
/// <para>
/// Most of each removal is the schema's. The mail graph hangs off <c>mailbox_accounts</c>, so deleting that row takes
/// the account's folders, the mail beneath them, and everything derived from that mail — contents, search documents,
/// chunks, vectors, classifications, threads, mutations, rule executions, audited citations, and queued work —
/// without anything here naming one of them. The user's own subtree cascades from the user row in the same way.
/// </para>
/// <para>
/// What neither cascade reaches is the tables that record a mail account as a plain identifier with no foreign key
/// onto one. Those are the seam's own work, and they are enumerated from the model rather than from a list somebody
/// maintains: a table added later that names an account without keying onto one is discharged by the same walk on the
/// day it appears, instead of being remembered about after an erasure request has already been answered. Each of
/// those statements narrows on the account, which is what the account-keyed graph bought — a row recorded against an
/// account that was authorized and has never synchronized, a sealed refresh token being the one that occurs, carries
/// the account it was written for and is reached by the same statement.
/// </para>
/// <para>
/// Nothing is deleted until the transaction has established, from its own reads, that every account it is about to
/// take is one nothing can be writing to. Two writers are outside the user-row lock, and each is answered here rather
/// than assumed away. A synchronization run is stopped by the caller, which takes the accounts off the set this
/// deployment serves and holds each one's supervision scope before it opens this transaction — a lease, and therefore
/// the deployment's rather than one replica's. A job is not stopped by any of that, because the queue claims by type
/// and reads no lease of the caller's, so this transaction locks the accounts' job rows itself: a claim selects under
/// <c>FOR UPDATE SKIP LOCKED</c> and passes them by, and a claim that landed just before the lock is read back under
/// it and refuses the erasure. The set of accounts is recomputed here for the same reason, since an assignment ending
/// elsewhere can make an account solely this user's after the caller took its holds.
/// </para>
/// <para>
/// The walk then runs twice, once before the user row is deleted and once after. Deleting that row is what stops any
/// further write keyed onto it, and nothing stops a writer that only names a mail account — so the second pass is what
/// reaches the rows such a writer committed while the first was running. It is one repeat rather than a loop because
/// of the paragraph above: the writers that could still be committing have been stopped, so the second pass is a belt
/// over braces instead of the only thing standing between an erasure and a writer it never saw.
/// </para>
/// <para>
/// The contact book is reached by the cascade rather than by the walk. <c>contacts</c> and <c>contact_addresses</c>
/// record no mail account, so nothing here names either, and the cascade reaches them in two hops rather than one:
/// <c>contacts</c> keys onto the user row, and <c>contact_addresses</c> keys onto <c>contacts</c> through
/// <c>(ContactId, UserId)</c>, so the addresses go with the person and the person goes with the user — which is what
/// an erasure request owes about a book of third parties this user assembled.
/// Nothing about it is reported separately, for the same reason nothing else the cascade takes is.
/// </para>
/// </remarks>
internal static class UserAccountErasure
{
    private const string AccountIdentifierPropertyName = nameof(MailFolderEntity.MailboxAccountId);

    private const string UserPropertyName = nameof(MailDraftEntity.UserId);

    /// <summary>Erases one user, the accounts they were the last assigned user of, and everything recorded about either.</summary>
    /// <param name="session">The transaction the whole erasure runs in, so a partial one is never committed.</param>
    /// <param name="userId">The user to remove.</param>
    /// <param name="quiescedAccounts">The accounts the caller is holding every writer off, which is what an account this walk deletes has to be among.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>What was removed, or the account that stopped anything being removed at all.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> or <paramref name="quiescedAccounts" /> is <see langword="null" />.</exception>
    [RequiresIntegrationCoverage]
    public static async Task<UserErasure> EraseAsync(
        IPersistenceSession session,
        Guid userId,
        IReadOnlyList<Guid> quiescedAccounts,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(quiescedAccounts);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // The user row is held for the rest of the transaction, so two erasures of one user are serialized, and a
        // write that keys onto that row — an assignment naming it — waits on the foreign-key check until this
        // transaction ends and is then refused against a row that is gone.
        //
        // What it does not hold is a plain read. Under MVCC a row lock blocks no `SELECT`, so a run resolving this
        // user while the statements below run is handed the identifier at once; what stops that run is its own insert
        // rather than its resolution. And a writer that never touches the user row at all — an enqueue keyed to a mail
        // account it already held — is outside any lock this statement could take, which is why the walk below runs
        // twice.
        await writeContext.Database
            .SqlQueryRaw<Guid>(UserRowLockStatement(writeContext.Model), userId)
            .ToListAsync(cancellationToken);

        // An account this user shares with somebody else stays whole, so what is erased is the accounts the
        // unassignment below leaves with nobody: mail no user is served is mail the deployment holds for nobody.
        var orphanedAccounts = await writeContext.MailAccountAssignments
            .Where(assignment => assignment.UserId == userId)
            .Where(assignment => !writeContext.MailAccountAssignments
                .Any(other => other.MailAccountId == assignment.MailAccountId && other.UserId != userId))
            .Select(assignment => assignment.MailAccountId)
            .ToListAsync(cancellationToken);

        // The caller read this set before it took its holds, and an assignment ending in between narrows rather than
        // widens it: somebody else leaving a shared account makes that account solely this user's, and the row that
        // write touches is keyed to the other user, so the lock above never saw it. Deleting such an account would be
        // the one thing this whole path exists to prevent — mail removed while a replica is still synchronizing it —
        // so the recompute is the authority and an account the caller is not holding ends the erasure here, before a
        // single row has been written.
        var unheld = orphanedAccounts
            .Where(account => !quiescedAccounts.Contains(account))
            .Select(static account => (Guid?)account)
            .FirstOrDefault();

        if (unheld is { } accountNobodyHolds)
        {
            return UserErasure.Refused(accountNobodyHolds);
        }

        // Holding the supervision scope stops synchronization and nothing else: a job is claimed by type rather than
        // by account, so a row already queued for one of these accounts is claimable by any replica right up to the
        // instant the cascade takes it. Locking every one of those rows closes that, because a claim selects under
        // `FOR UPDATE SKIP LOCKED` and therefore passes a locked row by. What the lock cannot undo is a claim that
        // landed a moment before it, which is what the read after it is for.
        if (orphanedAccounts.Count > 0)
        {
            var accountTexts = orphanedAccounts.Select(static account => account.ToString("D")).ToArray();

            await writeContext.Database
                .SqlQueryRaw<Guid>(AccountJobRowLockStatement(writeContext.Model), [accountTexts])
                .ToListAsync(cancellationToken);

            var claimed = await writeContext.Database
                .SqlQueryRaw<string>(
                    ClaimedAccountJobStatement(writeContext.Model),
                    [accountTexts, nameof(JobState.Claimed)])
                .ToListAsync(cancellationToken);

            var busy = orphanedAccounts
                .Where(account => claimed.Contains(account.ToString("D"), StringComparer.Ordinal))
                .Select(static account => (Guid?)account)
                .FirstOrDefault();

            if (busy is { } accountBeingWrittenTo)
            {
                return UserErasure.Refused(accountBeingWrittenTo);
            }
        }

        // Read before anything is deleted, because everything below reaches the payload rows by cascade and a cascade
        // removes the only pointer to an object without any application code seeing it. An erasure answered to a data
        // subject has to be true of the bucket as well as of the database, so this is the one deletion path that may
        // not leave its objects to the sweep.
        await ReleasedContentObjects.ReleaseForUserAsync(session, userId, cancellationToken);

        var rowsErasedBesideTheCascade = await EraseOrphanedAccountsAsync(
            session,
            orphanedAccounts,
            cancellationToken);

        if (orphanedAccounts.Count > 0)
        {
            await writeContext.MailAccountRecords
                .Where(account => orphanedAccounts.Contains(account.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        rowsErasedBesideTheCascade += await EraseAuthoredRowsAsync(writeContext, userId, cancellationToken);

        var erasedUsers = await writeContext.UserAccounts
            .Where(user => user.Id == userId)
            .ExecuteDeleteAsync(cancellationToken);

        // Both walks again, after the user row is gone. Everything keyed onto that row is refused from here on, so
        // what the second pass reaches is what a writer committed while the first was running — a row naming this
        // user, and a row naming one of the accounts that went with them, which no lock taken here could have
        // stopped because such a writer touches neither the user row nor the account record. Rows the transaction
        // would otherwise leave behind for a user and a mailbox it reports as erased. It is one repeat rather than a
        // loop: a writer that committed later still is one the supervision hold and the job-row lock above have
        // already stopped, which is stopping the work rather than deleting harder.
        rowsErasedBesideTheCascade += await EraseOrphanedAccountsAsync(
            session,
            orphanedAccounts,
            cancellationToken);

        rowsErasedBesideTheCascade += await EraseAuthoredRowsAsync(writeContext, userId, cancellationToken);

        return new UserErasure(erasedUsers > 0, rowsErasedBesideTheCascade, UnquiescedAccount: null);
    }

    /// <summary>Erases everything stored for one mail account.</summary>
    /// <param name="session">The transaction the erasure runs in.</param>
    /// <param name="accountId">The account, as the text every mail row names it by.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>How many rows the statements removed beside the cascade.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> is blank.</exception>
    /// <remarks>
    /// Narrowed on the account column alone, and ended by deleting the account's <c>mailbox_accounts</c> row so the
    /// cascade takes the folders and everything beneath them. No user narrows it: one mailbox is one copy of the mail
    /// however many people are assigned it, so erasing the mailbox erases that copy for all of them. The record of the
    /// account itself is the caller's to delete, because whether the account is going away at all is the caller's
    /// question rather than this walk's.
    /// </remarks>
    [RequiresIntegrationCoverage]
    public static async Task<int> EraseMailOfAccountAsync(
        IPersistenceSession session,
        string accountId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        await ReleasedContentObjects.ReleaseForMailAccountAsync(session, accountId, cancellationToken);

        var erased = 0;
        var mailboxAccounts = MailboxAccountEntityTypeOf(writeContext.Model);
        var tables = TablesTheCascadeDoesNotReach(writeContext.Model).Append(mailboxAccounts);

        foreach (var entityType in tables)
        {
            var accountColumn = entityType == mailboxAccounts
                ? QuotedColumn(entityType, nameof(MailboxAccountEntity.Id))
                : QuotedColumn(entityType, AccountIdentifierPropertyName);

            // Every identifier in it is one the model supplied, and the value is a parameter.
            var statement = $$"""
                DELETE FROM {{QuotedTable(entityType)}}
                WHERE {{accountColumn}} = {0}
                """;

            erased += await writeContext.Database.ExecuteSqlRawAsync(
                statement,
                [accountId],
                cancellationToken);
        }

        return erased;
    }

    /// <summary>Deletes what one user authored in one account, leaving the mailbox and everybody else's rows whole.</summary>
    /// <param name="session">The transaction the removal runs in.</param>
    /// <param name="accountId">The account, as the text every mail row names it by.</param>
    /// <param name="userId">The user whose authored rows go.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>How many rows the statements removed.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="accountId" /> is blank.</exception>
    /// <remarks>
    /// What an unassignment owes. A draft is read only by its author among the account's assigned users and a
    /// recurring send is stopped only by the person who declared it, so both become unreadable by anybody the moment
    /// that person is no longer assigned — and a standing instruction left running would go on sending from a mailbox
    /// its author cannot see. The mail itself is untouched, because it is the mailbox's rather than theirs.
    /// </remarks>
    [RequiresIntegrationCoverage]
    public static async Task<int> EraseAuthoredRowsOfAccountAsync(
        IPersistenceSession session,
        string accountId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var erased = 0;

        foreach (var entityType in TablesNamingTheirAuthor(writeContext.Model))
        {
            // Everything in it is either a parameter or an identifier the model itself supplied.
            var statement = $$"""
                DELETE FROM {{QuotedTable(entityType)}}
                WHERE {{QuotedColumn(entityType, AccountIdentifierPropertyName)}} = {0}
                  AND {{QuotedColumn(entityType, UserPropertyName)}} = {1}
                """;

            erased += await writeContext.Database.ExecuteSqlRawAsync(
                statement,
                [accountId, userId],
                cancellationToken);
        }

        return erased;
    }

    /// <summary>Names the two tables that keep their author, which an erasure takes by the user rather than by the account.</summary>
    /// <param name="model">The model the schema is generated from.</param>
    /// <returns>The entity types recording an act one person took: their drafts and their recurring sends.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="model" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Named rather than derived, and that is the point: <c>outgoing_emails</c> carries an author too and is
    /// deliberately absent, because a submitted message belongs to the mailbox like the sent mail it becomes and
    /// erasing whoever asked for it may not withdraw it from everybody else assigned that mailbox. A rule reading
    /// "every table with both columns" would take it, which is why this list is two names a reader can check against
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0014-single-tenant-multi-user-ownership-on-the-mail-account.md">ADR 0014</see>.
    /// </remarks>
    internal static IReadOnlyList<IEntityType> TablesNamingTheirAuthor(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return
        [
            PersistedSchemaNames.EntityTypeOf<MailDraftEntity>(model),
            PersistedSchemaNames.EntityTypeOf<RecurringSendEntity>(model),
        ];
    }

    /// <summary>Names the tables that record a mail account and that deleting that account's row would leave behind.</summary>
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

        var reachedFromTheAccount = CascadeClosureOf([MailboxAccountEntityTypeOf(model)]);

        IEntityType[] namingAnAccount =
        [
            .. model.GetEntityTypes()
                .Where(entityType => entityType.FindProperty(AccountIdentifierPropertyName) is not null)
                .Where(entityType => !reachedFromTheAccount.Contains(entityType))
                .OrderBy(entityType => entityType.GetTableName(), StringComparer.Ordinal),
        ];

        var reachedFromEachOther = namingAnAccount
            .SelectMany(candidate => CascadeClosureOf([candidate]).Where(reached => reached != candidate))
            .ToHashSet();

        return [.. namingAnAccount.Where(candidate => !reachedFromEachOther.Contains(candidate))];
    }

    /// <summary>Erases everything stored for each account the departing user was the last assigned to.</summary>
    /// <returns>How many rows the pass removed beside the cascade.</returns>
    private static async Task<int> EraseOrphanedAccountsAsync(
        IPersistenceSession session,
        IReadOnlyList<Guid> orphanedAccounts,
        CancellationToken cancellationToken)
    {
        var erased = 0;

        foreach (var accountId in orphanedAccounts)
        {
            erased += await EraseMailOfAccountAsync(session, accountId.ToString("D"), cancellationToken);
        }

        return erased;
    }

    /// <summary>Deletes the rows one user authored, in every account including the ones that outlive them.</summary>
    /// <returns>How many rows the pass removed.</returns>
    private static async Task<int> EraseAuthoredRowsAsync(
        MailFathomDbContext writeContext,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var erased = 0;

        foreach (var entityType in TablesNamingTheirAuthor(writeContext.Model))
        {
            // A draft is what one person is writing and a recurring send is a standing instruction one person gave, so
            // both go with that person even where the mailbox stays. Everything in the statement is either a parameter
            // or an identifier the model itself supplied.
            var statement = $$"""
                DELETE FROM {{QuotedTable(entityType)}}
                WHERE {{QuotedColumn(entityType, UserPropertyName)}} = {0}
                """;

            erased += await writeContext.Database.ExecuteSqlRawAsync(statement, [userId], cancellationToken);
        }

        return erased;
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

    /// <summary>The statement that holds one user's row for the rest of the transaction.</summary>
    private static string UserRowLockStatement(IModel model)
    {
        var userEntityType = PersistedSchemaNames.EntityTypeOf<UserAccountEntity>(model);

        var userKeyColumn = QuotedColumn(userEntityType, nameof(UserAccountEntity.Id));

        return $$"""
            SELECT {{userKeyColumn}} AS "Value" FROM {{QuotedTable(userEntityType)}}
            WHERE {{userKeyColumn}} = {0}
            FOR UPDATE
            """;
    }

    /// <summary>The statement that takes every job row of these accounts out of reach of the next claim.</summary>
    /// <remarks>
    /// Every row rather than the claimable ones, because what the lock is for is the row a claim would take next and
    /// the states a row moves between are the queue's business rather than this walk's. It waits rather than skipping,
    /// which is what makes it correct: a row another transaction is claiming right now is one this erasure has to see
    /// the outcome of, and a claim is a single short statement, so the wait is bounded by that statement.
    /// </remarks>
    private static string AccountJobRowLockStatement(IModel model)
    {
        var jobs = PersistedSchemaNames.EntityTypeOf<JobEntity>(model);

        return $$"""
            SELECT {{QuotedColumn(jobs, nameof(JobEntity.Id))}} AS "Value" FROM {{QuotedTable(jobs)}}
            WHERE {{QuotedColumn(jobs, AccountIdentifierPropertyName)}} = ANY({0})
            FOR UPDATE
            """;
    }

    /// <summary>The statement that names which of these accounts a claim still holds a job for, read under that lock.</summary>
    private static string ClaimedAccountJobStatement(IModel model)
    {
        var jobs = PersistedSchemaNames.EntityTypeOf<JobEntity>(model);

        var accountColumn = QuotedColumn(jobs, AccountIdentifierPropertyName);

        return $$"""
            SELECT DISTINCT {{accountColumn}} AS "Value" FROM {{QuotedTable(jobs)}}
            WHERE {{accountColumn}} = ANY({0})
              AND {{QuotedColumn(jobs, nameof(JobEntity.State))}} = {1}
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
