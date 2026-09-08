// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence;

/// <summary>
/// Holds the whole model to the rule that an account reference is the user and the identifier together, rather than
/// restating it once per table.
/// </summary>
/// <remarks>
/// <para>
/// A bare <c>MailboxAccountId</c> names one account only while one namespace serves the deployment, and it no longer
/// does: two people served by one instance may each call an account <c>work</c>, so a table that records the
/// identifier alone stops naming a row — and a new table gaining the identifier without the user is exactly how that
/// would come back. Reading it off the model rather than off a list is what makes the rule hold for a table nobody
/// remembered to add here.
/// </para>
/// <para>
/// The model is built in memory by the real PostgreSQL provider and no connection is opened, so what these assertions
/// state is what the schema is generated from. Whether PostgreSQL then plans a read against the indexes below is an
/// integration question and is measured there.
/// </para>
/// </remarks>
public sealed class AccountReferenceOwnershipTests
{
    private const string AccountColumn = "MailboxAccountId";

    private const string UserColumn = "UserId";

    [Fact]
    public void Model_EveryEntityTypeNamingAnAccount_CarriesTheUserBesideIt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        string[] withoutAUser =
        [
            .. EntityTypesNamingAnAccount(context)
                .Where(entityType => entityType.FindProperty(UserColumn) is null)
                .Select(entityType => entityType.ClrType.Name)
                .Order(StringComparer.Ordinal),
        ];

        // Assert
        Assert.Empty(withoutAUser);
    }

    /// <summary>
    /// The user is exactly as optional as the account it qualifies, which is what keeps the pair from ever being half
    /// present.
    /// </summary>
    /// <remarks>
    /// On every table but the queue that means both are required. A job may belong to no account at all — a deployment
    /// -wide sweep is enqueued against nothing — so there the identifier is nullable and the user is nullable with it,
    /// for exactly the rows the identifier is absent from.
    /// </remarks>
    [Fact]
    public void Model_TheUserBesideAnAccountReference_IsExactlyAsOptionalAsTheAccount()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        string[] disagreeing =
        [
            .. EntityTypesNamingAnAccount(context)
                .Where(entityType => entityType.FindProperty(UserColumn) is { } user
                    && user.IsNullable != entityType.FindProperty(AccountColumn)!.IsNullable)
                .Select(entityType => entityType.ClrType.Name)
                .Order(StringComparer.Ordinal),
        ];

        // Assert
        Assert.Empty(disagreeing);
    }

    /// <summary>
    /// An index that still led with the identifier would be read by a scope the user already narrowed, so PostgreSQL
    /// would walk every user's rows for that identifier before applying the term that made the read the caller's own.
    /// </summary>
    /// <remarks>
    /// Stated as the user sitting immediately before the account rather than as the user leading, because two
    /// indexes are entered by something else entirely — an answering entry by its run, an outgoing message by when it
    /// was recorded — and the account is a narrowing term inside them rather than the way in. What the rule holds in
    /// both shapes is that the pair is never split, so no read walks one user's identifier through another's rows.
    /// It carries no exception: the index backing a foreign key onto the account used to be one, because that key
    /// named one column, and it names the pair now.
    /// </remarks>
    [Fact]
    public void Model_EveryIndexNamingAnAccount_PlacesTheUserImmediatelyBeforeIt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        string[] splittingThePair =
        [
            .. EntityTypesNamingAnAccount(context)
                .SelectMany(entityType => entityType.GetIndexes())
                .Where(index => !PlacesTheUserBeforeTheAccount(index.Properties))
                .Select(DatabaseNameOf)
                .Order(StringComparer.Ordinal),
        ];

        // Assert
        Assert.Empty(splittingThePair);
    }

    /// <summary>A key that names an account identifier names the user first, so the key names one account.</summary>
    /// <remarks>
    /// <para>
    /// Six keys were led by the identifier alone — the thread binding, the two re-derivation cursors, the rule
    /// evaluation run, the refresh token, and the spam classification run — and each of them meant "one row per
    /// account". That sentence is only true of a key that says whose account, so each now leads with the user.
    /// </para>
    /// <para>
    /// Read off the model rather than listed, for the reason every other rule here is: a table added later with a key
    /// over the identifier alone would silently make two users share a row, and this is what says so.
    /// </para>
    /// </remarks>
    [Fact]
    public void Model_EveryKeyNamingAnAccount_PlacesTheUserImmediatelyBeforeIt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        string[] splittingThePair =
        [
            .. EntityTypesNamingAnAccount(context)
                .SelectMany(entityType => entityType.GetKeys())
                .Where(key => !PlacesTheUserBeforeTheAccount(key.Properties))
                .Select(key => $"{key.DeclaringEntityType.ClrType.Name}.{key.GetName()}")
                .Order(StringComparer.Ordinal),
        ];

        // Assert
        Assert.Empty(splittingThePair);
    }

    /// <summary>A queue row names an account and its user together, or names neither.</summary>
    /// <remarks>
    /// The one table where the reference is optional, and therefore the one where the foreign key onto the account
    /// cannot enforce itself: PostgreSQL leaves a row supplying only one of the two columns unchecked, so a row
    /// carrying an identifier and no user would reference a mailbox nothing resolved. The check is what closes that,
    /// which makes it part of the reference rather than a separate rule about nullability.
    /// </remarks>
    [Fact]
    public void JobModel_TheUserBesideTheAccount_IsPresentForExactlyTheRowsTheAccountIs()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var check = Assert.Single(
            context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(JobEntity))!.GetCheckConstraints());

        // Assert
        Assert.Equal(PersistenceConstraintNames.JobAccountUserCheckConstraintName, check.Name);
        Assert.Equal($"(\"{UserColumn}\" IS NULL) = (\"{AccountColumn}\" IS NULL)", check.Sql);
    }

    /// <summary>The account itself is identified by the user and the identifier together, in that order.</summary>
    /// <remarks>
    /// This is the claim every other one here rests on. An identifier is the readable string an operator wrote and it
    /// names one mailbox within its user, so a key naming it alone would let the first user to declare <c>work</c>
    /// take the word from everybody served beside them.
    /// </remarks>
    [Fact]
    public void Model_TheMailboxAccountKey_IsTheUserAndThenTheIdentifier()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = context.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(MailboxAccountEntity))!
            .FindPrimaryKey()!;

        // Assert
        Assert.Equal([UserColumn, "Id"], key.Properties.Select(property => property.Name));
    }

    /// <summary>Every foreign key onto the account table names the pair, which is what makes a reference resolvable.</summary>
    /// <remarks>
    /// Read off the model rather than listed, for the reason the rules above are: a table added later that keyed onto
    /// the identifier alone would no longer name one account, and nothing but this would say so. The principal
    /// columns are asserted as well as the dependent ones, because a key pointing at the right pair in the wrong order
    /// would resolve one user's mailbox through another user's identifier.
    /// </remarks>
    [Fact]
    public void Model_EveryForeignKeyOntoTheAccountTable_NamesTheUserAndTheIdentifier()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        string[] namingSomethingElse =
        [
            .. ForeignKeysOntoTheAccountTable(context)
                .Where(foreignKey => !NamesThePair(foreignKey.Properties)
                    || !NamesThePrincipalPair(foreignKey.PrincipalKey.Properties))
                .Select(foreignKey => foreignKey.DeclaringEntityType.ClrType.Name)
                .Order(StringComparer.Ordinal),
        ];

        // Assert
        Assert.Empty(namingSomethingElse);
        Assert.NotEmpty(ForeignKeysOntoTheAccountTable(context));
    }

    private static bool NamesThePair(IReadOnlyList<IProperty> properties) =>
        properties.Select(property => property.Name).SequenceEqual([UserColumn, AccountColumn]);

    private static bool NamesThePrincipalPair(IReadOnlyList<IProperty> properties) =>
        properties.Select(property => property.Name).SequenceEqual([UserColumn, "Id"]);

    private static IReadOnlyList<IForeignKey> ForeignKeysOntoTheAccountTable(MailFathomDbContext context) =>
    [
        .. context.GetService<IDesignTimeModel>()
            .Model
            .GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Where(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(MailboxAccountEntity)),
    ];

    /// <summary>Reports whether the pair reads as one thing: the user, then the identifier it qualifies.</summary>
    private static bool PlacesTheUserBeforeTheAccount(IReadOnlyList<IProperty> properties)
    {
        var account = properties.Select(property => property.Name).ToList().IndexOf(AccountColumn);

        return account < 0 || (account > 0 && properties[account - 1].Name == UserColumn);
    }

    private static string DatabaseNameOf(IIndex index) =>
        index.GetDatabaseName() ?? string.Join('_', index.Properties.Select(property => property.Name));

    /// <summary>Reads the design-time model, for the reason each table's own model tests do.</summary>
    private static IEnumerable<IEntityType> EntityTypesNamingAnAccount(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>()
            .Model
            .GetEntityTypes()
            .Where(entityType => entityType.FindProperty(AccountColumn) is not null);

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
