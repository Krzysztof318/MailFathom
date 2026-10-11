// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Policies;

public sealed class SettingsPolicyModelTests
{
    [Fact]
    public void Model_TheSettingsPolicies_AreHeldBesideTheDocumentsTheyGovern()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var table = EntityType(context).GetTableName();

        // Assert
        Assert.Equal("settings_policies", table);
    }

    /// <summary>
    /// The deployment's row names no organization, so an index treating each null as distinct would admit a second
    /// deployment policy and leave nothing able to say which of the two governs.
    /// </summary>
    [Fact]
    public void Model_OneScope_HoldsOnePolicyWithNullsNotDistinct()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = EntityType(context)
            .GetIndexes()
            .Single(candidate => candidate.Properties.Single().Name == nameof(SettingsPolicyEntity.OrganizationId));

        // Assert
        Assert.True(index.IsUnique);
        Assert.False(index.GetAreNullsDistinct());
        Assert.Equal(PersistenceConstraintNames.SettingsPolicyScopeUniqueIndexName, index.GetDatabaseName());
    }

    /// <summary>An organization that is removed takes its policy with it, by the foreign key rather than by a walk a later reader could drop.</summary>
    [Fact]
    public void Model_RemovingAnOrganization_RemovesItsPolicy()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = EntityType(context).GetForeignKeys().Single();

        // Assert
        Assert.Equal(nameof(OrganizationEntity), foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal(nameof(SettingsPolicyEntity.OrganizationId), foreignKey.Properties.Single().Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal(PersistenceConstraintNames.SettingsPolicyOrganizationForeignKeyName, foreignKey.GetConstraintName());
    }

    [Fact]
    public void Model_ThePolicy_IsADocumentGuardedByItsOwnVersion()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var entity = EntityType(context);

        // Assert
        Assert.Equal("jsonb", entity.FindProperty(nameof(SettingsPolicyEntity.Document))!.GetColumnType());
        Assert.True(entity.FindProperty(nameof(SettingsPolicyEntity.Version))!.IsConcurrencyToken);
    }

    /// <summary>Reads the design-time model, because the read-optimized one does not keep whether an index treats nulls as distinct.</summary>
    private static IEntityType EntityType(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(SettingsPolicyEntity))
        ?? throw new InvalidOperationException($"The model does not map {nameof(SettingsPolicyEntity)}.");

    private static MailFathomDbContext CreateContext() => new(
        MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null),
        PostgresTextSearchConfiguration.Default);
}
