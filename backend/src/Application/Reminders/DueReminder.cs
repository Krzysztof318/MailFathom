// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Reminders;

namespace MailFathom.Application.Reminders;

/// <summary>One reminder that has come due, with everything announcing it takes and nothing else.</summary>
/// <remarks>
/// <para>
/// The headline travels here because the notification's own headline is what the thing is called, and reading the
/// event or the task a second time to learn it would be one query per reminder. Nothing else about that record
/// does: the run has no use for an event's origin or a task's completion, and a record that carried them would be a
/// second copy of the calendar and the task list passing through a background pass.
/// </para>
/// <para>
/// <see cref="Subject" /> is what decides the shape of the notification — its kind, what it leads to, and the name
/// its deduplication key is written under — and <see cref="Identity" /> is the record it is about within that kind.
/// The two are kept apart from the headline so that composing the notification stays one reading of a closed set
/// rather than a guess at which identifier happens to be present.
/// </para>
/// <para>
/// <see cref="DueAt" /> is the instant the reminder falls at as the record now stands, which is what the claim is
/// made against — so a reader holding one of these is holding the answer to <em>which occurrence</em> it is
/// announcing rather than only which reminder.
/// </para>
/// <para>
/// The headline is personal data and the owner is who it belongs to. Neither is logged, made a metric dimension, or
/// put into a failure message; <see cref="Identity" /> is what a failure names.
/// </para>
/// </remarks>
/// <param name="Owner">The person whose record the reminder is on.</param>
/// <param name="Subject">Which kind of record it is on.</param>
/// <param name="Identity">What addresses that record.</param>
/// <param name="Headline">What that record is called.</param>
/// <param name="Reminder">The lead the person set.</param>
/// <param name="DueAt">The instant that lead falls at, as the record now stands.</param>
public sealed record DueReminder(
    UserId Owner,
    ReminderSubject Subject,
    Guid Identity,
    string Headline,
    Reminder Reminder,
    DateTimeOffset DueAt);
