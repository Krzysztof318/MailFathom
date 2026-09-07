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

/// <summary>
/// Asserts the shape of the two tables a derivation is stored in. The model is built in memory by the real PostgreSQL
/// provider and no connection is opened, so what this states is what the keys, constraints, and delete behaviours are
/// declared to be.
/// </summary>
/// <remarks>
/// Each name asserted here is one the mapping states rather than leaves to convention, which is only worth doing while
/// something reads it back: <c>PersistenceConcurrencyConflicts</c> recognizes a losing writer by the constraint it
/// violated, so a name that drifted from the constant would turn a race the retry resolves into a provider failure that
/// ends the account run carrying it. Nothing else would report that, because the write succeeds on every run where no
/// second writer is present.
/// </remarks>
public sealed class EmailEnrichmentModelTests
{
    /// <summary>One derivation per message, and the key a second concurrent writer is recognized by.</summary>
    [Fact]
    public void EmailEnrichmentModel_PrimaryKey_IsTheMessageUnderTheNameTheConflictPredicateReads()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = EntityType<EmailEnrichmentEntity>(context).FindPrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.Equal([nameof(EmailEnrichmentEntity.StoredEmailId)], key.Properties.Select(property => property.Name));
        Assert.Equal(PersistenceConstraintNames.EmailEnrichmentPrimaryKeyConstraintName, key.GetName());
    }

    /// <summary>A message carries one reading of each kind, and the index rather than the writer is what decides it.</summary>
    [Fact]
    public void EmailEnrichmentMarkModel_TheAspectIndex_IsUniquePerMessageUnderItsDeclaredName()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = EntityType<EmailEnrichmentMarkEntity>(context).GetIndexes().Single();

        // Assert
        Assert.True(index.IsUnique);
        Assert.Equal(
            [nameof(EmailEnrichmentMarkEntity.StoredEmailId), nameof(EmailEnrichmentMarkEntity.Aspect)],
            index.Properties.Select(property => property.Name));
        Assert.Equal(PersistenceConstraintNames.EmailEnrichmentMarkAspectUniqueIndexName, index.GetDatabaseName());
    }

    /// <summary>Erasing a message erases what was derived from it, which is the privacy obligation the schema carries.</summary>
    [Fact]
    public void EmailEnrichmentModel_TheMessageItWasDerivedFrom_TakesTheDerivationWithIt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = EntityType<EmailEnrichmentEntity>(context).GetForeignKeys().Single();

        // Assert
        Assert.Equal(nameof(StoredEmailEntity), foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>And the marks go with the derivation, under the name PostgreSQL's identifier length made explicit.</summary>
    [Fact]
    public void EmailEnrichmentMarkModel_TheDerivationItBelongsTo_TakesItsMarksWithIt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = EntityType<EmailEnrichmentMarkEntity>(context).GetForeignKeys().Single();

        // Assert
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal(PersistenceConstraintNames.EmailEnrichmentMarkForeignKeyName, foreignKey.GetConstraintName());
    }

    private static IEntityType EntityType<TEntity>(MailFathomDbContext context)
        where TEntity : class =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
