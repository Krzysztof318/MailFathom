// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Calendar;

namespace MailFathom.Application.Calendar;

/// <summary>What became of a write to somebody's calendar, and the event as it stands where one was written.</summary>
/// <remarks>
/// The event travels with the outcome because every write here answers with the record as it is to be drawn: a caller
/// that had to read it back would draw a calendar one round trip behind the act it just performed. A refusal carries
/// none, which is what keeps a refused write from reporting anything about the event it did not change.
/// </remarks>
public sealed record CalendarEventWriteResult
{
    private CalendarEventWriteResult(CalendarEventWriteOutcome outcome, CalendarEvent? calendarEvent)
    {
        this.Outcome = outcome;
        this.Event = calendarEvent;
    }

    /// <summary>Gets what became of the write.</summary>
    public CalendarEventWriteOutcome Outcome { get; }

    /// <summary>Gets the event as the calendar now holds it, or <see langword="null" /> where the write was refused.</summary>
    public CalendarEvent? Event { get; }

    /// <summary>Gets the result of a write that named an event the caller's calendar does not hold.</summary>
    public static CalendarEventWriteResult NotFound { get; } = new(CalendarEventWriteOutcome.NotFound, null);

    /// <summary>Gets the result of a write whose title is not one.</summary>
    public static CalendarEventWriteResult TitleRefused { get; } = new(CalendarEventWriteOutcome.TitleRefused, null);

    /// <summary>Gets the result of a write stating an end that is not after its start.</summary>
    public static CalendarEventWriteResult EndNotAfterStart { get; } =
        new(CalendarEventWriteOutcome.EndNotAfterStart, null);

    /// <summary>Gets the result of an acceptance of an event that is already on the calendar.</summary>
    public static CalendarEventWriteResult AlreadyOnTheCalendar { get; } =
        new(CalendarEventWriteOutcome.AlreadyOnTheCalendar, null);

    /// <summary>Gets the result of a write stating reminders the calendar will not hold.</summary>
    public static CalendarEventWriteResult RemindersRefused { get; } =
        new(CalendarEventWriteOutcome.RemindersRefused, null);

    /// <summary>States that the calendar holds the event as supplied.</summary>
    /// <param name="calendarEvent">The event as it now stands.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="calendarEvent" /> is <see langword="null" />.</exception>
    public static CalendarEventWriteResult Written(CalendarEvent calendarEvent)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);

        return new CalendarEventWriteResult(CalendarEventWriteOutcome.Written, calendarEvent);
    }
}
