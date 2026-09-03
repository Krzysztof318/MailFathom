// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Notifications;

/// <summary>
/// Holds the notification row to the two obligations that are the schema's rather than any code path's: that a
/// notification leaves with the mail it leads to and with the person it happened to, and that one condition standing
/// unread cannot be said twice.
/// </summary>
/// <remarks>
/// The model is built in memory by the real PostgreSQL provider and no connection is opened, so what these assertions
/// state is what the schema is generated from. That a running PostgreSQL then performs the cascade and refuses the
/// second insert is an integration question and is measured there.
/// </remarks>
public sealed class NotificationModelTests
{
    /// <summary>A notification that leads to a message cannot outlive it, which is what deleting the mail has to mean.</summary>
    [Fact]
    public void Model_TheMessageANotificationLeadsTo_TakesTheNotificationWithItWhenItIsDeleted()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = ForeignKeyOn(context, nameof(NotificationEntity.TargetStoredEmailId));

        // Assert
        Assert.Equal(nameof(StoredEmailEntity), foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>
    /// The target is optional, because most notifications lead to a screen or to nothing at all — which is what makes
    /// the cascade above a rule about the rows that do name a message rather than a requirement that every row does.
    /// </summary>
    [Fact]
    public void Model_TheMessageANotificationLeadsTo_IsOptional()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var target = EntityType(context).FindProperty(nameof(NotificationEntity.TargetStoredEmailId));

        // Assert
        Assert.NotNull(target);
        Assert.True(target.IsNullable);
    }

    /// <summary>
    /// This table names no mail account, so the erasure walk that enumerates the tables that do would never reach it.
    /// The cascade from the owner row is what discharges an erasure request over it instead.
    /// </summary>
    [Fact]
    public void Model_TheOwnerANotificationHappenedTo_TakesTheirNotificationsWithThem()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = ForeignKeyOn(context, nameof(NotificationEntity.OwnerId));

        // Assert
        Assert.Equal(nameof(OwnerAccountEntity), foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>
    /// The deduplication rule is the database's, and it is partial in exactly one direction: one unread statement per
    /// condition, and no bound at all on conditions the person has already read.
    /// </summary>
    [Fact]
    public void Model_TheDeduplicationIndex_IsUniqueOverUnreadRowsAlone()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = EntityType(context)
            .GetIndexes()
            .Single(candidate => candidate.GetDatabaseName()
                == PersistenceConstraintNames.NotificationUnreadConditionUniqueIndexName);

        // Assert
        Assert.True(index.IsUnique);
        Assert.Equal(
            [nameof(NotificationEntity.OwnerId), nameof(NotificationEntity.DeduplicationKey)],
            index.Properties.Select(property => property.Name));
        Assert.Equal($"NOT \"{nameof(NotificationEntity.IsRead)}\"", index.GetFilter());
    }

    /// <summary>The centre's page and its retention sweep are one order, so they are one index.</summary>
    [Fact]
    public void Model_TheTimelineIndex_LeadsWithTheOwnerAndThenTheInstant()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = EntityType(context)
            .GetIndexes()
            .Single(candidate => candidate.GetDatabaseName()
                == PersistenceConstraintNames.NotificationTimelineIndexName);

        // Assert
        Assert.Equal(
            [
                nameof(NotificationEntity.OwnerId),
                nameof(NotificationEntity.OccurredAt),
                nameof(NotificationEntity.Id),
            ],
            index.Properties.Select(property => property.Name));
    }

    private static IForeignKey ForeignKeyOn(MailFathomDbContext context, string propertyName) =>
        EntityType(context)
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Any(property => property.Name == propertyName));

    private static IEntityType EntityType(MailFathomDbContext context) =>
        context.Model.FindEntityType(typeof(NotificationEntity))
            ?? throw new InvalidOperationException("The model holds no notification row.");

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
