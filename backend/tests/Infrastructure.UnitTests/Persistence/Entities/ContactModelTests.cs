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
/// Asserts the shape of the model the contact book's schema is generated from. The model is built in memory by the real
/// PostgreSQL provider and no connection is opened, so this states what the indexes and the keys are declared to be;
/// whether PostgreSQL then plans a listing against them is an integration question, asked where a book large enough for
/// the planner to have a choice is seeded.
/// </summary>
/// <remarks>
/// Every claim here is about the owner being part of the structure rather than a predicate a reader remembers to carry.
/// A book scoped only in the queries would pass every test of those queries and still let a page of one owner's book be
/// a walk of the table, an address one owner holds refuse another's, and a person outlive the owner who wrote them down.
/// </remarks>
public sealed class ContactModelTests
{
    /// <summary>The one order a book is listed in, which leads with the owner because a page is always of one book.</summary>
    [Fact]
    public void ContactModel_TheListingIndex_LeadsWithTheOwnerAndEndsWithTheIdentity()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = FindIndex(EntityTypeOf<ContactEntity>(context), PersistenceConstraintNames.ContactListingIndexName);

        // Assert
        Assert.Equal(
            ["OwnerId", "DisplayNameSortKey", "Id"],
            index.Properties.Select(property => property.Name));
        Assert.False(index.IsUnique);
    }

    /// <summary>The order is the ordinal one the domain derived the sort key to produce, whatever collation the database was created with.</summary>
    [Fact]
    public void ContactModel_TheSortKeyTheListingIsOrderedBy_StaysPinnedToTheOrdinalCollation()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var sortKey = EntityTypeOf<ContactEntity>(context).FindProperty(nameof(ContactEntity.DisplayNameSortKey));

        // Assert
        Assert.NotNull(sortKey);
        Assert.Equal("C", sortKey.GetCollation());
    }

    /// <summary>One address is one person's within one book, which is the index leading with the owner rather than the address.</summary>
    [Fact]
    public void ContactAddressModel_TheUniquenessOverAnAddress_HoldsWithinOneOwnersBook()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = FindIndex(
            EntityTypeOf<ContactAddressEntity>(context),
            PersistenceConstraintNames.ContactAddressUniqueIndexName);

        // Assert
        Assert.Equal(["OwnerId", "NormalizedAddress"], index.Properties.Select(property => property.Name));
        Assert.True(index.IsUnique);
    }

    /// <summary>
    /// An address row carries the owner as well as the contact, and the key is what keeps the repetition honest: it
    /// points at the pair on the contact, so no row can name an owner other than the one its contact is filed under.
    /// </summary>
    [Fact]
    public void ContactAddressModel_TheKeyBackToThePerson_CarriesTheOwnerAndCascades()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = Assert.Single(EntityTypeOf<ContactAddressEntity>(context).GetForeignKeys());

        // Assert
        Assert.Equal(["ContactId", "OwnerId"], foreignKey.Properties.Select(property => property.Name));
        Assert.Equal(["Id", "OwnerId"], foreignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(typeof(ContactEntity), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);

        // The half a nullable owner would slip past: PostgreSQL treats NULLs as distinct in a unique index, so an
        // address row written under no owner would escape (OwnerId, NormalizedAddress) and stay claimable in every book.
        Assert.True(foreignKey.IsRequired);
    }

    /// <summary>Erasing an owner takes their whole book, which is this key rather than a statement somebody remembers to write.</summary>
    [Fact]
    public void ContactModel_TheKeyOntoTheOwner_CascadesFromTheOwnerRecord()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = Assert.Single(EntityTypeOf<ContactEntity>(context).GetForeignKeys());

        // Assert
        Assert.Equal([nameof(ContactEntity.OwnerId)], foreignKey.Properties.Select(property => property.Name));
        Assert.Equal(typeof(OwnerAccountEntity), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.True(foreignKey.IsRequired);
    }

    private static IIndex FindIndex(IEntityType entityType, string indexName)
    {
        var index = entityType
            .GetIndexes()
            .FirstOrDefault(candidate => candidate.GetDatabaseName() == indexName);

        Assert.NotNull(index);

        return index;
    }

    /// <summary>Reads the design-time model, for the reason the stored email's own model tests do.</summary>
    private static IEntityType EntityTypeOf<TEntity>(MailFathomDbContext context)
        where TEntity : class =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
