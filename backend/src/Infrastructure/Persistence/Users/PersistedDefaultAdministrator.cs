// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.DefaultAdministrator;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Keeps the record of the default administrator in PostgreSQL, where every replica's first start contends for one row.</summary>
/// <remarks>
/// Each act is one transaction that claims or locks the single row before it writes anything, so the replica that wins
/// writes and every other one, blocked on the same row, reads what it wrote. Nothing here is read first and decided
/// afterwards.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersistedDefaultAdministrator(MailFathomDbContext dbContext) : IDefaultAdministratorStore
{
    /// <summary>The seeded role the default administrator is given, as the migration that introduced roles wrote it.</summary>
    /// <remarks>
    /// Named by its identifier rather than its name, because the seeded rows are an operator's to rename. One deleted
    /// before the first start is not recreated: the administrator is recorded without it, and a root assigns another.
    /// </remarks>
    internal static readonly Guid AdministratorRoleId = Guid.Parse("01a11deb-3808-7000-8000-000000000003");

    /// <summary>The label the default administrator is recorded under where no other user holds it.</summary>
    internal const string DisplayName = "admin";

    /// <summary>The label it falls back to where somebody else is already called <see cref="DisplayName" />, since labels are unique.</summary>
    internal const string FallbackDisplayName = "admin (default administrator)";

    /// <summary>The record a provisioned user starts with, which names their language and nothing else.</summary>
    private const string ProvisionedRecord = """{"Language":"English"}""";

    /// <inheritdoc />
    public async Task<DefaultAdministratorRecord> RecordOnceAsync(
        UserId candidate,
        Guid assignmentId,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        if (!candidate.IsSpecified)
        {
            throw new ArgumentException("The default administrator is recorded under a named identifier.", nameof(candidate));
        }

        var userId = candidate.Value;
        var singleRowId = DefaultAdministratorEntity.SingleRowId;

        await using (var recording = await dbContext.Database.BeginTransactionAsync(cancellationToken))
        {
            // The claim. A replica starting beside this one blocks on the same key until this transaction ends, and then
            // inserts nothing, so the user below is written by exactly one start in the deployment's lifetime.
            var claimed = await dbContext.Database.ExecuteSqlAsync(
                $"""
                 INSERT INTO default_administrator ("Id", "UserId", "RecordedAt", "PasswordSettingAppliedAt")
                 VALUES ({singleRowId}, NULL, {recordedAt}, NULL)
                 ON CONFLICT DO NOTHING
                 """,
                cancellationToken);

            if (claimed == 1)
            {
                await this.WriteAdministratorAsync(userId, assignmentId, recordedAt, cancellationToken);
            }

            await recording.CommitAsync(cancellationToken);
        }

        var record = await dbContext.DefaultAdministrator
            .AsNoTracking()
            .SingleAsync(row => row.Id == singleRowId, cancellationToken);

        return new DefaultAdministratorRecord(
            record.UserId is { } recorded ? UserId.Create(recorded) : null,
            record.PasswordSettingAppliedAt is not null);
    }

    /// <inheritdoc />
    public async Task<DefaultAdministratorPasswordOutcome> ApplyPasswordSettingAsync(
        UserId administrator,
        Guid credentialId,
        UserCredentialLookup lookup,
        string passwordHash,
        IReadOnlyList<MailFathomPermission> permissions,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentNullException.ThrowIfNull(permissions);

        if (!administrator.IsSpecified || credentialId == Guid.Empty || !lookup.IsSpecified)
        {
            throw new ArgumentException("A password is applied to a named administrator under a named credential and username.");
        }

        var singleRowId = DefaultAdministratorEntity.SingleRowId;

        await using var applying = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var record = await dbContext.DefaultAdministrator
            .FromSql($"""SELECT * FROM default_administrator WHERE "Id" = {singleRowId} FOR UPDATE""")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (record is null || record.PasswordSettingAppliedAt is not null)
        {
            return DefaultAdministratorPasswordOutcome.AlreadyApplied;
        }

        if (record.UserId != administrator.Value)
        {
            return DefaultAdministratorPasswordOutcome.AdministratorRemoved;
        }

        var outcome = await this.ProvisionPasswordAsync(
            administrator.Value,
            credentialId,
            lookup.Value,
            passwordHash,
            [.. permissions.Select(permission => permission.Name)],
            appliedAt,
            cancellationToken);

        if (outcome == DefaultAdministratorPasswordOutcome.UsernameTaken)
        {
            await applying.RollbackAsync(cancellationToken);

            return outcome;
        }

        await dbContext.Database.ExecuteSqlAsync(
            $"""UPDATE default_administrator SET "PasswordSettingAppliedAt" = {appliedAt} WHERE "Id" = {singleRowId}""",
            cancellationToken);

        await applying.CommitAsync(cancellationToken);

        return outcome;
    }

    /// <summary>Writes the user, with both mail endpoint switches off, and its assignment, then names it on the claimed row.</summary>
    private async Task WriteAdministratorAsync(
        Guid userId,
        Guid assignmentId,
        DateTimeOffset recordedAt,
        CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO settings_accounts
                 ("Id", "DisplayName", "Document", "McpEndpointEnabled", "ClientEndpointEnabled", "Version", "CreatedAt", "UpdatedAt")
             VALUES (
                 {userId},
                 CASE WHEN EXISTS (SELECT 1 FROM settings_accounts WHERE "DisplayName" = {DisplayName})
                      THEN {FallbackDisplayName} ELSE {DisplayName} END,
                 CAST({ProvisionedRecord} AS jsonb), FALSE, FALSE, 1, {recordedAt}, {recordedAt})
             """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO role_assignments ("Id", "RoleId", "PrincipalUserId", "AssignedAt")
             SELECT {assignmentId}, {AdministratorRoleId}, {userId}, {recordedAt}
             WHERE EXISTS (SELECT 1 FROM roles WHERE "Id" = {AdministratorRoleId})
             """,
            cancellationToken);

        var singleRowId = DefaultAdministratorEntity.SingleRowId;

        await dbContext.Database.ExecuteSqlAsync(
            $"""UPDATE default_administrator SET "UserId" = {userId} WHERE "Id" = {singleRowId}""",
            cancellationToken);
    }

    /// <summary>Provisions the administrator's password credential, unless it already holds one.</summary>
    /// <remarks>
    /// The conflict clause names no target, so the lookup's unique index is the constraint a taken username meets: the
    /// credential is scoped to no organization because the administrator is in none.
    /// </remarks>
    private async Task<DefaultAdministratorPasswordOutcome> ProvisionPasswordAsync(
        Guid userId,
        Guid credentialId,
        string lookup,
        string passwordHash,
        string[] permissions,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken)
    {
        var passwordMethod = UserCredentialMethod.Password.Name;

        if (await dbContext.UserCredentials.AnyAsync(
                credential => credential.UserId == userId && credential.Method == passwordMethod,
                cancellationToken))
        {
            return DefaultAdministratorPasswordOutcome.AlreadyHeld;
        }

        string[] administrationOnly = [UserCredentialSurface.Administration.Name];
        string[] fromAnywhere = [];

        var written = await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO user_credentials
                 ("Id", "UserId", "Method", "OrganizationId", "Lookup", "Material", "Permissions", "Surfaces", "AllowedSourceNetworks", "Enabled", "Version", "CreatedAt", "MaterialChangedAt")
             VALUES ({credentialId}, {userId}, {passwordMethod}, NULL, {lookup}, {passwordHash}, {permissions}, {administrationOnly}, {fromAnywhere}, TRUE, 1, {appliedAt}, {appliedAt})
             ON CONFLICT DO NOTHING
             """,
            cancellationToken);

        return written == 1
            ? DefaultAdministratorPasswordOutcome.Applied
            : DefaultAdministratorPasswordOutcome.UsernameTaken;
    }
}
