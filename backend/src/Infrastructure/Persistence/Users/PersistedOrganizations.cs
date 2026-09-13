// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Organizations;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Keeps the organizations a deployment groups its users into, and moves users between them.</summary>
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
    public async Task<IReadOnlyList<Organization>> ReadAsync(CancellationToken cancellationToken)
    {
        var stored = await dbContext.Organizations
            .AsNoTracking()
            .OrderBy(organization => organization.ShortName)
            .Take(Organization.MaximumListed)
            .Select(organization => new
            {
                organization.Id,
                organization.DisplayName,
                organization.ShortName,
                Members = dbContext.UserAccounts.Count(user => user.OrganizationId == organization.Id),
                organization.CreatedAt,
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. stored.Select(organization => new Organization(
                organization.Id,
                organization.DisplayName,
                OrganizationShortName.Create(organization.ShortName),
                organization.Members,
                organization.CreatedAt)),
        ];
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
    /// The statement deletes only an organization nobody belongs to, and the restricting foreign key refuses it too when a
    /// member joined between the check and the delete; either way the count read afterwards is what the refusal names.
    /// </remarks>
    public async Task<OrganizationWriteResult> DeleteAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        try
        {
            var removed = await dbContext.Organizations
                .Where(organization => organization.Id == organizationId
                    && !dbContext.UserAccounts.Any(user => user.OrganizationId == organizationId))
                .ExecuteDeleteAsync(cancellationToken);

            if (removed == 1)
            {
                return OrganizationWriteResult.Of(OrganizationWriteOutcome.Written);
            }
        }
        catch (PostgresException violation) when (violation.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // A member joined between the existence check and the delete; the count below names them.
        }

        if (!await dbContext.Organizations
                .AsNoTracking()
                .AnyAsync(organization => organization.Id == organizationId, cancellationToken))
        {
            return OrganizationWriteResult.Of(OrganizationWriteOutcome.UnknownOrganization);
        }

        var members = await dbContext.UserAccounts
            .AsNoTracking()
            .CountAsync(user => user.OrganizationId == organizationId, cancellationToken);

        return new OrganizationWriteResult(
            OrganizationWriteOutcome.StillHasMembers,
            organizationId,
            RemainingMembers: members);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The collision is read under the user's own lock and named before anything is written, so an administrator is told
    /// which username stands in the way. A credential provisioned in the target scope for somebody else in the same instant
    /// is refused by the lookup index instead, and is answered as the same refusal without the name.
    /// </remarks>
    public async Task<OrganizationWriteResult> SetUserOrganizationAsync(
        MailUserId user,
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

    private static OrganizationWriteResult WrittenOrUnknown(int written) => OrganizationWriteResult.Of(
        written == 1 ? OrganizationWriteOutcome.Written : OrganizationWriteOutcome.UnknownOrganization);

    private static string RequireShortName(OrganizationShortName shortName) => shortName.IsSpecified
        ? shortName.Value
        : throw new ArgumentException("An organization is named by a stated short name.", nameof(shortName));
}
