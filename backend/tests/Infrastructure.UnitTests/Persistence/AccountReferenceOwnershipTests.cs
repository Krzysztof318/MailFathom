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
/// Holds the whole model to the rule that an account reference is the account's generated identifier and nothing
/// beside it, rather than restating it once per table.
/// </summary>
/// <remarks>
/// <para>
/// The identifier is generated for the account rather than written by an operator within somebody's list, so it names
/// one mailbox across the deployment on its own. What that buys is the mailbox two people are assigned: one row, one
/// key, one copy of the mail. A table that keyed the identifier beside a user would have that mailbox stored once per
/// reader, which is the shape this rule exists to keep out — and a new table gaining a user beside its account is
/// exactly how it would come back. Reading it off the model rather than off a list is what makes the rule hold for a
/// table nobody remembered to add here.
/// </para>
/// <para>
/// A user column is not itself forbidden: a draft, an outgoing message, and a recurring send each record who wrote
/// them, and that is an attribute of the row rather than half of the name of its mailbox. What these assertions
/// refuse is the pair appearing in a key, an index, or a foreign key, which is where it would decide identity.
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

    /// <summary>The account itself is identified by the generated identifier alone, which is what makes it shareable.</summary>
    /// <remarks>
    /// This is the claim every other one here rests on. The identifier is the account's own rather than a name within
    /// somebody's list, so one row is the mailbox however many users are assigned it — and a key naming a user beside
    /// it would give each of them a row of their own and a copy of the mail behind it.
    /// </remarks>
    [Fact]
    public void Model_TheMailboxAccountKey_IsTheGeneratedIdentifierAlone()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = context.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(MailboxAccountEntity))!
            .FindPrimaryKey()!;

        // Assert
        Assert.Equal(["Id"], key.Properties.Select(property => property.Name));
    }

    /// <summary>
    /// A key that names an account names no user beside it, so the row it identifies is the mailbox's rather than one
    /// reader's view of the mailbox.
    /// </summary>
    /// <remarks>
    /// Each of these keys means "one row per account" — the thread binding, the two re-derivation cursors, the rule
    /// evaluation run, the refresh token, the spam classification run, the stored content total. That sentence stops
    /// being true the moment a user joins the key, and the mail behind the row is then stored once per assignment.
    /// Read off the model rather than listed, for the reason every other rule here is.
    /// </remarks>
    [Fact]
    public void Model_EveryKeyNamingAnAccount_NamesNoUserBesideIt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        string[] alsoNamingAUser =
        [
            .. EntityTypesNamingAnAccount(context)
                .SelectMany(entityType => entityType.GetKeys())
                .Where(NamesBoth)
                .Select(key => $"{key.DeclaringEntityType.ClrType.Name}.{key.GetName()}")
                .Order(StringComparer.Ordinal),
        ];

        // Assert
        Assert.Empty(alsoNamingAUser);
    }

    /// <summary>
    /// An index led by a user would be read by a scope that never names one, so PostgreSQL would walk it rather than
    /// enter it: a read narrowed to the accounts a caller is assigned states those identifiers and nothing else.
    /// </summary>
    /// <remarks>
    /// Stated as no user standing before the account rather than as the account leading, because two indexes are
    /// entered by something else entirely — an answering entry by its run, an outgoing message by when it was
    /// recorded — and the account is a narrowing term inside them rather than the way in.
    /// <para>
    /// The drafts listing is the one index a user genuinely leads, and it is named here rather than left to the rule
    /// because the exception is about what the read is: a draft is read by whoever wrote it and by nobody else
    /// assigned to the mailbox, so the listing is entered by the author and narrows on an account only when the
    /// caller named one. Naming it keeps a second index acquiring a user in front of its account a failure.
    /// </para>
    /// </remarks>
    [Fact]
    public void Model_EveryIndexNamingAnAccount_PlacesNoUserBeforeIt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        string[] enteredByAUser =
        [
            .. EntityTypesNamingAnAccount(context)
                .SelectMany(entityType => entityType.GetIndexes())
                .Where(index => PlacesAUserBeforeTheAccount(index.Properties))
                .Select(DatabaseNameOf)
                .Where(name => name != PersistenceConstraintNames.MailDraftAccountIndexName)
                .Order(StringComparer.Ordinal),
        ];

        // Assert
        Assert.Empty(enteredByAUser);
    }

    /// <summary>Every foreign key onto the account table names the identifier alone, which is what resolves it.</summary>
    /// <remarks>
    /// Read off the model rather than listed, for the reason the rules above are: a table added later that keyed onto
    /// a user beside the identifier would reference a mailbox per reader rather than the mailbox, and nothing but this
    /// would say so. The principal column is asserted as well as the dependent one, so a key cannot point at the
    /// account table through anything but its own primary key.
    /// </remarks>
    [Fact]
    public void Model_EveryForeignKeyOntoTheAccountTable_NamesTheIdentifierAlone()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        string[] namingSomethingElse =
        [
            .. ForeignKeysOntoTheAccountTable(context)
                .Where(foreignKey => !NamesTheAccountAlone(foreignKey.Properties, AccountColumn)
                    || !NamesTheAccountAlone(foreignKey.PrincipalKey.Properties, "Id"))
                .Select(foreignKey => foreignKey.DeclaringEntityType.ClrType.Name)
                .Order(StringComparer.Ordinal),
        ];

        // Assert
        Assert.Empty(namingSomethingElse);
        Assert.NotEmpty(ForeignKeysOntoTheAccountTable(context));
    }

    private static bool NamesTheAccountAlone(IReadOnlyList<IProperty> properties, string column) =>
        properties.Select(property => property.Name).SequenceEqual([column]);

    private static bool NamesBoth(IKey key)
    {
        string[] names = [.. key.Properties.Select(property => property.Name)];

        return names.Contains(AccountColumn) && names.Contains(UserColumn);
    }

    private static IReadOnlyList<IForeignKey> ForeignKeysOntoTheAccountTable(MailFathomDbContext context) =>
    [
        .. context.GetService<IDesignTimeModel>()
            .Model
            .GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Where(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(MailboxAccountEntity)),
    ];

    /// <summary>Reports whether a read would have to pass a user before it reached the account it narrows by.</summary>
    private static bool PlacesAUserBeforeTheAccount(IReadOnlyList<IProperty> properties)
    {
        string[] names = [.. properties.Select(property => property.Name)];
        var account = Array.IndexOf(names, AccountColumn);

        return account > 0 && names.Take(account).Contains(UserColumn);
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
