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

namespace MailFathom.Infrastructure.UnitTests.Persistence.Grants;

/// <summary>
/// Holds the role, group, and assignment rows to the invariants that are the schema's rather than any code path's: a
/// role name and a group name unique across the deployment, an assignment naming a scope that exists and never written
/// twice, and no grant outliving what it was given to.
/// </summary>
/// <remarks>
/// The model is built in memory by the real PostgreSQL provider and no connection is opened, so what these assertions
/// state is what the schema is generated from. That a running PostgreSQL then refuses and cascades is an integration
/// question and is measured there.
/// </remarks>
public sealed class GrantModelTests
{
    [Fact]
    public void Model_ARoleName_IsUniqueAcrossTheDeployment()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = UniqueIndexOver<RoleEntity>(context, nameof(RoleEntity.Name));

        // Assert
        Assert.Equal(PersistenceConstraintNames.RoleNameUniqueIndexName, index.GetDatabaseName());
    }

    [Fact]
    public void Model_AGroupName_IsUniqueAcrossTheDeployment()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = UniqueIndexOver<UserGroupEntity>(context, nameof(UserGroupEntity.Name));

        // Assert
        Assert.Equal(PersistenceConstraintNames.UserGroupNameUniqueIndexName, index.GetDatabaseName());
    }

    /// <summary>
    /// Every row leaves three of the four principal and scope columns null, so an index treating each null as
    /// distinct would admit every assignment twice — the one at the deployment scope above all.
    /// </summary>
    [Fact]
    public void Model_TheSameRoleToTheSamePrincipalAtTheSameScope_IsHeldOnceWithNullsNotDistinct()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = UniqueIndexOver<RoleAssignmentEntity>(
            context,
            nameof(RoleAssignmentEntity.RoleId),
            nameof(RoleAssignmentEntity.PrincipalUserId),
            nameof(RoleAssignmentEntity.PrincipalGroupId),
            nameof(RoleAssignmentEntity.ScopeOrganizationId),
            nameof(RoleAssignmentEntity.ScopeUserId));

        // Assert
        Assert.Equal(PersistenceConstraintNames.RoleAssignmentUniqueIndexName, index.GetDatabaseName());
        Assert.False(index.GetAreNullsDistinct());
    }

    /// <summary>A foreign key on each side is what refuses an assignment naming a role, a principal, or a scope that does not exist.</summary>
    [Theory]
    [InlineData(nameof(RoleAssignmentEntity.RoleId), nameof(RoleEntity), PersistenceConstraintNames.RoleAssignmentRoleForeignKeyName)]
    [InlineData(nameof(RoleAssignmentEntity.PrincipalUserId), nameof(UserAccountEntity), PersistenceConstraintNames.RoleAssignmentPrincipalUserForeignKeyName)]
    [InlineData(nameof(RoleAssignmentEntity.PrincipalGroupId), nameof(UserGroupEntity), PersistenceConstraintNames.RoleAssignmentPrincipalGroupForeignKeyName)]
    [InlineData(nameof(RoleAssignmentEntity.ScopeOrganizationId), nameof(OrganizationEntity), PersistenceConstraintNames.RoleAssignmentScopeOrganizationForeignKeyName)]
    [InlineData(nameof(RoleAssignmentEntity.ScopeUserId), nameof(UserAccountEntity), PersistenceConstraintNames.RoleAssignmentScopeUserForeignKeyName)]
    public void Model_EachThingAnAssignmentNames_IsAForeignKeyTheStoreReadsByName(
        string column,
        string principalEntity,
        string constraintName)
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = ForeignKeyOn<RoleAssignmentEntity>(context, column);

        // Assert
        Assert.Equal(principalEntity, foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal(constraintName, foreignKey.GetConstraintName());
    }

    /// <summary>Removing a user, a group, or an organization takes every assignment naming it with it, so no grant outlives what it was given to or what it reached.</summary>
    [Theory]
    [InlineData(nameof(RoleAssignmentEntity.PrincipalUserId))]
    [InlineData(nameof(RoleAssignmentEntity.PrincipalGroupId))]
    [InlineData(nameof(RoleAssignmentEntity.ScopeOrganizationId))]
    [InlineData(nameof(RoleAssignmentEntity.ScopeUserId))]
    public void Model_RemovingWhatAnAssignmentNames_RemovesTheAssignment(string column)
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = ForeignKeyOn<RoleAssignmentEntity>(context, column);

        // Assert
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>Removing a role still assigned would revoke it from everybody holding it as a side effect, so the database refuses it as the store does.</summary>
    [Fact]
    public void Model_RemovingAnAssignedRole_IsRestricted()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = ForeignKeyOn<RoleAssignmentEntity>(context, nameof(RoleAssignmentEntity.RoleId));

        // Assert
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Theory]
    [InlineData(nameof(UserGroupMemberEntity.GroupId), nameof(UserGroupEntity))]
    [InlineData(nameof(UserGroupMemberEntity.UserId), nameof(UserAccountEntity))]
    public void Model_RemovingAGroupOrAUser_RemovesTheMembership(string column, string principalEntity)
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = ForeignKeyOn<UserGroupMemberEntity>(context, column);

        // Assert
        Assert.Equal(principalEntity, foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>An organization is removed only once nobody belongs to it, so its groups are empty, and they go with it rather than outliving every scope that could reach them.</summary>
    [Fact]
    public void Model_RemovingAnOrganization_RemovesItsGroups()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = ForeignKeyOn<UserGroupEntity>(context, nameof(UserGroupEntity.OrganizationId));

        // Assert
        Assert.Equal(nameof(OrganizationEntity), foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Model_ARolesList_LeavesWithTheRole()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = ForeignKeyOn<RolePermissionEntity>(context, nameof(RolePermissionEntity.RoleId));

        // Assert
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>Exactly one principal and at most one scope, neither scope column meaning the deployment.</summary>
    [Theory]
    [InlineData(PersistenceConstraintNames.RoleAssignmentPrincipalCheckConstraintName, "= 1")]
    [InlineData(PersistenceConstraintNames.RoleAssignmentScopeCheckConstraintName, "<= 1")]
    public void Model_AnAssignmentsPrincipalAndScope_AreBoundedByACheckConstraint(string constraintName, string bound)
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var constraint = EntityType<RoleAssignmentEntity>(context)
            .GetCheckConstraints()
            .Single(candidate => candidate.ModelName == constraintName);

        // Assert
        Assert.StartsWith("num_nonnulls(", constraint.Sql, StringComparison.Ordinal);
        Assert.EndsWith(bound, constraint.Sql, StringComparison.Ordinal);
    }

    private static IIndex UniqueIndexOver<TEntity>(MailFathomDbContext context, params string[] columns) =>
        EntityType<TEntity>(context)
            .GetIndexes()
            .Single(index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(columns));

    private static IForeignKey ForeignKeyOn<TEntity>(MailFathomDbContext context, string column) =>
        EntityType<TEntity>(context)
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Single().Name == column);

    /// <summary>Reads the design-time model, because the read-optimized one keeps neither check constraints nor whether an index treats nulls as distinct.</summary>
    private static IEntityType EntityType<TEntity>(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity))
        ?? throw new InvalidOperationException($"The model does not map {typeof(TEntity).Name}.");

    private static MailFathomDbContext CreateContext() => new(
        MailFathomDbContextDesignTimeFactory.BuildOptions(
            orchestratedConnectionString: null,
            designTimeConnectionString: null),
        PostgresTextSearchConfiguration.Default);
}
