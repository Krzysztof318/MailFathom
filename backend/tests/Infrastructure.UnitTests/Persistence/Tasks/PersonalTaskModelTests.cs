// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Tasks;

/// <summary>
/// Holds the task row to the obligations that are the schema's rather than any code path's: that a task leaves with
/// the person whose list it was on, that it outlives the mail it cites, and that one person's list is read in one
/// order.
/// </summary>
/// <remarks>
/// The model is built in memory by the real PostgreSQL provider and no connection is opened, so what these assertions
/// state is what the schema is generated from. That a running PostgreSQL then performs the cascade is an integration
/// question and is measured there.
/// </remarks>
public sealed class PersonalTaskModelTests
{
    /// <summary>
    /// This table names no mail account, so the erasure walk that enumerates the tables that do would never reach it.
    /// The cascade from the user row is what discharges an erasure request over it instead.
    /// </summary>
    [Fact]
    public void Model_ThePersonWhoseListATaskIsOn_TakesTheirTasksWithThem()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = ForeignKeyOn(context, nameof(PersonalTaskEntity.UserId));

        // Assert
        Assert.Equal(nameof(UserAccountEntity), foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>
    /// The citation carries no foreign key, which is what lets a task outlive the mail it was read out of: a
    /// commitment read out of a thread is still owed once the thread is gone. Every association to a stored message
    /// cascades but for a reply's own parent, so a task that named one would be erased with the mail — which is why
    /// this is a value, exactly as the mutation audit trail's own message is.
    /// </summary>
    [Fact]
    public void Model_TheMessageATaskCites_IsAValueRatherThanAnAssociation()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var references = EntityType(context)
            .GetForeignKeys()
            .Where(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(StoredEmailEntity));

        // Assert
        Assert.Empty(references);
    }

    /// <summary>Most tasks cite no message at all, and one whose message was erased reads the same way.</summary>
    [Fact]
    public void Model_TheMessageATaskCites_IsOptional()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var citation = EntityType(context).FindProperty(nameof(PersonalTaskEntity.SourceStoredEmailId));

        // Assert
        Assert.NotNull(citation);
        Assert.True(citation.IsNullable);
    }

    /// <summary>A day rather than an instant, and absent where nobody has said when.</summary>
    [Fact]
    public void Model_TheDayATaskIsDueOn_IsOptional()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var dueOn = EntityType(context).FindProperty(nameof(PersonalTaskEntity.DueOn));

        // Assert
        Assert.NotNull(dueOn);
        Assert.True(dueOn.IsNullable);
        Assert.Equal(typeof(DateOnly?), dueOn.ClrType);
    }

    /// <summary>The list is drawn in the order the index declares, which is what keeps a long list costing one walk.</summary>
    [Fact]
    public void Model_TheListIndex_LeadsWithThePersonAndThenTheDueDay()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = EntityType(context)
            .GetIndexes()
            .Single(candidate => candidate.GetDatabaseName()
                == PersistenceConstraintNames.PersonalTaskDueOrderIndexName);

        // Assert
        Assert.Equal(
            [
                nameof(PersonalTaskEntity.UserId),
                nameof(PersonalTaskEntity.DueOn),
                nameof(PersonalTaskEntity.Id),
            ],
            index.Properties.Select(property => property.Name));
    }

    private static IForeignKey ForeignKeyOn(MailFathomDbContext context, string propertyName) =>
        EntityType(context)
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Any(property => property.Name == propertyName));

    private static IEntityType EntityType(MailFathomDbContext context) =>
        context.Model.FindEntityType(typeof(PersonalTaskEntity))
            ?? throw new InvalidOperationException("The model holds no task row.");

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
