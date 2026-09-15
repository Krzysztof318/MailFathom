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
/// Asserts the shape of the model the exports table is generated from. The model is built in memory by the real
/// PostgreSQL provider and no connection is opened, so this states what the table is declared to be; what PostgreSQL
/// then does with a row is an integration question.
/// </summary>
public sealed class MailboxExportModelTests
{
    /// <summary>
    /// An export is a copy of one account's mail, so an erased account must leave behind no record pointing at an
    /// archive of it — and nothing else in the schema would remove one.
    /// </summary>
    [Fact]
    public void MailboxExportModel_TheAccountTheExportCopies_TakesItsExportsWithItWhenItIsErased()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var foreignKey = Assert.Single(ExportEntityType(context).GetForeignKeys());

        // Assert
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal(PersistenceConstraintNames.MailboxExportAccountForeignKeyName, foreignKey.GetConstraintName());
    }

    /// <summary>An operator's listing is one account's exports newest first, which is the order the index is declared in.</summary>
    [Fact]
    public void MailboxExportModel_TheListingAnOperatorReads_IsOrderedNewestFirstWithinOneAccount()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var index = Assert.Single(
            ExportEntityType(context).GetIndexes(),
            candidate => candidate.GetDatabaseName()
                == PersistenceConstraintNames.MailboxExportAccountTimelineIndexName);

        // Assert
        Assert.Equal(
            [nameof(MailboxExportEntity.MailboxAccountId), nameof(MailboxExportEntity.RequestedAt)],
            index.Properties.Select(property => property.Name));
        Assert.Equal([false, true], index.IsDescending!);
    }

    /// <summary>
    /// The expiry pass reaches a row by its age and nothing else does, and it asks only about archives that exist — so
    /// the index is partial. Without the filter every failed, cancelled, and already-deleted export would sit in it.
    /// </summary>
    [Fact]
    public void MailboxExportModel_TheIndexTheExpiryPassReads_CoversOnlyTheRowsThatStillHaveAnArchive()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var index = Assert.Single(
            ExportEntityType(context).GetIndexes(),
            candidate => candidate.GetDatabaseName() == PersistenceConstraintNames.MailboxExportExpiryIndexName);

        // Assert
        Assert.Equal([nameof(MailboxExportEntity.ExpiresAt)], index.Properties.Select(property => property.Name));
        Assert.Equal("\"ExpiresAt\" IS NOT NULL", index.GetFilter());
    }

    /// <summary>
    /// Every write states the state it expects to find and is applied as one conditional statement, so a cancellation
    /// meeting a completion writes nothing. A concurrency token would be read by neither of them, because neither goes
    /// through the change tracker — so the row carries none, exactly as the job row does.
    /// </summary>
    [Fact]
    public void MailboxExportModel_TheRowTwoWritersRaceOver_CarriesNoConcurrencyTokenForTheStateCompareAndSetToDuplicate()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var tokens = ExportEntityType(context)
            .GetProperties()
            .Where(property => property.IsConcurrencyToken);

        // Assert
        Assert.Empty(tokens);
    }

    /// <summary>
    /// The state is stored as its own name, so an operator reading the row while answering a question sees what the
    /// export is doing rather than which ordinal it happened to be declared at.
    /// </summary>
    [Fact]
    public void MailboxExportModel_TheStateColumn_IsWrittenAsItsOwnNameRatherThanAnOrdinal()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var state = ExportEntityType(context).FindProperty(nameof(MailboxExportEntity.State));

        // Assert
        Assert.NotNull(state);
        Assert.Equal(typeof(string), state.GetProviderClrType());
        Assert.Equal(32, state.GetMaxLength());
    }

    /// <summary>Reads the design-time model, for the reason the stored email's own model tests do.</summary>
    private static IEntityType ExportEntityType(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(MailboxExportEntity))!;
}
