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
/// the rule that makes a repeated import write nothing, what happens to a calendar when its owner is erased, and why
/// the message an event cites is not a key. The model is built in memory by the real PostgreSQL provider and no
/// connection is opened, so what this states is the declaration a schema is generated from.
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

    /// <summary>The calendar goes with the person whose it is, rather than being reached by an erasure that has to know this table exists.</summary>
    [Fact]
    public void CalendarEventModel_TheOwner_IsTheOneReferenceAndItCascades()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var reference = Assert.Single(EntityTypeOf<CalendarEventEntity>(context).GetForeignKeys());

        // Assert
        Assert.Equal(["UserId"], reference.Properties.Select(property => property.Name));
        Assert.Equal(typeof(UserAccountEntity), reference.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, reference.DeleteBehavior);
    }

    /// <summary>
    /// The message an event came out of is cited as an identifier rather than as an association, which is the whole of
    /// how an event outlives the mail a date was found in: every foreign key onto a stored message cascades, so a key
    /// here would take somebody's meeting off their calendar the moment they deleted the message in their mailbox.
    /// </summary>
    [Fact]
    public void CalendarEventModel_TheCitedMessage_IsAnIdentifierRatherThanAKey()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var calendarEvent = EntityTypeOf<CalendarEventEntity>(context);

        // Assert
        Assert.NotNull(calendarEvent.FindProperty(nameof(CalendarEventEntity.SourceStoredEmailId)));
        Assert.DoesNotContain(
            calendarEvent.GetForeignKeys(),
            reference => reference.PrincipalEntityType.ClrType == typeof(StoredEmailEntity));
    }

    /// <summary>
    /// The producer asks one question of the whole deployment every interval, and the index it rides holds only the
    /// reminders still owed: a filter on the claim leaves out every reminder of every event already past, which is
    /// nearly all of them once a calendar has any history.
    /// </summary>
    [Fact]
    public void CalendarEventReminderModel_DueIndex_HoldsOnlyTheRemindersNothingHasAnnouncedYet()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = IndexNamed(
            EntityTypeOf<CalendarEventReminderEntity>(context),
            PersistenceConstraintNames.CalendarEventReminderDueIndexName);

        // Assert
        Assert.Equal(["DueAt"], index.Properties.Select(property => property.Name));
        Assert.Equal("\"RaisedForDueAt\" IS NULL", index.GetFilter());
    }

    /// <summary>
    /// What the delete confirmation promises: an event deleted takes its reminders with it, so nothing is left to
    /// announce a commitment that is gone. The lead is half the key, which is what makes one lead set twice one row.
    /// </summary>
    [Fact]
    public void CalendarEventReminderModel_TheEvent_IsTheKeyItCascadesFrom()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var reminder = EntityTypeOf<CalendarEventReminderEntity>(context);
        var reference = Assert.Single(reminder.GetForeignKeys());

        // Assert
        Assert.Equal(
            ["CalendarEventId", "MinutesBefore"],
            reminder.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(typeof(CalendarEventEntity), reference.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, reference.DeleteBehavior);
    }

    /// <summary>
    /// Why the key is named rather than left to the convention: two revisions of one event both read that a lead has no
    /// row and both insert one, and the loser is recognized by the constraint its insert violated. A name only EF Core
    /// knew about would leave that race ending as a provider failure instead of the retry from a fresh read.
    /// </summary>
    [Fact]
    public void CalendarEventReminderModel_ItsKey_IsNamedSoALosingWriterIsRecognized()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = EntityTypeOf<CalendarEventReminderEntity>(context).FindPrimaryKey()!;

        // Assert
        Assert.Equal(PersistenceConstraintNames.CalendarEventReminderPrimaryKeyConstraintName, key.GetName());
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
