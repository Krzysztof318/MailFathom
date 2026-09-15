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
/// Every claim here is about the book being part of the structure rather than a predicate a reader remembers to carry.
/// A book scoped only in the queries would pass every test of those queries and still let a page of one book be a walk
/// of the table, an address one book holds refuse another's, and a person outlive the user who wrote them down.
/// </remarks>
public sealed class ContactModelTests
{
    /// <summary>The one order a book is listed in, which leads with the book because a page is read one book at a time.</summary>
    [Fact]
    public void ContactModel_TheListingIndex_LeadsWithTheBookAndEndsWithTheIdentity()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = FindIndex(EntityTypeOf<ContactEntity>(context), PersistenceConstraintNames.ContactListingIndexName);

        // Assert
        Assert.Equal(
            ["BookHolderId", "DisplayNameSortKey", "Id"],
            index.Properties.Select(property => property.Name));
        Assert.False(index.IsUnique);
    }

    /// <summary>
    /// A row belongs to exactly one book, and the book it names agrees with the holder it hangs off and with the origin
    /// it carries.
    /// </summary>
    /// <remarks>
    /// Three columns saying one thing is a state every reader would otherwise have to reason about, and two of the ways
    /// they could disagree are the ones this whole split exists to rule out: a collected record filed under a user, so
    /// that erasing them takes a mailbox's record with it, and an asserted one filed under a mailbox, so that a person
    /// somebody wrote down is served to every other user of that account.
    /// </remarks>
    [Fact]
    public void ContactModel_TheBookAHolderNames_IsRefusedWhenTheThreeColumnsDisagree()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var constraint = Assert.Single(
            EntityTypeOf<ContactEntity>(context).GetCheckConstraints(),
            candidate => candidate.Name == PersistenceConstraintNames.ContactBookHolderCheckConstraintName);

        // Assert
        Assert.Contains(
            "((\"UserId\" IS NOT NULL)::int + (\"MailboxAccountId\" IS NOT NULL)::int) = 1",
            constraint.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"BookHolderId\" = CASE WHEN \"UserId\" IS NULL THEN 'account:' || \"MailboxAccountId\" ELSE 'user:' || \"UserId\"::text END",
            constraint.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"Origin\" = CASE WHEN \"UserId\" IS NULL THEN 'Collected' ELSE 'Asserted' END",
            constraint.Sql,
            StringComparison.Ordinal);
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

    /// <summary>One address is one person's within one book, which is the index leading with the book rather than the address.</summary>
    /// <remarks>
    /// Within rather than across is the whole of it now that a user reads several books: a user's own record and a
    /// record one of their mailboxes collected may both hold one address, and which of the two a read answers with is
    /// the precedence the scope states rather than a row the database refused to write.
    /// </remarks>
    [Fact]
    public void ContactAddressModel_TheUniquenessOverAnAddress_HoldsWithinOneBook()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = FindIndex(
            EntityTypeOf<ContactAddressEntity>(context),
            PersistenceConstraintNames.ContactAddressUniqueIndexName);

        // Assert
        Assert.Equal(["BookHolderId", "NormalizedAddress"], index.Properties.Select(property => property.Name));
        Assert.True(index.IsUnique);
    }

    /// <summary>
    /// An address row carries the book as well as the contact, and the key is what keeps the repetition honest: it
    /// points at the pair on the contact, so no row can name a book other than the one its contact is filed under.
    /// </summary>
    [Fact]
    public void ContactAddressModel_TheKeyBackToThePerson_CarriesTheBookAndCascades()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = Assert.Single(EntityTypeOf<ContactAddressEntity>(context).GetForeignKeys());

        // Assert
        Assert.Equal(["ContactId", "BookHolderId"], foreignKey.Properties.Select(property => property.Name));
        Assert.Equal(["Id", "BookHolderId"], foreignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(typeof(ContactEntity), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);

        // The half a nullable book would slip past: PostgreSQL treats NULLs as distinct in a unique index, so an
        // address row written under no book would escape (BookHolderId, NormalizedAddress) and stay claimable in every
        // book.
        Assert.True(foreignKey.IsRequired);
    }

    /// <summary>Each book keys onto its own holder and cascades from it, so neither half is erased by a statement somebody has to remember.</summary>
    /// <remarks>
    /// The two keys are what divide an erasure. A user leaving the deployment takes the people they wrote down, by the
    /// key onto the user record; what their mailboxes collected stays for whoever else is assigned them, and goes only
    /// when the mailbox itself does, by the key onto the account record beside that account's folders, threads, and
    /// jobs. Both are optional because a row carries exactly one of them, which the check constraint is what enforces.
    /// </remarks>
    [Fact]
    public void ContactModel_TheKeysOntoTheHolders_CascadeFromEachBooksOwnAndAreEachOptional()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKeys = EntityTypeOf<ContactEntity>(context).GetForeignKeys();

        // Assert
        var ontoTheUser = Assert.Single(
            foreignKeys,
            key => key.PrincipalEntityType.ClrType == typeof(UserAccountEntity));

        Assert.Equal([nameof(ContactEntity.UserId)], ontoTheUser.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Cascade, ontoTheUser.DeleteBehavior);
        Assert.False(ontoTheUser.IsRequired);

        var ontoTheAccount = Assert.Single(
            foreignKeys,
            key => key.PrincipalEntityType.ClrType == typeof(MailboxAccountEntity));

        Assert.Equal(
            [nameof(ContactEntity.MailboxAccountId)],
            ontoTheAccount.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Cascade, ontoTheAccount.DeleteBehavior);
        Assert.False(ontoTheAccount.IsRequired);
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
