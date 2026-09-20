// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;

namespace MailFathom.Application.Calendar;

/// <summary>One reminder that has come due, with everything announcing it takes and nothing else.</summary>
/// <remarks>
/// <para>
/// The event's title travels here because the notification's headline is what the event is called, and reading the
/// event a second time to learn it would be one query per reminder. Nothing else about the event does: the run has no
/// use for its origin, its end, or the message it cites, and a record that carried them would be a second copy of the
/// calendar passing through a background pass.
/// </para>
/// <para>
/// <see cref="DueAt" /> is the instant the reminder falls at as the calendar now stands, which is what the claim is
/// made against — so a reader holding one of these is holding the answer to <em>which occurrence</em> it is
/// announcing rather than only which reminder.
/// </para>
/// <para>
/// The title is personal data and the owner is who it belongs to. Neither is logged, made a metric dimension, or put
/// into a failure message; <see cref="Event" /> is what a failure names.
/// </para>
/// </remarks>
/// <param name="Owner">The person whose calendar holds the event.</param>
/// <param name="Event">The event the reminder is on.</param>
/// <param name="Title">What that event is called.</param>
/// <param name="Reminder">The lead the person set.</param>
/// <param name="DueAt">The instant that lead falls at, as the calendar now stands.</param>
public sealed record DueCalendarReminder(
    MailUserId Owner,
    CalendarEventId Event,
    CalendarEventTitle Title,
    CalendarReminder Reminder,
    DateTimeOffset DueAt);
