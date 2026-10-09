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
/// explicit list exists to prevent. So no migration after the one that seeds them may write a role table in a
/// statement naming a seeded role. A later migration may still write roles of its own, as the one carrying credential
/// grants onto users does, because a role it creates is one nobody held before it.
/// </remarks>
public sealed class RoleSeedingMigrationTests
{
    private static readonly string[] SeededRoleIdentities =
    [
        "01a11deb-3808-7000-8000-000000000001",
        "01a11deb-3808-7000-8000-000000000002",
        "01a11deb-3808-7000-8000-000000000003",
        "'Mail user'",
        "'Organization administrator'",
        "'Administrator'",
    ];

    [Fact]
    public void Migrations_AfterTheSeedingOne_WriteNoSeededRole()
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
                .Any(WritesASeededRole))
            .Select(migration => migration.Key)
            .ToArray();

        // Assert
        Assert.Empty(writers);
    }

    /// <summary>The control: the seeding migration itself is recognized as writing seeded roles, so the rule above is not passing over operations it cannot see.</summary>
    [Fact]
    public void Migrations_TheSeedingOne_IsRecognizedAsWritingSeededRoles()
    {
        // Arrange
        using var context = CreateContext();
        var migrations = context.GetService<IMigrationsAssembly>();

        // Act
        var seeding = migrations.CreateMigration(
            typeof(AddRolesGroupsAndRoleAssignments).GetTypeInfo(),
            context.Database.ProviderName!);

        // Assert
        Assert.Contains(seeding.UpOperations, WritesASeededRole);
    }

    private static bool WritesASeededRole(MigrationOperation operation) => operation switch
    {
        SqlOperation sql => WritesARole(sql)
            && SeededRoleIdentities.Any(identity => sql.Sql.Contains(identity, StringComparison.Ordinal)),
        _ => WritesARole(operation),
    };

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
