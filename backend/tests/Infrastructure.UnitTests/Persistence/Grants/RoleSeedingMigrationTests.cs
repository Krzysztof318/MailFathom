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

/// <summary>Holds the seeded roles to the rule that they are written once and edited by no later release.</summary>
/// <remarks>
/// A seeded role is an ordinary row an operator may have renamed, edited, or deleted, and a release that published a
/// permission and wrote it into a seeded list would widen everybody holding that role on upgrade — the drift an
/// explicit list exists to prevent. So no migration after the one that seeds them may touch a role or its list.
/// </remarks>
public sealed class RoleSeedingMigrationTests
{
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
