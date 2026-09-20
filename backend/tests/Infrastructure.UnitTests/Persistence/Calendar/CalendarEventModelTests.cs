// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Calendar;

/// <summary>
/// Asserts what a calendar's own schema has to say rather than leave to a reader: the order a window is answered in,
/// the rule that makes a repeated import write nothing, and what each of the two references does when what it names is
/// erased. The model is built in memory by the real PostgreSQL provider and no connection is opened, so what this
/// states is the declaration a schema is generated from.
/// </summary>
public sealed class CalendarEventModelTests
{
    /// <summary>A read is always one person's, and two events beginning at one instant still have an order.</summary>
    [Fact]
    public void CalendarEventModel_WindowIndex_LeadsWithTheOwnerAndClosesWithTheIdentity()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = IndexNamed(EntityTypeOf<CalendarEventEntity>(context), PersistenceConstraintNames.CalendarEventWindowIndexName);

        // Assert
        Assert.Equal(["UserId", "StartsAt", "Id"], index.Properties.Select(property => property.Name));
    }

    /// <summary>
    /// The whole of what makes importing one file twice create nothing the second time, and partial so that the events
    /// nobody imported — nearly all of them — cost no index entry.
    /// </summary>
    [Fact]
    public void CalendarEventModel_ImportedIdentifierIndex_IsUniquePerCalendarAndHoldsOnlyImportedEvents()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = IndexNamed(
            EntityTypeOf<CalendarEventEntity>(context),
            PersistenceConstraintNames.CalendarEventImportedUidUniqueIndexName);

        // Assert
        Assert.True(index.IsUnique);
        Assert.Equal(["UserId", "ImportedUid"], index.Properties.Select(property => property.Name));
        Assert.Equal("\"ImportedUid\" IS NOT NULL", index.GetFilter());
    }

    /// <summary>
    /// The calendar goes with the person whose it is, and the citation goes with the message — but the event does not,
    /// because an event somebody accepted is theirs and erasing the mail a date was found in is not a reason to take
    /// the meeting off their calendar.
    /// </summary>
    [Theory]
    [InlineData("UserId", typeof(UserAccountEntity), DeleteBehavior.Cascade)]
    [InlineData("SourceStoredEmailId", typeof(StoredEmailEntity), DeleteBehavior.SetNull)]
    public void CalendarEventModel_Reference_FollowsWhatItNamesAsFarAsItShould(
        string column,
        Type principal,
        DeleteBehavior behavior)
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var reference = Assert.Single(
            EntityTypeOf<CalendarEventEntity>(context).GetForeignKeys(),
            candidate => candidate.Properties.Any(property => property.Name == column));

        // Assert
        Assert.Equal(principal, reference.PrincipalEntityType.ClrType);
        Assert.Equal(behavior, reference.DeleteBehavior);
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
