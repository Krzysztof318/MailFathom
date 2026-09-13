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
/// Asserts the shape of the model a held account's folders are generated from. The two unique indexes are what a second
/// writer meets, so a filter or a null semantics lost here would let two edits commit a duplicate the domain refused.
/// </summary>
public sealed class LocalMailFolderModelTests
{
    /// <summary>A top-level folder has no parent, so the name index treats that null as one value rather than many.</summary>
    [Fact]
    public void LocalMailFolderModel_TheSiblingNameIndex_IsUniqueAmongLiveFoldersWithTheTopLevelAsOneParent()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = FindIndex(PersistenceConstraintNames.LocalMailFolderSiblingNameUniqueIndexName, context);

        // Assert
        Assert.Equal(
            ["UserId", "MailboxAccountId", "ParentId", "NameKey"],
            index.Properties.Select(property => property.Name));
        Assert.True(index.IsUnique);
        Assert.False((bool?)index.FindAnnotation("Npgsql:NullsDistinct")?.Value);
        Assert.Equal("\"ErasedAt\" IS NULL", index.GetFilter());
    }

    /// <summary>One live folder per protected role, and a folder without a role or already erased takes no part.</summary>
    [Fact]
    public void LocalMailFolderModel_TheRoleIndex_IsUniqueAmongLiveFoldersThatPlayARole()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = FindIndex(PersistenceConstraintNames.LocalMailFolderRoleUniqueIndexName, context);

        // Assert
        Assert.Equal(["UserId", "MailboxAccountId", "Role"], index.Properties.Select(property => property.Name));
        Assert.True(index.IsUnique);
        Assert.Equal("\"Role\" IS NOT NULL AND \"ErasedAt\" IS NULL", index.GetFilter());
    }

    /// <summary>Erasing an account takes its hierarchy with it, and removing a folder never takes the mail filed in it.</summary>
    [Fact]
    public void LocalMailFolderModel_TheKeys_CascadeFromTheAccountAndNeverOntoStoredMail()
    {
        // Arrange
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        // Act
        var accountKey = model.FindEntityType(typeof(LocalMailFolderEntity))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(MailboxAccountEntity));
        var storedEmailKey = model.FindEntityType(typeof(StoredEmailEntity))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(LocalMailFolderEntity));

        // Assert
        Assert.Equal(DeleteBehavior.Cascade, accountKey.DeleteBehavior);
        Assert.Equal(DeleteBehavior.NoAction, storedEmailKey.DeleteBehavior);
    }

    /// <summary>Every hierarchy write bumps the revision, so it is what makes a write conditional on the hierarchy it read.</summary>
    [Fact]
    public void LocalMailFolderModel_TheAccountsFoldersRevision_IsAConcurrencyToken()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var revision = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(MailboxAccountEntity))!
            .FindProperty(nameof(MailboxAccountEntity.LocalMailFoldersRevision));

        // Assert
        Assert.NotNull(revision);
        Assert.True(revision.IsConcurrencyToken);
    }

    private static IIndex FindIndex(string indexName, MailFathomDbContext context)
    {
        var index = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(LocalMailFolderEntity))!
            .GetIndexes()
            .FirstOrDefault(candidate => candidate.GetDatabaseName() == indexName);

        Assert.NotNull(index);

        return index;
    }

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
