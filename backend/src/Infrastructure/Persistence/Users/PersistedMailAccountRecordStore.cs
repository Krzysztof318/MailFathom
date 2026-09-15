// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Holds mail accounts as records of their own and the assignments that serve them to users.</summary>
/// <remarks>
/// <para>
/// Every write is a short run of statements in one transaction, each conditional on the state the caller judged, so the
/// loser of two writers commits nothing it did not mean. The address is the exception that cannot be decided by a
/// condition alone: two writers claiming one address at once both see it free, and the unique index is what separates
/// them, which is why a violation of that one index is read back as a refusal rather than raised.
/// </para>
/// <para>
/// A statement that turns out not to apply after an earlier one in the same transaction did is undone by the statement
/// after it rather than by abandoning the transaction, because the retry policy commits whatever the work staged.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this store.")]
[RequiresIntegrationCoverage]
internal sealed class PersistedMailAccountRecordStore(
    MailFathomDbContext dbContext,
    OptimisticConcurrencyRetryPolicy commitPolicy,
    TimeProvider timeProvider)
    : IMailAccountRecordStore
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<MailAccountSummary>> ReadAllAsync(int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var accounts = await dbContext.MailAccountRecords
            .AsNoTracking()
            .OrderBy(account => account.CreatedAt)
            .ThenBy(account => account.Id)
            .Take(limit)
            .Select(account => new { account.Id, account.EmailAddress, account.DisplayName, account.Version })
            .ToListAsync(cancellationToken);

        var ids = accounts.Select(account => account.Id).ToArray();

        var assignments = await dbContext.MailAccountAssignments
            .AsNoTracking()
            .Where(assignment => ids.Contains(assignment.MailAccountId))
            .OrderBy(assignment => assignment.AssignedAt)
            .ToListAsync(cancellationToken);

        return
        [
            .. accounts.Select(account => new MailAccountSummary(
                account.Id,
                account.EmailAddress,
                account.DisplayName,
                account.Version,
                [
                    .. assignments
                        .Where(assignment => assignment.MailAccountId == account.Id)
                        .Select(assignment => MailUserId.Create(assignment.UserId)),
                ])),
        ];
    }

    /// <inheritdoc />
    public async Task<MailAccountHolding?> ReadAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await dbContext.MailAccountRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == accountId, cancellationToken);

        if (account is null)
        {
            return null;
        }

        var users = await dbContext.MailAccountAssignments
            .AsNoTracking()
            .Where(assignment => assignment.MailAccountId == accountId)
            .OrderBy(assignment => assignment.AssignedAt)
            .Select(assignment => assignment.UserId)
            .ToListAsync(cancellationToken);

        return new MailAccountHolding(ToRecord(account), [.. users.Select(MailUserId.Create)]);
    }

    /// <inheritdoc />
    public Task<MailAccountWrite> CreateAsync(
        MailUserId user,
        long expectedUserVersion,
        MailAccountRecord account,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        var userId = RequireNamed(user);
        var now = timeProvider.GetUtcNow();
        var normalized = MailAccountRecord.NormalizedFormOf(account.EmailAddress);

        return commitPolicy.CommitAsync(
            async (session, token) =>
            {
                var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                // The conflict clause names no target, so it covers the address index as well as the key: an address
                // another account holds inserts nothing rather than raising.
                var created = await context.Database.ExecuteSqlAsync(
                    $"""
                     INSERT INTO settings_mail_accounts
                         ("Id", "EmailAddress", "NormalizedEmailAddress", "DisplayName", "Document", "Version", "CreatedAt", "UpdatedAt")
                     VALUES ({account.Id}, {account.EmailAddress}, {normalized}, {account.DisplayName}, CAST({account.Document} AS jsonb), 1, {now}, {now})
                     ON CONFLICT DO NOTHING
                     """,
                    token);

                if (created == 0)
                {
                    return new MailAccountWrite(MailAccountWriteResult.AddressHeld, 0);
                }

                if (!await StepUserVersionAsync(context, userId, expectedUserVersion, now, token))
                {
                    await context.Database.ExecuteSqlAsync(
                        $"""DELETE FROM settings_mail_accounts WHERE "Id" = {account.Id}""",
                        token);

                    return new MailAccountWrite(MailAccountWriteResult.VersionSuperseded, 0);
                }

                await context.Database.ExecuteSqlAsync(
                    $"""
                     INSERT INTO mail_account_assignments ("UserId", "MailAccountId", "AssignedAt")
                     VALUES ({userId}, {account.Id}, {now})
                     """,
                    token);

                return new MailAccountWrite(MailAccountWriteResult.Committed, 1);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MailAccountWrite> SaveAsync(MailAccountRecord account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        var now = timeProvider.GetUtcNow();
        var normalized = MailAccountRecord.NormalizedFormOf(account.EmailAddress);

        try
        {
            return await commitPolicy.CommitAsync(
                async (session, token) =>
                {
                    var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                    var saved = await context.Database
                        .SqlQuery<long>(
                            $"""
                             UPDATE settings_mail_accounts
                             SET "EmailAddress" = {account.EmailAddress},
                                 "NormalizedEmailAddress" = {normalized},
                                 "DisplayName" = {account.DisplayName},
                                 "Document" = CAST({account.Document} AS jsonb),
                                 "Version" = "Version" + 1,
                                 "UpdatedAt" = {now}
                             WHERE "Id" = {account.Id}
                               AND "Version" = {account.Version}
                               AND ({normalized}::text IS NULL OR NOT EXISTS (
                                   SELECT 1 FROM settings_mail_accounts AS other
                                   WHERE other."NormalizedEmailAddress" = {normalized}::text AND other."Id" <> {account.Id}))
                             RETURNING "Version" AS "Value"
                             """)
                        .ToListAsync(token);

                    if (saved.Count == 0)
                    {
                        return await WhyNotSavedAsync(context, account, token);
                    }

                    await StepAssignedUsersAsync(context, account.Id, now, token);

                    return new MailAccountWrite(MailAccountWriteResult.Committed, saved[0]);
                },
                cancellationToken);
        }
        catch (Exception failure) when (ViolatesTheAddressIndex(failure))
        {
            // The race the condition above cannot see: another writer took the address in a transaction that had not
            // committed when this one read. The index is what refused it.
            return new MailAccountWrite(MailAccountWriteResult.AddressHeld, account.Version);
        }
    }

    /// <inheritdoc />
    public Task<MailAccountWrite> AssignAsync(
        Guid accountId,
        MailUserId user,
        long expectedUserVersion,
        CancellationToken cancellationToken)
    {
        var userId = RequireNamed(user);
        var now = timeProvider.GetUtcNow();

        return commitPolicy.CommitAsync(
            async (session, token) =>
            {
                var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                var assigned = await context.Database.ExecuteSqlAsync(
                    $"""
                     INSERT INTO mail_account_assignments ("UserId", "MailAccountId", "AssignedAt")
                     SELECT {userId}, {accountId}, {now}
                     WHERE EXISTS (SELECT 1 FROM settings_mail_accounts WHERE "Id" = {accountId})
                       AND EXISTS (SELECT 1 FROM settings_accounts WHERE "Id" = {userId})
                     ON CONFLICT DO NOTHING
                     """,
                    token);

                // Nothing inserted: the account or the user is gone, or the conflict clause met this user's own
                // assignment already standing. Somebody else's is not a conflict — the key is the pair — so an
                // account several people are assigned takes each of them without the others being consulted.
                if (assigned == 0)
                {
                    var alreadyAssigned = await context.MailAccountAssignments
                        .AnyAsync(row => row.MailAccountId == accountId && row.UserId == userId, token);

                    return new MailAccountWrite(
                        alreadyAssigned ? MailAccountWriteResult.NothingToChange : MailAccountWriteResult.NotFound,
                        0);
                }

                if (!await StepUserVersionAsync(context, userId, expectedUserVersion, now, token))
                {
                    await context.Database.ExecuteSqlAsync(
                        $"""DELETE FROM mail_account_assignments WHERE "UserId" = {userId} AND "MailAccountId" = {accountId}""",
                        token);

                    return new MailAccountWrite(MailAccountWriteResult.VersionSuperseded, 0);
                }

                return new MailAccountWrite(MailAccountWriteResult.Committed, 0);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<MailAccountUnassignment> UnassignAsync(
        Guid accountId,
        MailUserId user,
        CancellationToken cancellationToken)
    {
        var userId = RequireNamed(user);
        var now = timeProvider.GetUtcNow();

        return commitPolicy.CommitAsync(
            async (session, token) =>
            {
                var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                var ended = await context.Database.ExecuteSqlAsync(
                    $"""DELETE FROM mail_account_assignments WHERE "UserId" = {userId} AND "MailAccountId" = {accountId}""",
                    token);

                if (ended == 0)
                {
                    return new MailAccountUnassignment(Unassigned: false, AccountErased: false);
                }

                await context.Database.ExecuteSqlAsync(
                    $"""UPDATE settings_accounts SET "Version" = "Version" + 1, "UpdatedAt" = {now} WHERE "Id" = {userId}""",
                    token);

                var nobodyElse = !await context.MailAccountAssignments
                    .AnyAsync(row => row.MailAccountId == accountId, token);

                // The mail is the mailbox's, so an unassignment that leaves somebody else reading it erases none of it.
                // What goes either way is what this user authored there — a draft only its author reads, and a standing
                // instruction only its author gave — because neither is readable by anyone once they are not assigned.
                await UserAccountErasure.EraseAuthoredRowsOfAccountAsync(
                    session,
                    accountId.ToString("D"),
                    userId,
                    token);

                if (nobodyElse)
                {
                    await UserAccountErasure.EraseMailOfAccountAsync(session, accountId.ToString("D"), token);

                    await context.Database.ExecuteSqlAsync(
                        $"""DELETE FROM settings_mail_accounts WHERE "Id" = {accountId}""",
                        token);
                }

                return new MailAccountUnassignment(Unassigned: true, AccountErased: nobodyElse);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> EraseAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        return commitPolicy.CommitAsync(
            async (session, token) =>
            {
                var context = await EfCorePersistenceSessionAccessor.JoinAsync(session, token);

                if (!await context.MailAccountRecords.AnyAsync(account => account.Id == accountId, token))
                {
                    return false;
                }

                await StepAssignedUsersAsync(context, accountId, now, token);
                await UserAccountErasure.EraseMailOfAccountAsync(session, accountId.ToString("D"), token);

                await context.Database.ExecuteSqlAsync(
                    $"""DELETE FROM settings_mail_accounts WHERE "Id" = {accountId}""",
                    token);

                return true;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The same predicate the erasure's own transaction narrows on, asked ahead of it so the work bound to those
    /// accounts can be stopped first. Read outside any transaction, so it describes the assignment relation as it
    /// stood rather than as the erasure will leave it: an assignment written between this read and the erasure makes
    /// an account shared, and the erasure asks again under its own lock and leaves that account whole.
    /// </remarks>
    public async Task<IReadOnlyList<Guid>> ReadSolelyAssignedAsync(
        MailUserId user,
        CancellationToken cancellationToken)
    {
        var userId = RequireNamed(user);

        return await dbContext.MailAccountAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .Where(assignment => !dbContext.MailAccountAssignments
                .Any(other => other.MailAccountId == assignment.MailAccountId && other.UserId != userId))
            .Select(assignment => assignment.MailAccountId)
            .OrderBy(accountId => accountId)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Moves one user's record version where it still stands at the version a write was judged against.</summary>
    private static async Task<bool> StepUserVersionAsync(
        MailFathomDbContext context,
        Guid userId,
        long expectedVersion,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlAsync(
            $"""
             UPDATE settings_accounts SET "Version" = "Version" + 1, "UpdatedAt" = {now}
             WHERE "Id" = {userId} AND "Version" = {expectedVersion}
             """,
            cancellationToken) > 0;

    /// <summary>Moves the record version of every user an account is assigned to, so every replica composes them again.</summary>
    private static Task<int> StepAssignedUsersAsync(
        MailFathomDbContext context,
        Guid accountId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        context.Database.ExecuteSqlAsync(
            $"""
             UPDATE settings_accounts SET "Version" = "Version" + 1, "UpdatedAt" = {now}
             WHERE "Id" IN (SELECT "UserId" FROM mail_account_assignments WHERE "MailAccountId" = {accountId})
             """,
            cancellationToken);

    /// <summary>Reads back why a conditional save matched no row.</summary>
    private static async Task<MailAccountWrite> WhyNotSavedAsync(
        MailFathomDbContext context,
        MailAccountRecord account,
        CancellationToken cancellationToken)
    {
        var standing = await context.MailAccountRecords
            .AsNoTracking()
            .Where(candidate => candidate.Id == account.Id)
            .Select(candidate => (long?)candidate.Version)
            .FirstOrDefaultAsync(cancellationToken);

        return standing switch
        {
            null => new MailAccountWrite(MailAccountWriteResult.NotFound, 0),
            { } version when version != account.Version => new MailAccountWrite(MailAccountWriteResult.VersionSuperseded, version),
            { } version => new MailAccountWrite(MailAccountWriteResult.AddressHeld, version),
        };
    }

    /// <summary>Reports whether a failure is the address index refusing a second account one address.</summary>
    internal static bool ViolatesTheAddressIndex(Exception failure)
    {
        for (var current = failure; current is not null; current = current.InnerException)
        {
            if (current is PostgresException
                {
                    SqlState: PostgresErrorCodes.UniqueViolation,
                    ConstraintName: PersistenceConstraintNames.MailAccountRecordAddressUniqueIndexName,
                })
            {
                return true;
            }
        }

        return false;
    }

    private static MailAccountRecord ToRecord(MailAccountRecordEntity account) =>
        new(account.Id, account.EmailAddress, account.DisplayName, account.Document, account.Version);

    private static Guid RequireNamed(MailUserId user) =>
        user.IsSpecified
            ? user.Value
            : throw new ArgumentException("A mail account is assigned to a named user.", nameof(user));
}
