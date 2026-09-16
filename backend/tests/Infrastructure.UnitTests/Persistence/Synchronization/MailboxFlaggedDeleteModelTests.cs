// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Synchronization;

/// <summary>
/// Asserts the shape a delete left flagged on the server is followed through. The model is built in memory by the real
/// PostgreSQL provider and no connection is opened, so what this states is the declaration a schema is generated from.
/// </summary>
public sealed class MailboxFlaggedDeleteModelTests
{
    /// <summary>A settlement replayed after a commit conflict follows an occurrence once rather than twice.</summary>
    [Fact]
    public void MailboxFlaggedDeleteModel_OccurrenceIndex_IsUniqueOnTheServerPosition()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = IndexNamed(
            EntityTypeOf<MailboxFlaggedDeleteEntity>(context),
            PersistenceConstraintNames.MailboxFlaggedDeleteOccurrenceUniqueIndexName);

        // Assert
        Assert.True(index.IsUnique);
        Assert.Equal(["MailFolderId", "UidValidity", "Uid"], index.Properties.Select(property => property.Name));
    }

    /// <summary>
    /// The record outlives an erased local copy, which is why it exists, and goes with a kept copy or a folder that is
    /// gone, since neither leaves anything a run could ask about or bring back.
    /// </summary>
    [Theory]
    [InlineData("MailFolderId", typeof(MailFolderEntity))]
    [InlineData("StoredEmailId", typeof(StoredEmailEntity))]
    public void MailboxFlaggedDeleteModel_Reference_CascadesFromWhatItNames(string column, Type principal)
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var reference = Assert.Single(
            EntityTypeOf<MailboxFlaggedDeleteEntity>(context).GetForeignKeys(),
            candidate => candidate.Properties.Any(property => property.Name == column));

        // Assert
        Assert.Equal(principal, reference.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, reference.DeleteBehavior);
    }

    /// <summary>The index reconciliation reads holds only the flag-only deletes still waiting to be settled.</summary>
    [Fact]
    public void MailboxMutationModel_FlaggedDeleteIndex_HoldsOnlyUnsettledFlagOnlyDeletes()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = IndexNamed(
            EntityTypeOf<MailboxMutationEntity>(context),
            PersistenceConstraintNames.MailboxMutationFlaggedDeleteIndexName);

        // Assert
        Assert.Equal(
            ["MailFolderId", "UidValidity", "RecordedAt"],
            index.Properties.Select(property => property.Name));
        Assert.Equal(
            "\"ServerDisposition\" = 'FlagDeleted' AND \"Stage\" = 'Completed'"
            + " AND \"DeleteFlagSettledAt\" IS NULL AND \"SourceRemovalObservedAt\" IS NULL",
            index.GetFilter());
    }

    private static IIndex IndexNamed(IEntityType entityType, string name) =>
        Assert.Single(entityType.GetIndexes(), candidate => candidate.GetDatabaseName() == name);

    /// <summary>
    /// Reads the design-time model rather than <c>DbContext.Model</c>, because the runtime model is trimmed to what a
    /// query needs and throws for the index configuration a schema is generated from.
    /// </summary>
    private static IEntityType EntityTypeOf<TEntity>(MailFathomDbContext context)
        where TEntity : class =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
