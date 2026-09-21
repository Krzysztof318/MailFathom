// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Reminders;
using MailFathom.Domain.Tasks;
using Xunit;

namespace MailFathom.Domain.UnitTests.Tasks;

/// <summary>
/// Covers what announcing a task means: the anchor a lead is measured back from, the order a set is kept in, and the
/// two things a writer may not state — a reminder against no due day, and a lead twice.
/// </summary>
public sealed class PersonalTaskReminderTests
{
    private static readonly UserId User = UserId.Create(Guid.NewGuid());

    private static readonly PersonalTaskId Identifier = PersonalTaskId.Create(Guid.NewGuid());

    private static readonly DateOnly DueOn = new(2026, 9, 27);

    private static readonly TimeSpan Warsaw = TimeSpan.FromHours(2);

    /// <summary>Nine in the morning on the due day, read in the offset the writer stated and not in UTC.</summary>
    [Fact]
    public void AnchorsRemindersAt_ADatedTaskThatAnnouncesSomething_IsNineInTheMorningInTheStatedOffset()
    {
        // Act
        var task = Announcing(0);

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 9, 0, 0, Warsaw), task.AnchorsRemindersAt);
    }

    /// <summary>The presets the design draws for a due date, each measured back from that same hour.</summary>
    [Theory]
    [InlineData(0, 9)]
    [InlineData(60, 8)]
    [InlineData(4 * 60, 5)]
    public void RemindsAt_APresetADueDateOffers_FallsThatFarBeforeNineInTheMorning(int minutesBefore, int hour)
    {
        // Arrange
        var task = Announcing(minutesBefore);

        // Act
        var remindsAt = task.RemindsAt(Reminder.Create(minutesBefore));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 9, 27, hour, 0, 0, Warsaw), remindsAt);
    }

    /// <summary>A day and two days before reach back into the days before the due one, still at that hour.</summary>
    [Theory]
    [InlineData(24 * 60, 26)]
    [InlineData(2 * 24 * 60, 25)]
    public void RemindsAt_APresetReachingBackWholeDays_FallsAtNineOnTheEarlierDay(int minutesBefore, int day)
    {
        // Arrange
        var task = Announcing(minutesBefore);

        // Act
        var remindsAt = task.RemindsAt(Reminder.Create(minutesBefore));

        // Assert
        Assert.Equal(new DateTimeOffset(2026, 9, day, 9, 0, 0, Warsaw), remindsAt);
    }

    /// <summary>Moving the due date moves every instant with it, because nothing stores what the person chose twice.</summary>
    [Fact]
    public void Revise_ADueDateSomebodyMoved_MovesEveryReminderWithIt()
    {
        // Arrange
        var task = Announcing(24 * 60);

        // Act
        var moved = task.Revise(
            task.Title,
            new DateOnly(2026, 10, 1),
            new TaskAnnouncement(Warsaw, task.Reminders));

        // Assert
        Assert.Equal(
            new DateTimeOffset(2026, 9, 30, 9, 0, 0, Warsaw),
            moved.RemindsAt(Reminder.Create(24 * 60)));
    }

    /// <summary>
    /// Taking the date off a task takes its reminders with it rather than leaving leads measured back from nothing,
    /// which is the same answer the client gives by offering no preset on an undated task.
    /// </summary>
    [Fact]
    public void Revise_ADateSomebodyTookOffATaskThatAnnouncedSomething_IsRefused()
    {
        // Arrange
        var task = Announcing(60);

        // Act
        var revising = () => task.Revise(task.Title, dueOn: null, new TaskAnnouncement(Warsaw, task.Reminders));

        // Assert
        Assert.Equal("announcement", Assert.Throws<ArgumentException>(revising).ParamName);
    }

    /// <summary>Turning the last reminder off is a task stated with none, which is a statement rather than an omission.</summary>
    [Fact]
    public void Revise_ATaskWhoseLastReminderWasTurnedOff_AnnouncesNothingAndAnchorsNothing()
    {
        // Arrange
        var task = Announcing(60);

        // Act
        var quiet = task.Revise(task.Title, task.DueOn, TaskAnnouncement.Silent);

        // Assert
        Assert.Empty(quiet.Reminders);
        Assert.Null(quiet.DueDayOffset);
        Assert.Null(quiet.AnchorsRemindersAt);
    }

    /// <summary>A task nobody dated offers nothing to measure back from, so a lead against one is refused outright.</summary>
    [Fact]
    public void Compose_ARemindedTaskNobodyDated_IsRefused()
    {
        // Act
        var composing = () => PersonalTask.Compose(
            Identifier,
            User,
            "Sign the NDA",
            dueOn: null,
            new TaskAnnouncement(Warsaw, [Reminder.Create(60)]),
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Assert
        Assert.Equal("announcement", Assert.Throws<ArgumentException>(composing).ParamName);
    }

    /// <summary>The earliest warning first, which is the order a person reads their own reminders in.</summary>
    [Fact]
    public void Compose_ASetStatedInAnyOrder_KeepsItLongestLeadFirst()
    {
        // Act
        var task = Compose([Reminder.Create(0), Reminder.Create(2880), Reminder.Create(60)]);

        // Assert
        Assert.Equal([2880, 60, 0], task.Reminders.Select(reminder => reminder.MinutesBefore));
    }

    /// <summary>One lead stated twice is a caller's mistake rather than a set to fold, so it is reported.</summary>
    [Fact]
    public void Compose_OneLeadStatedTwice_IsRefused()
    {
        // Act
        var composing = () => Compose([Reminder.Create(60), Reminder.Create(60)]);

        // Assert
        Assert.Equal("reminders", Assert.Throws<ArgumentException>(composing).ParamName);
    }

    /// <summary>Each reminder is a row a run reads and a notification it may write, so the count is bounded.</summary>
    [Fact]
    public void Compose_MoreRemindersThanATaskMayCarry_IsRefused()
    {
        // Arrange
        var tooMany = Enumerable.Range(1, Reminder.MaximumCount + 1).Select(Reminder.Create).ToArray();

        // Act
        var composing = () => Compose(tooMany);

        // Assert
        Assert.Equal("reminders", Assert.Throws<ArgumentOutOfRangeException>(composing).ParamName);
    }

    /// <summary>
    /// The offset is what places the hour, so a value no UTC offset can be is refused here as well as at the
    /// transport boundary — the anchor is then a value rather than a property that throws when it is read.
    /// </summary>
    [Theory]
    [InlineData(15, 0)]
    [InlineData(-13, 0)]
    [InlineData(-15, 0)]
    [InlineData(2, 30)]
    public void Compose_AnOffsetNoDueDayCanRunIn_IsRefused(int hours, int seconds)
    {
        // Arrange
        var offset = TimeSpan.FromHours(hours) + TimeSpan.FromSeconds(seconds);

        // Act
        var composing = () => PersonalTask.Compose(
            Identifier,
            User,
            "Send the counter-proposal",
            DueOn,
            new TaskAnnouncement(offset, [Reminder.Create(60)]),
            PersonalTaskOrigin.Asserted,
            sourceMessage: null);

        // Assert
        Assert.Equal("announcement", Assert.Throws<ArgumentOutOfRangeException>(composing).ParamName);
    }

    /// <summary>A task that announces nothing has no hour to name, so asking for one is a fault rather than an answer.</summary>
    [Fact]
    public void RemindsAt_ATaskThatAnnouncesNothing_Refuses()
    {
        // Arrange
        var task = Compose([]);

        // Act
        var asking = void () => task.RemindsAt(Reminder.Create(60));

        // Assert
        Assert.Throws<InvalidOperationException>(asking);
    }

    /// <summary>
    /// A stored row is input from outside this process however it got there, so restoring one refuses the leads
    /// composing refuses. The two run through one helper today, and this is what says so from the outside — a later
    /// change that reordered the validation or special-cased a restored row would otherwise let a corrupted row back
    /// in, and the first thing to notice would be a producer announcing an hour nobody can place.
    /// </summary>
    [Fact]
    public void Restore_ARemindedRowNobodyDated_IsRefused()
    {
        // Act
        var restoring = () => Restore(dueOn: null, Warsaw);

        // Assert
        Assert.Equal("announcement", Assert.Throws<ArgumentException>(restoring).ParamName);
    }

    /// <summary>The offset a stored row states is untrusted for the same reason its leads are, and refused the same way.</summary>
    [Theory]
    [InlineData(15, 0)]
    [InlineData(-13, 0)]
    [InlineData(-15, 0)]
    [InlineData(2, 30)]
    public void Restore_AnOffsetNoDueDayCanRunIn_IsRefused(int hours, int seconds)
    {
        // Arrange
        var offset = TimeSpan.FromHours(hours) + TimeSpan.FromSeconds(seconds);

        // Act
        var restoring = () => Restore(DueOn, offset);

        // Assert
        Assert.Equal("announcement", Assert.Throws<ArgumentOutOfRangeException>(restoring).ParamName);
    }

    private static PersonalTask Restore(DateOnly? dueOn, TimeSpan offset) => PersonalTask.Restore(
        Identifier,
        User,
        "Send the counter-proposal",
        dueOn,
        new TaskAnnouncement(offset, [Reminder.Create(60)]),
        PersonalTaskOrigin.Asserted,
        sourceMessage: null,
        isCompleted: false);

    private static PersonalTask Announcing(int minutesBefore) => Compose([Reminder.Create(minutesBefore)]);

    private static PersonalTask Compose(Reminder[] reminders) => PersonalTask.Compose(
        Identifier,
        User,
        "Send the counter-proposal",
        DueOn,
        reminders.Length == 0 ? TaskAnnouncement.Silent : new TaskAnnouncement(Warsaw, reminders),
        PersonalTaskOrigin.Asserted,
        sourceMessage: null);
}
