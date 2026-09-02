// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence;

/// <summary>Proves the identifiers a composed statement is written from come from the model rather than from a literal.</summary>
/// <remarks>
/// The few statements this layer composes rather than translates take their table and column names through here, so a
/// name that stopped agreeing with the mapping would produce a statement PostgreSQL refuses at run time — inside the
/// transaction that was storing somebody's mail. What is asserted is therefore that a mapped name is read and a missing
/// one is refused loudly, both of which are decidable from the design-time model and neither of which needs a server.
/// </remarks>
public sealed class PersistedSchemaNamesTests
{
    [Fact]
    public void QuotedTable_AMappedEntity_NamesTheTableTheModelStates()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var table = PersistedSchemaNames.QuotedTable(
            PersistedSchemaNames.EntityTypeOf<OwnerStoredContentEntity>(context.Model));

        // Assert
        Assert.Equal($"\"{OwnerStoredContentEntity.TableName}\"", table);
    }

    [Fact]
    public void QuotedColumn_AMappedProperty_NamesTheColumnTheModelStates()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        var entityType = PersistedSchemaNames.EntityTypeOf<OwnerStoredContentEntity>(context.Model);

        // Act
        var ownerColumn = PersistedSchemaNames.QuotedColumn(
            entityType,
            nameof(OwnerStoredContentEntity.OwnerId));
        var countColumn = PersistedSchemaNames.QuotedColumn(
            entityType,
            nameof(OwnerStoredContentEntity.StoredContentByteCount));

        // Assert
        Assert.Equal($"\"{OwnerStoredContentEntity.OwnerIdColumnName}\"", ownerColumn);
        Assert.Equal($"\"{OwnerStoredContentEntity.StoredContentByteCountColumnName}\"", countColumn);
    }

    /// <summary>A name the model does not hold is refused where the statement is composed, not where it is executed.</summary>
    [Fact]
    public void EntityTypeOf_AClassTheModelDoesNotMap_IsRefused()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        var entityType = PersistedSchemaNames.EntityTypeOf<OwnerStoredContentEntity>(context.Model);

        // Act, Assert
        Assert.Throws<InvalidOperationException>(
            () => PersistedSchemaNames.EntityTypeOf<PersistedSchemaNamesTests>(context.Model));
        Assert.Throws<InvalidOperationException>(
            () => PersistedSchemaNames.QuotedColumn(entityType, "NoSuchProperty"));
    }

    [Fact]
    public void EntityTypeOf_AMissingArgument_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => PersistedSchemaNames.EntityTypeOf<OwnerStoredContentEntity>(null!));
        Assert.Throws<ArgumentNullException>(() => PersistedSchemaNames.QuotedTable(null!));
        Assert.Throws<ArgumentNullException>(() => PersistedSchemaNames.QuotedColumn(null!, "OwnerId"));
    }
}
