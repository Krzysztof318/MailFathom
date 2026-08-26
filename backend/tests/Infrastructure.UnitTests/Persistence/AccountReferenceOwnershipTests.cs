// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence;

/// <summary>
/// Holds the whole model to the rule that an account reference is the owner and the identifier together, rather than
/// restating it once per table.
/// </summary>
/// <remarks>
/// <para>
/// A bare <c>MailboxAccountId</c> names one account only while one namespace serves the deployment. Under several
/// owners two people may each call an account <c>work</c>, so a table that records the identifier alone stops naming a
/// row — and a new table gaining the identifier without the owner is exactly how that would come back. Reading it off
/// the model rather than off a list is what makes the rule hold for a table nobody remembered to add here.
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

    private const string OwnerColumn = "OwnerId";

    [Fact]
    public void Model_EveryEntityTypeNamingAnAccount_CarriesTheOwnerBesideIt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        string[] withoutAnOwner =
        [
            .. EntityTypesNamingAnAccount(context)
                .Where(entityType => entityType.FindProperty(OwnerColumn) is null)
                .Select(entityType => entityType.ClrType.Name)
                .Order(StringComparer.Ordinal),
        ];

        // Assert
        Assert.Empty(withoutAnOwner);
    }

    /// <summary>
    /// The owner is exactly as optional as the account it qualifies, which is what keeps the pair from ever being half
    /// present.
    /// </summary>
    /// <remarks>
    /// On every table but the queue that means both are required. A job may belong to no account at all — a deployment
    /// -wide sweep is enqueued against nothing — so there the identifier is nullable and the owner is nullable with it,
    /// for exactly the rows the identifier is absent from.
    /// </remarks>
    [Fact]
    public void Model_TheOwnerBesideAnAccountReference_IsExactlyAsOptionalAsTheAccount()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        string[] disagreeing =
        [
            .. EntityTypesNamingAnAccount(context)
                .Where(entityType => entityType.FindProperty(OwnerColumn) is { } owner
                    && owner.IsNullable != entityType.FindProperty(AccountColumn)!.IsNullable)
                .Select(entityType => entityType.ClrType.Name)
                .Order(StringComparer.Ordinal),
        ];

        // Assert
        Assert.Empty(disagreeing);
    }

    /// <summary>
    /// An index that still led with the identifier would be read by a scope the owner already narrowed, so PostgreSQL
    /// would walk every owner's rows for that identifier before applying the term that made the read the caller's own.
    /// </summary>
    /// <remarks>
    /// Stated as the owner sitting immediately before the account rather than as the owner leading, because two
    /// indexes are entered by something else entirely — an answering entry by its run, an outgoing message by when it
    /// was recorded — and the account is a narrowing term inside them rather than the way in. What the rule holds in
    /// both shapes is that the pair is never split, so no read walks one owner's identifier through another's rows.
    /// </remarks>
    [Fact]
    public void Model_EveryIndexNamingAnAccount_PlacesTheOwnerImmediatelyBeforeIt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        string[] splittingThePair =
        [
            .. EntityTypesNamingAnAccount(context)
                .SelectMany(entityType => entityType.GetIndexes())
                .Where(index => !BacksTheForeignKeyOntoTheAccountTable(index))
                .Where(index => !PlacesTheOwnerBeforeTheAccount(index))
                .Select(DatabaseNameOf)
                .Order(StringComparer.Ordinal),
        ];

        // Assert
        Assert.Empty(splittingThePair);
    }

    /// <summary>The index a foreign key onto the account table is backed by is the one place the pair is not together.</summary>
    /// <remarks>
    /// <para>
    /// It is the key's own column and nothing else, because <c>mailbox_accounts</c> still keeps a single-column key
    /// here: this change adds the owner beside every reference and leaves which identifiers are legal alone. Until the
    /// key itself becomes the pair, a composite index cannot back this constraint, so the exception is not one a
    /// better index would close.
    /// </para>
    /// <para>
    /// Named rather than filtered away silently, so the exception reads as the scope boundary it is and so an index
    /// that stopped backing a foreign key would fail the rule above instead of inheriting its licence.
    /// </para>
    /// </remarks>
    [Fact]
    public void Model_TheIndexesBackingAForeignKeyOntoTheAccountTable_AreTheOnlyOnesTheOwnerIsAbsentFrom()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        string[] withoutTheOwner =
        [
            .. EntityTypesNamingAnAccount(context)
                .SelectMany(entityType => entityType.GetIndexes())
                .Where(index => !PlacesTheOwnerBeforeTheAccount(index))
                .Select(DatabaseNameOf)
                .Order(StringComparer.Ordinal),
        ];

        // Assert
        Assert.Equal(
            [
                "IX_email_threads_MailboxAccountId",
                "IX_jobs_MailboxAccountId",
                "IX_mail_folders_MailboxAccountId",
            ],
            withoutTheOwner);
    }

    /// <summary>Reports whether the pair reads as one thing: the owner, then the identifier it qualifies.</summary>
    private static bool PlacesTheOwnerBeforeTheAccount(IIndex index)
    {
        var account = index.Properties.Select(property => property.Name).ToList().IndexOf(AccountColumn);

        return account < 0 || (account > 0 && index.Properties[account - 1].Name == OwnerColumn);
    }

    private static bool BacksTheForeignKeyOntoTheAccountTable(IIndex index) =>
        index.DeclaringEntityType
            .GetForeignKeys()
            .Where(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(MailboxAccountEntity))
            .Any(foreignKey => foreignKey.Properties.SequenceEqual(index.Properties));

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
