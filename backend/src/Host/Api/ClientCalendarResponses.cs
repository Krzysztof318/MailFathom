// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Calendar;

namespace MailFathom.Host.Api;

/// <summary>The event a caller states for their own calendar.</summary>
/// <param name="Title">What the event is called.</param>
/// <param name="Start">When it begins.</param>
/// <param name="End">When it ends, or nothing to state no end.</param>
/// <param name="IsAllDay">Whether the event is stated as a day rather than as a clock time; a request naming nothing states a clock time.</param>
/// <param name="Reminders">The leads to announce it at, in minutes before it, or nothing to announce nothing.</param>
/// <param name="SourceMessage">The message it was created from, or nothing where none was open.</param>
/// <remarks>
/// It is its own shape rather than the amendment's with one more field, because the message is stated once and never
/// again: an amendment that carried it would be offering a caller a field the calendar silently ignores.
/// </remarks>
internal sealed record CalendarEventCreationRequest(
    string? Title,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    bool IsAllDay,
    IReadOnlyList<int>? Reminders,
    Guid? SourceMessage);

/// <summary>The event a caller states one of their own is to stand as.</summary>
/// <param name="Title">What the event is called afterwards.</param>
/// <param name="Start">When it begins afterwards.</param>
/// <param name="End">When it ends afterwards, or nothing to hold no end.</param>
/// <param name="IsAllDay">Whether it is stated as a day rather than as a clock time afterwards.</param>
/// <param name="Reminders">The leads it is announced at afterwards, in minutes before it, or nothing to announce nothing.</param>
/// <remarks>
/// The whole record rather than the difference from the one held, so a caller sending only what changed is sending a
/// record that is missing the rest. The reminders are part of that record, which is what makes turning the last one
/// off an event stated with none rather than a field left out. What it cannot state is the identity, the origin, or
/// the message the event cites: an amendment changes none of those.
/// </remarks>
internal sealed record CalendarEventAmendmentRequest(
    string? Title,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    bool IsAllDay,
    IReadOnlyList<int>? Reminders);

/// <summary>One event as the calendar holds it.</summary>
/// <param name="Id">The identity this deployment gave it, which neither an amendment nor an acceptance changes.</param>
/// <param name="Title">What the event is called, as whoever wrote it down wrote it.</param>
/// <param name="Start">When it begins.</param>
/// <param name="End">When it ends, or nothing where nothing said how long it lasts.</param>
/// <param name="IsAllDay">Whether the event is stated as a day rather than as a clock time.</param>
/// <param name="Reminders">The leads it is announced at, in minutes before it, longest first, and empty where nothing announces it.</param>
/// <param name="RemindsAt">The instants those leads fall at, in the same order, which an all-day event measures from nine in the morning rather than from midnight.</param>
/// <param name="Origin">Whether the event is on the calendar or offered to it, as <c>Asserted</c> or <c>Proposed</c>.</param>
/// <param name="SourceMessage">The message it came out of, or nothing where no message named it.</param>
/// <param name="RecordedAt">When it was first written here.</param>
/// <param name="AmendedAt">When it was last amended or accepted, which equals <paramref name="RecordedAt" /> until one happens.</param>
/// <remarks>
/// <para>
/// The instants the reminders fall at travel beside the leads rather than being left to the client to derive, because
/// what an all-day event is measured from is a rule this deployment owns: a client computing it would be the second
/// place that rule is written, and the two would disagree the first time one of them changed.
/// </para>
/// <para>
/// The identifier the event was imported under is deliberately absent. It exists for the one question whoever reads an
/// <c>.ics</c> file asks — whether an entry is already here — and nothing a client draws reads it, so it stays behind
/// the surface rather than travelling with every event on a screen.
/// </para>
/// <para>
/// The title and the times are personal data: they say who somebody is meeting and when they are not somewhere else.
/// They travel to the person whose calendar this is and reach nothing else — no log line, no metric dimension, and no
/// failure message.
/// </para>
/// </remarks>
internal sealed record CalendarEventResponse(
    Guid Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset? End,
    bool IsAllDay,
    IReadOnlyList<int> Reminders,
    IReadOnlyList<DateTimeOffset> RemindsAt,
    string Origin,
    Guid? SourceMessage,
    DateTimeOffset RecordedAt,
    DateTimeOffset AmendedAt)
{
    /// <summary>Describes one event for the person whose calendar holds it.</summary>
    /// <param name="calendarEvent">The event as the calendar holds it.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="calendarEvent" /> is <see langword="null" />.</exception>
    internal static CalendarEventResponse For(CalendarEvent calendarEvent)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);

        return new CalendarEventResponse(
            calendarEvent.Id.Value,
            calendarEvent.Title.Value,
            calendarEvent.Start,
            calendarEvent.End,
            calendarEvent.IsAllDay,
            [.. calendarEvent.Reminders.Select(reminder => reminder.MinutesBefore)],
            [.. calendarEvent.Reminders.Select(calendarEvent.RemindsAt)],
            calendarEvent.Origin.ToString(),
            calendarEvent.SourceMessage?.Value,
            calendarEvent.RecordedAt,
            calendarEvent.AmendedAt);
    }
}

/// <summary>One window of a calendar, earliest first.</summary>
/// <param name="Events">The events any part of which falls inside the window, never more than it asked for.</param>
/// <remarks>
/// There is no cursor here and no total. A window is bounded by how many events it answers with rather than by how
/// long it is, so a caller that came back with as many as it asked for widens nothing and asks about a shorter span
/// instead — and a count over the whole calendar would be a second query nobody drew.
/// </remarks>
internal sealed record CalendarWindowResponse(IReadOnlyList<CalendarEventResponse> Events);
