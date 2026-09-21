// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Reminders;
using MailFathom.Domain.Tasks;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Tasks;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Tasks;

/// <summary>Covers the row a task is written as, and the task a row is read back into.</summary>
public sealed class PersonalTaskMappingTests
{
    private static readonly UserId User = UserId.Create(Guid.NewGuid());

    private static readonly DateOnly DueOn = new(2026, 9, 27);

    /// <summary>Everything the task stated reaches the row, because nothing else re-derives any of it.</summary>
    [Fact]
    public void ToEntity_ATaskCitingAMessage_KeepsEveryPartOfIt()
    {
        // Arrange
        var identifier = PersonalTaskId.Create(Guid.NewGuid());
        var message = StoredEmailId.Create(Guid.NewGuid());
        var task = PersonalTask.Compose(
            identifier,
            User,
            "Send the counter-proposal",
            DueOn,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Proposed,
            message);

        // Act
        var entity = PersonalTaskMapping.ToEntity(task);

        // Assert
        Assert.Equal(identifier.Value, entity.Id);
        Assert.Equal(User.Value, entity.UserId);
        Assert.Equal("Send the counter-proposal", entity.Title);
        Assert.Equal(DueOn, entity.DueOn);
        Assert.Equal(PersonalTaskOrigin.Proposed, entity.Origin);
        Assert.Equal(message.Value, entity.SourceStoredEmailId);
        Assert.False(entity.IsCompleted);
    }

    /// <summary>A task citing no message leaves the column empty rather than filling it with anything.</summary>
    [Fact]
    public void ToEntity_ATaskCitingNoMessage_LeavesTheCitationEmpty()
    {
        // Arrange
        var task = PersonalTask.Compose(
            PersonalTaskId.Create(Guid.NewGuid()),
            User,
            "Close the budget",
            dueOn: null,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Act
        var entity = PersonalTaskMapping.ToEntity(task);

        // Assert
        Assert.Null(entity.SourceStoredEmailId);
        Assert.Null(entity.DueOn);
    }

    /// <summary>A row read back is the task it was written from, completion and citation included.</summary>
    [Fact]
    public void ToPersonalTask_ARowACompletedTaskWasWrittenAs_ReadsBackAsThatTask()
    {
        // Arrange
        var identifier = Guid.NewGuid();
        var message = Guid.NewGuid();
        var entity = new PersonalTaskEntity
        {
            Id = identifier,
            UserId = User.Value,
            Title = "Sign the NDA",
            DueOn = DueOn,
            Origin = PersonalTaskOrigin.Asserted,
            IsCompleted = true,
            SourceStoredEmailId = message,
        };

        // Act
        var task = PersonalTaskMapping.ToPersonalTask(entity);

        // Assert
        Assert.Equal(identifier, task.Id.Value);
        Assert.Equal(User, task.User);
        Assert.Equal("Sign the NDA", task.Title);
        Assert.Equal(DueOn, task.DueOn);
        Assert.Equal(PersonalTaskOrigin.Asserted, task.Origin);
        Assert.Equal(message, task.SourceMessage?.Value);
        Assert.True(task.IsCompleted);
    }

    /// <summary>A row citing no message reads back as a task that cites none, which most tasks are.</summary>
    [Fact]
    public void ToPersonalTask_ARowCitingNoMessage_ReadsBackAsATaskCitingNothing()
    {
        // Arrange
        var entity = new PersonalTaskEntity
        {
            Id = Guid.NewGuid(),
            UserId = User.Value,
            Title = "Sign the NDA",
            DueOn = null,
            Origin = PersonalTaskOrigin.Asserted,
            IsCompleted = false,
            SourceStoredEmailId = null,
        };

        // Act
        var task = PersonalTaskMapping.ToPersonalTask(entity);

        // Assert
        Assert.Null(task.SourceMessage);
    }

    /// <summary>
    /// The instant is written beside the lead rather than computed in the producer's query, so the hour a due day is
    /// measured back from and the offset it is read in stay one rule written once.
    /// </summary>
    [Fact]
    public void ToEntity_ATaskThatAnnouncesSomething_WritesTheInstantEachLeadFallsAt()
    {
        // Arrange
        var warsaw = TimeSpan.FromHours(2);
        var task = PersonalTask.Compose(
            PersonalTaskId.Create(Guid.NewGuid()),
            User,
            "Send the counter-proposal",
            DueOn,
            new TaskAnnouncement(warsaw, [Reminder.Create(0), Reminder.Create(24 * 60)]),
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Act
        var entity = PersonalTaskMapping.ToEntity(task);

        // Assert
        Assert.Equal(120, entity.DueDayOffsetMinutes);
        Assert.Equal(
            [
                (24 * 60, new DateTimeOffset(2026, 9, 26, 9, 0, 0, warsaw)),
                (0, new DateTimeOffset(2026, 9, 27, 9, 0, 0, warsaw)),
            ],
            entity.Reminders.Select(reminder => (reminder.MinutesBefore, reminder.DueAt)));
        Assert.All(entity.Reminders, reminder => Assert.Null(reminder.RaisedForDueAt));
    }

    /// <summary>A task announcing nothing states no offset either, because the offset exists only to place an hour.</summary>
    [Fact]
    public void ToEntity_ATaskThatAnnouncesNothing_WritesNoOffsetAndNoReminderRow()
    {
        // Arrange
        var task = PersonalTask.Compose(
            PersonalTaskId.Create(Guid.NewGuid()),
            User,
            "Sign the NDA",
            DueOn,
            TaskAnnouncement.Silent,
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Act
        var entity = PersonalTaskMapping.ToEntity(task);

        // Assert
        Assert.Null(entity.DueDayOffsetMinutes);
        Assert.Empty(entity.Reminders);
    }

    /// <summary>What a row holds is what the task reads back with, leads and the hour they are measured from alike.</summary>
    [Fact]
    public void ToPersonalTask_ARowCarryingReminders_ReadsThemBackWithTheirAnchor()
    {
        // Arrange
        var identifier = Guid.NewGuid();
        var entity = new PersonalTaskEntity
        {
            Id = identifier,
            UserId = User.Value,
            Title = "Send the counter-proposal",
            DueOn = DueOn,
            DueDayOffsetMinutes = 120,
            Origin = PersonalTaskOrigin.Asserted,
            IsCompleted = false,
            SourceStoredEmailId = null,
        };

        entity.Reminders.Add(new PersonalTaskReminderEntity
        {
            PersonalTaskId = identifier,
            MinutesBefore = 60,
            DueAt = new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.FromHours(2)),
        });

        // Act
        var task = PersonalTaskMapping.ToPersonalTask(entity);

        // Assert
        Assert.Equal([60], task.Reminders.Select(reminder => reminder.MinutesBefore));
        Assert.Equal(
            new DateTimeOffset(2026, 9, 27, 9, 0, 0, TimeSpan.FromHours(2)),
            task.AnchorsRemindersAt);
    }

    /// <summary>
    /// A row an older build wrote carries leads and no offset, and those leads name no instant — so it reads back as
    /// a task announcing nothing rather than as reminders measured from an hour this deployment invented.
    /// </summary>
    [Fact]
    public void ToPersonalTask_ARowCarryingRemindersAndNoOffset_ReadsBackAsAnnouncingNothing()
    {
        // Arrange
        var identifier = Guid.NewGuid();
        var entity = new PersonalTaskEntity
        {
            Id = identifier,
            UserId = User.Value,
            Title = "Send the counter-proposal",
            DueOn = DueOn,
            DueDayOffsetMinutes = null,
            Origin = PersonalTaskOrigin.Asserted,
            IsCompleted = false,
            SourceStoredEmailId = null,
        };

        entity.Reminders.Add(new PersonalTaskReminderEntity
        {
            PersonalTaskId = identifier,
            MinutesBefore = 60,
            DueAt = new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero),
        });

        // Act
        var task = PersonalTaskMapping.ToPersonalTask(entity);

        // Assert
        Assert.Empty(task.Reminders);
        Assert.Null(task.AnchorsRemindersAt);
    }
}
