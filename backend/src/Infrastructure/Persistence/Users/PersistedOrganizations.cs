// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.Application.Paging;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Keeps the organizations a deployment groups its users and mail accounts into, and moves either between them.</summary>
/// <remarks>
/// <para>
/// Every write is a single statement or one short transaction executed against the database, for the reason
/// <see cref="PersistedUserCredentials" /> gives: these acts are reached on paths that hold no persistence session. Each
/// refusal an administrator can meet is decided by a constraint at the moment of the write — the short name's unique
/// index, the membership's restricting foreign key, the credential lookup's unique index — and a read afterwards only says
/// which one it was.
/// </para>
/// <para>
/// A move locks the user row before it touches their credentials, which is the order
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0033-where-a-signed-in-session-lives-so-every-replica-accepts-it.md">ADR 0033</see>
/// requires of every path, and it is the same lock provisioning a credential takes — so a password provisioned while the
/// user moves lands in exactly one of the two scopes.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedOrganizations(MailFathomDbContext dbContext) : IOrganizationStore
{
    /// <inheritdoc />
    /// <remarks>
    /// The short name is read rather than asserted. Every route that writes one judges it first, so a row this build
    /// will not read was written by an older one, edited in the database, or restored from a backup — and asserting it
    /// here would turn that one row into an unreadable listing, which is the listing an operator repairs it from. It is
    /// named apart instead, and its members lose exactly what the organization decides: the prefix their login begins
    /// with, and nothing of anybody else's.
    /// </remarks>
    public async Task<OrganizationListing> ReadAsync(
        AdministrativeListingQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var organizations = dbContext.Organizations.AsNoTracking();

        // Compared by PostgreSQL as a `uuid`, which is the order the primary key's index holds and the order the page
        // is taken in, so the boundary never has to agree with how the CLR compares two `Guid` values.
        if (query.After is { } after)
        {
            organizations = organizations.Where(organization => organization.Id > after);
        }

        var stored = await organizations
            .OrderBy(organization => organization.Id)
            .Take(query.PageSize + 1)
            .Select(organization => new StoredOrganizationRow(
                organization.Id,
                organization.DisplayName,
                organization.ShortName,
                dbContext.UserAccounts.Count(user => user.OrganizationId == organization.Id),
                dbContext.MailAccountRecords.Count(account => account.OrganizationId == organization.Id),
                organization.CreatedAt))
            .ToArrayAsync(cancellationToken);

        return ListingOf(query.PageOf(stored, row => row.Id));
    }

    /// <summary>Splits the rows of one page into the ones this build serves and the ones it will not.</summary>
    /// <param name="page">The page of rows as the statement selected them.</param>
    /// <returns>The listing.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    /// <remarks>Composed apart from the statement so that an unreadable row costing exactly itself is assertable without a server.</remarks>
    internal static OrganizationListing ListingOf(AdministrativeListingPage<StoredOrganizationRow> page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var read = page.Entries
            .Select(organization => (Row: organization, Readable: OrganizationShortName.TryCreate(
                organization.ShortName,
                out var shortName), ShortName: shortName))
            .ToArray();

        return new OrganizationListing(
            [
                .. read
                    .Where(entry => entry.Readable)
                    .Select(entry => new Organization(
                        entry.Row.Id,
                        entry.Row.DisplayName,
                        entry.ShortName,
                        entry.Row.Members,
                        entry.Row.MailAccounts,
                        entry.Row.CreatedAt)),
            ],
            [
                .. read
                    .Where(entry => !entry.Readable)
                    .Select(entry => new UnreadableOrganization(
                        entry.Row.Id,
                        entry.Row.DisplayName,
                        OrganizationShortName.DescribeAcceptedForm())),
            ],
            page.ContinuesAfter);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each short name is judged by <see cref="OrganizationShortName" /> rather than by a predicate in SQL, so what this
    /// reports is exactly what the listing and a sign-in refuse and the rule is written once. The rows are streamed and
    /// judged one at a time, so what the read holds in memory is the unreadable rows rather than the table.
    /// </remarks>
    public async Task<IReadOnlyList<UnreadableOrganization>> ReadUnreadableAsync(CancellationToken cancellationToken)
    {
        // ponytail: still reads every organization's short name over the wire; a SQL predicate mirroring
        // OrganizationShortName's accepted form is the upgrade if this scan is measured slow.
        var stored = dbContext.Organizations
            .AsNoTracking()
            .OrderBy(organization => organization.Id)
            .Select(organization => new { organization.Id, organization.DisplayName, organization.ShortName })
            .AsAsyncEnumerable();

        List<UnreadableOrganization> unreadable = [];

        await foreach (var organization in stored.WithCancellation(cancellationToken))
        {
            if (!OrganizationShortName.TryCreate(organization.ShortName, out _))
            {
                unreadable.Add(new UnreadableOrganization(
                    organization.Id,
                    organization.DisplayName,
                    OrganizationShortName.DescribeAcceptedForm()));
            }
        }

        return unreadable;
    }

    /// <inheritdoc />
    public async Task<OrganizationWriteResult> CreateAsync(
        Guid organizationId,
        string displayName,
        OrganizationShortName shortName,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var storedShortName = RequireShortName(shortName);

        // The identifier is freshly minted, so the short name's index is the one constraint a loser can meet.
        var written = await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO organizations ("Id", "DisplayName", "ShortName", "CreatedAt")
             VALUES ({organizationId}, {displayName}, {storedShortName}, {createdAt})
             ON CONFLICT DO NOTHING
             """,
            cancellationToken);

        return OrganizationWriteResult.Of(
            written == 1 ? OrganizationWriteOutcome.Written : OrganizationWriteOutcome.ShortNameTaken);
    }

    /// <inheritdoc />
    public async Task<OrganizationWriteResult> RenameAsync(
        Guid organizationId,
        string displayName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var written = await dbContext.Organizations
            .Where(organization => organization.Id == organizationId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(organization => organization.DisplayName, displayName),
                cancellationToken);

        return WrittenOrUnknown(written);
    }

    /// <inheritdoc />
    public async Task<OrganizationWriteResult> ChangeShortNameAsync(
        Guid organizationId,
        OrganizationShortName shortName,
        CancellationToken cancellationToken)
    {
        var storedShortName = RequireShortName(shortName);

        try
        {
            var written = await dbContext.Organizations
                .Where(organization => organization.Id == organizationId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(organization => organization.ShortName, storedShortName),
                    cancellationToken);

            return WrittenOrUnknown(written);
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return OrganizationWriteResult.Of(OrganizationWriteOutcome.ShortNameTaken);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The organization row is locked before its members and its mail accounts are counted, and the counts and the delete
    /// commit together. A move of a user or an account into it checks the restricting foreign key, which waits on that
    /// lock, and a move out that committed first is already seen by the count — so the numbers a refusal names are the
    /// numbers that refused it, and a delete that goes ahead leaves a concurrent move to meet a missing organization
    /// rather than a dangling member or account.
    /// </remarks>
    public async Task<OrganizationWriteResult> DeleteAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        await using var removal = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM organizations WHERE "Id" = {organizationId} FOR UPDATE""",
            cancellationToken);

        if (!await dbContext.Organizations.AnyAsync(organization => organization.Id == organizationId, cancellationToken))
        {
            return OrganizationWriteResult.Of(OrganizationWriteOutcome.UnknownOrganization);
        }

        var members = await dbContext.UserAccounts
            .CountAsync(user => user.OrganizationId == organizationId, cancellationToken);

        var mailAccounts = await dbContext.MailAccountRecords
            .CountAsync(account => account.OrganizationId == organizationId, cancellationToken);

        if (members > 0 || mailAccounts > 0)
        {
            return new OrganizationWriteResult(
                members > 0 ? OrganizationWriteOutcome.StillHasMembers : OrganizationWriteOutcome.StillHoldsMailAccounts,
                organizationId,
                RemainingMembers: members,
                RemainingMailAccounts: mailAccounts);
        }

        await dbContext.Organizations
            .Where(organization => organization.Id == organizationId)
            .ExecuteDeleteAsync(cancellationToken);

        await removal.CommitAsync(cancellationToken);

        return OrganizationWriteResult.Of(OrganizationWriteOutcome.Written);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The accounts the user is assigned are counted under the user's own lock, which an assignment to them takes as
    /// well, so an assignment either committed first and is counted or waits for this move and then reads the user's
    /// new organization. The collision is read under the same lock and named before anything is written, so an
    /// administrator is told which username stands in the way. A credential provisioned in the target scope for somebody else in the same instant
    /// is refused by the lookup index instead, and is answered as the same refusal without the name.
    /// </remarks>
    public async Task<OrganizationWriteResult> SetUserOrganizationAsync(
        UserId user,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        if (!user.IsSpecified)
        {
            throw new ArgumentException("A user is moved between organizations by name.", nameof(user));
        }

        var userId = user.Value;
        var password = UserCredentialMethod.Password.Name;

        await using var move = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM settings_accounts WHERE "Id" = {userId} FOR UPDATE""",
            cancellationToken);

        if (!await dbContext.UserAccounts.AnyAsync(record => record.Id == userId, cancellationToken))
        {
            return OrganizationWriteResult.Of(OrganizationWriteOutcome.UnknownUser);
        }

        if (organizationId is { } target
            && !await dbContext.Organizations.AnyAsync(organization => organization.Id == target, cancellationToken))
        {
            return OrganizationWriteResult.Of(OrganizationWriteOutcome.UnknownOrganization);
        }

        var straddling = await AccountsAssignedOutside(dbContext, userId, organizationId).CountAsync(cancellationToken);

        if (straddling > 0)
        {
            return new OrganizationWriteResult(
                OrganizationWriteOutcome.AssignmentsOutsideOrganization,
                StandingAssignments: straddling);
        }

        if (await CollidingUsernames(dbContext, userId, organizationId).FirstOrDefaultAsync(cancellationToken) is { } colliding)
        {
            return new OrganizationWriteResult(OrganizationWriteOutcome.UsernameTaken, CollidingUsername: colliding);
        }

        try
        {
            await dbContext.UserAccounts
                .Where(record => record.Id == userId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(record => record.OrganizationId, organizationId),
                    cancellationToken);

            await dbContext.UserCredentials
                .Where(credential => credential.UserId == userId && credential.Method == password)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(credential => credential.OrganizationId, organizationId)
                        .SetProperty(credential => credential.Version, credential => credential.Version + 1),
                    cancellationToken);

            await move.CommitAsync(cancellationToken);
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return OrganizationWriteResult.Of(OrganizationWriteOutcome.UsernameTaken);
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return OrganizationWriteResult.Of(OrganizationWriteOutcome.UnknownOrganization);
        }

        return new OrganizationWriteResult(OrganizationWriteOutcome.Written, organizationId ?? Guid.Empty);
    }

    /// <summary>Composes the usernames of one user's passwords that another user already holds in the target scope.</summary>
    /// <param name="dbContext">The context the query is composed over.</param>
    /// <param name="userId">The user being moved.</param>
    /// <param name="organizationId">The organization they are moving into, or <see langword="null" /> for none.</param>
    /// <returns>The colliding usernames, in order.</returns>
    /// <remarks>Composed apart so its translation — above all the comparison against a scope that may be none — is assertable without a server.</remarks>
    internal static IQueryable<string> CollidingUsernames(
        MailFathomDbContext dbContext,
        Guid userId,
        Guid? organizationId)
    {
        var password = UserCredentialMethod.Password.Name;

        return dbContext.UserCredentials
            .Where(credential => credential.UserId == userId
                && credential.Method == password
                && dbContext.UserCredentials.Any(held => held.Method == password
                    && held.Lookup == credential.Lookup
                    && held.UserId != userId
                    && held.OrganizationId == organizationId))
            .OrderBy(credential => credential.Lookup)
            .Select(credential => credential.Lookup);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The account row is locked before its users are counted, against the shared lock an assignment of it takes, so
    /// an assignment either committed first and is counted or waits for this move and then reads the account's new
    /// organization. A move of one of its users concurrently with this one cannot let both through: each compares the
    /// other's side as it stood, and while the two agree before either move, a move of one side alone always leaves them
    /// differing. The organization's restricting foreign key waits on a deletion that locked it first, and is answered as
    /// the organization being gone.
    /// </remarks>
    public async Task<OrganizationWriteResult> SetMailAccountOrganizationAsync(
        Guid mailAccountId,
        Guid? organizationId,
        CancellationToken cancellationToken)
    {
        await using var move = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        await dbContext.Database.ExecuteSqlAsync(
            $"""SELECT 1 FROM settings_mail_accounts WHERE "Id" = {mailAccountId} FOR UPDATE""",
            cancellationToken);

        if (!await dbContext.MailAccountRecords.AnyAsync(account => account.Id == mailAccountId, cancellationToken))
        {
            return OrganizationWriteResult.Of(OrganizationWriteOutcome.UnknownMailAccount);
        }

        if (organizationId is { } target
            && !await dbContext.Organizations.AnyAsync(organization => organization.Id == target, cancellationToken))
        {
            return OrganizationWriteResult.Of(OrganizationWriteOutcome.UnknownOrganization);
        }

        var straddling = await UsersAssignedOutside(dbContext, mailAccountId, organizationId).CountAsync(cancellationToken);

        if (straddling > 0)
        {
            return new OrganizationWriteResult(
                OrganizationWriteOutcome.AssignmentsOutsideOrganization,
                StandingAssignments: straddling);
        }

        try
        {
            await dbContext.MailAccountRecords
                .Where(account => account.Id == mailAccountId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(account => account.OrganizationId, organizationId),
                    cancellationToken);

            await move.CommitAsync(cancellationToken);
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            return OrganizationWriteResult.Of(OrganizationWriteOutcome.UnknownOrganization);
        }

        return new OrganizationWriteResult(OrganizationWriteOutcome.Written, organizationId ?? Guid.Empty);
    }

    /// <summary>Composes the accounts one user is assigned that a move into the target organization would leave outside it.</summary>
    /// <param name="dbContext">The context the query is composed over.</param>
    /// <param name="userId">The user being moved.</param>
    /// <param name="organizationId">The organization they are moving into, or <see langword="null" /> for none.</param>
    /// <returns>The identifiers of those accounts.</returns>
    /// <remarks>Composed apart so its translation — above all that an account in no organization stands outside every organization, and only outside one — is assertable without a server.</remarks>
    internal static IQueryable<Guid> AccountsAssignedOutside(
        MailFathomDbContext dbContext,
        Guid userId,
        Guid? organizationId) =>
        dbContext.MailAccountRecords
            .Where(account => account.OrganizationId != organizationId
                && dbContext.MailAccountAssignments.Any(assignment => assignment.MailAccountId == account.Id
                    && assignment.UserId == userId))
            .Select(account => account.Id);

    /// <summary>Composes the users one account is assigned to that a move into the target organization would leave outside it.</summary>
    /// <param name="dbContext">The context the query is composed over.</param>
    /// <param name="mailAccountId">The account being moved.</param>
    /// <param name="organizationId">The organization it is moving into, or <see langword="null" /> for none.</param>
    /// <returns>The identifiers of those users.</returns>
    /// <remarks>Composed apart for the reason <see cref="AccountsAssignedOutside" /> is.</remarks>
    internal static IQueryable<Guid> UsersAssignedOutside(
        MailFathomDbContext dbContext,
        Guid mailAccountId,
        Guid? organizationId) =>
        dbContext.UserAccounts
            .Where(user => user.OrganizationId != organizationId
                && dbContext.MailAccountAssignments.Any(assignment => assignment.UserId == user.Id
                    && assignment.MailAccountId == mailAccountId))
            .Select(user => user.Id);

    private static OrganizationWriteResult WrittenOrUnknown(int written) => OrganizationWriteResult.Of(
        written == 1 ? OrganizationWriteOutcome.Written : OrganizationWriteOutcome.UnknownOrganization);

    private static string RequireShortName(OrganizationShortName shortName) => shortName.IsSpecified
        ? shortName.Value
        : throw new ArgumentException("An organization is named by a stated short name.", nameof(shortName));
}
