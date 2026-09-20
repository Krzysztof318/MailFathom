// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Reminders;
using Xunit;

namespace MailFathom.Domain.UnitTests.Reminders;

/// <summary>Covers the leads a reminder may state, and the one a surface asks about before somebody is refused.</summary>
public sealed class ReminderTests
{
    /// <summary>A lead is whole minutes, and what it is worth as a span is what a producer measures back with.</summary>
    [Fact]
    public void Create_AStatedLead_KeepsItAsMinutesAndAsASpan()
    {
        // Act
        var reminder = Reminder.Create(90);

        // Assert
        Assert.Equal(90, reminder.MinutesBefore);
        Assert.Equal(TimeSpan.FromMinutes(90), reminder.Lead);
    }

    /// <summary>Zero is the event itself rather than the absence of a reminder, which is an event carrying none.</summary>
    [Fact]
    public void Create_NoLeadAtAll_IsTheEventItself()
    {
        // Act
        var reminder = Reminder.Create(0);

        // Assert
        Assert.Equal(TimeSpan.Zero, reminder.Lead);
    }

    /// <summary>The lead decides how far ahead of now a run has to look, so an unbounded one reads the whole calendar.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(Reminder.MaximumMinutesBefore + 1)]
    public void Create_ALeadNoReminderMayState_IsRefused(int minutesBefore)
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => Reminder.Create(minutesBefore));

        // Assert
        Assert.Equal("minutesBefore", refusal.ParamName);
    }

    /// <summary>A surface reading what somebody typed asks rather than catching, so the answer agrees with the refusal.</summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(Reminder.MaximumMinutesBefore, true)]
    [InlineData(-1, false)]
    [InlineData(Reminder.MaximumMinutesBefore + 1, false)]
    public void IsStatable_ALeadAsSupplied_AnswersWhatCreateWould(int minutesBefore, bool statable)
    {
        // Act, Assert
        Assert.Equal(statable, Reminder.IsStatable(minutesBefore));
    }

    /// <summary>Two reminders are the same reminder where they state the same lead, which is what refuses a repeated one.</summary>
    [Fact]
    public void Equals_TwoRemindersStatingOneLead_AreOneReminder()
    {
        // Act, Assert
        Assert.Equal(Reminder.Create(15), Reminder.Create(15));
        Assert.NotEqual(Reminder.Create(15), Reminder.Create(30));
    }
}
