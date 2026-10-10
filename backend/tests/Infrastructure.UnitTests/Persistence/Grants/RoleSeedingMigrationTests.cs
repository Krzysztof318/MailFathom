// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Reflection;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Grants;

/// <summary>Holds the seeded roles to the rule that they are written once and edited by no later release that was not decided to.</summary>
/// <remarks>
/// A seeded role is an ordinary row an operator may have renamed, edited, or deleted, and a release that published a
/// permission and wrote it into a seeded list would widen everybody holding that role on upgrade — the drift a list
/// written out by name exists to prevent. So no migration after the one that seeds them may touch a role or its list
/// unless it is named below with the decision that admits it.
/// </remarks>
public sealed class RoleSeedingMigrationTests
{
    private const string MailUserRoleId = "01a11deb-3808-7000-8000-000000000001";

    private const string OrganizationAdministratorRoleId = "01a11deb-3808-7000-8000-000000000002";

    /// <summary>The migrations allowed to write roles after the seeding one, each named rather than admitted by a looser rule.</summary>
    /// <remarks>
    /// <see cref="HoldMailGrantsOnUsers" /> carries what each credential granted onto its user, so it creates roles of
    /// its own and assigns them — and the seeded <c>Mail user</c> — to users who held no role before it. It widens no
    /// seeded role's list and nobody who already held a role, which is the drift this rule exists to refuse.
    /// <see cref="RewriteAdministratorRoleAsAPattern" /> is the one deliberate edit of a seeded list: ADR 0012 decides
    /// that <c>Administrator</c> lists the pattern <c>*</c>, which reaches on the day it is written exactly the names
    /// it replaces, and the test below holds it to that role alone.
    /// </remarks>
    private static readonly Type[] RoleWritersAfterTheSeedingOne =
        [typeof(HoldMailGrantsOnUsers), typeof(RewriteAdministratorRoleAsAPattern)];

    [Fact]
    public void Migrations_AfterTheSeedingOne_WriteNoRoleAndNoRolesPermissions()
    {
        // Arrange
        using var context = CreateContext();
        var migrations = context.GetService<IMigrationsAssembly>();
        var seeding = migrations.Migrations.Single(migration => migration.Value == typeof(AddRolesGroupsAndRoleAssignments));

        // Act
        var writers = migrations.Migrations
            .Where(migration => string.CompareOrdinal(migration.Key, seeding.Key) > 0)
            .Where(migration => !RoleWritersAfterTheSeedingOne.Contains(migration.Value.AsType()))
            .Where(migration => migrations
                .CreateMigration(migration.Value, context.Database.ProviderName!)
                .UpOperations
                .Any(WritesARole))
            .Select(migration => migration.Key)
            .ToArray();

        // Assert
        Assert.Empty(writers);
    }

    /// <summary>The control: the seeding migration itself is recognized as writing roles, so the rule above is not passing over operations it cannot see.</summary>
    [Fact]
    public void Migrations_TheSeedingOne_IsRecognizedAsWritingRoles()
    {
        // Arrange
        using var context = CreateContext();
        var migrations = context.GetService<IMigrationsAssembly>();

        // Act
        var seeding = migrations.CreateMigration(
            typeof(AddRolesGroupsAndRoleAssignments).GetTypeInfo(),
            context.Database.ProviderName!);

        // Assert
        Assert.Contains(seeding.UpOperations, WritesARole);
    }

    /// <summary>
    /// The rewrite is admitted for one role, so it names neither of the two that stay written out by name: a
    /// <c>Mail user</c> or an <c>Organization administrator</c> turned into a pattern would widen on every later release.
    /// </summary>
    [Fact]
    public void Migrations_TheAdministratorRewrite_NamesNeitherRoleThatStaysWrittenOutByName()
    {
        // Arrange
        using var context = CreateContext();
        var migrations = context.GetService<IMigrationsAssembly>();

        // Act
        var rewrite = migrations.CreateMigration(
            typeof(RewriteAdministratorRoleAsAPattern).GetTypeInfo(),
            context.Database.ProviderName!);
        var statements = rewrite.UpOperations.Concat(rewrite.DownOperations).OfType<SqlOperation>().ToArray();

        // Assert
        Assert.Equal(2, statements.Length);
        Assert.All(statements, statement =>
        {
            Assert.Contains("01a11deb-3808-7000-8000-000000000003", statement.Sql, StringComparison.Ordinal);
            Assert.DoesNotContain(MailUserRoleId, statement.Sql, StringComparison.Ordinal);
            Assert.DoesNotContain(OrganizationAdministratorRoleId, statement.Sql, StringComparison.Ordinal);
        });
    }

    private static bool WritesARole(MigrationOperation operation) => operation switch
    {
        SqlOperation sql => sql.Sql.Contains(RolePermissionEntity.TableName, StringComparison.Ordinal)
            || sql.Sql.Contains($"INTO {RoleEntity.TableName} ", StringComparison.Ordinal)
            || sql.Sql.Contains($"UPDATE {RoleEntity.TableName} ", StringComparison.Ordinal)
            || sql.Sql.Contains($"FROM {RoleEntity.TableName} ", StringComparison.Ordinal),
        InsertDataOperation insert => IsRoleTable(insert.Table),
        UpdateDataOperation update => IsRoleTable(update.Table),
        DeleteDataOperation delete => IsRoleTable(delete.Table),
        _ => false,
    };

    private static bool IsRoleTable(string table) =>
        table is RoleEntity.TableName or RolePermissionEntity.TableName;

    private static MailFathomDbContext CreateContext() => new(
        MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null),
        PostgresTextSearchConfiguration.Default);
}
