// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Entities;

public sealed class StoredSecretModelTests
{
    [Fact]
    public void StoredSecretModel_Owner_IsRequiredAndErasesTheSecretWithTheOwner()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var reference = Assert.Single(EntityType(context).GetForeignKeys());

        // Assert
        Assert.Equal(["OwnerId"], reference.Properties.Select(property => property.Name));
        Assert.Equal(typeof(OwnerAccountEntity), reference.PrincipalEntityType.ClrType);
        Assert.True(reference.IsRequired);
        Assert.Equal(DeleteBehavior.Cascade, reference.DeleteBehavior);
        Assert.Equal(PersistenceConstraintNames.StoredSecretOwnerForeignKeyName, reference.GetConstraintName());
    }

    [Fact]
    public void StoredSecretModel_SealedMaterial_IsRequiredAndBoundedByTheSchema()
    {
        // Arrange
        using var context = CreateContext();
        var entityType = EntityType(context);

        // Act
        var material = entityType.FindProperty("SealedMaterial");
        var bound = Assert.Single(entityType.GetCheckConstraints());

        // Assert
        Assert.NotNull(material);
        Assert.False(material.IsNullable);
        Assert.Equal("bytea", material.GetColumnType());
        Assert.Equal(PersistenceConstraintNames.StoredSecretMaterialLengthCheckConstraintName, bound.Name);
        Assert.Contains(StoredSecretEntity.MaximumSealedMaterialByteCount.ToString(), bound.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void StoredSecretModel_IndexesSupportIdentityAndKeyRetirementQueries()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var indexes = EntityType(context).GetIndexes().ToDictionary(index => index.GetDatabaseName()!);

        // Assert
        var identity = indexes[PersistenceConstraintNames.StoredSecretOwnerNameUniqueIndexName];
        Assert.Equal(["OwnerId", "Name"], identity.Properties.Select(property => property.Name));
        Assert.True(identity.IsUnique);

        var key = indexes[PersistenceConstraintNames.StoredSecretKeyIndexName];
        Assert.Equal(["DataEncryptionKeyId"], key.Properties.Select(property => property.Name));
        Assert.False(key.IsUnique);
    }

    private static IEntityType EntityType(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StoredSecretEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
