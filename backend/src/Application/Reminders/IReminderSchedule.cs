// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Reminders;

/// <summary>Answers which reminders of one kind have come due across the deployment, and records the ones announced.</summary>
/// <remarks>
/// <para>
/// This is the one port here that is not one person's. A reminder comes due whether or not anybody is signed in, so
/// what reads it is a run over the deployment rather than a request with a principal behind it — which is why the
/// reads name no owner and why <see cref="ReminderSweep" /> is the only caller, under the process identity and under
/// a lease.
/// </para>
/// <para>
/// One implementation per kind of thing that carries reminders, because each reads a table of its own and answers
/// for its own record's rules — which reminders a completed task raises, what an all-day event is measured from. The
/// pass holds every implementation registered and reads each in turn, which is what lets a kind be added without a
/// producer of its own.
/// </para>
/// <para>
/// A reminder is announced at most once per instant it falls at, and the instant is the claim:
/// <see cref="MarkRaisedAsync" /> records which one was announced rather than that something was, so a record moved
/// to a new time has reminders that are due again on their own, and one moved back onto a time it already announced
/// stays quiet. Nothing here holds a flag, because a flag would have to be cleared by whoever moved the record and a
/// writer that forgot would silence the reminder for good.
/// </para>
/// </remarks>
public interface IReminderSchedule
{
    /// <summary>Gets which kind of thing this schedule's reminders are about.</summary>
    /// <remarks>
    /// A constant per implementation rather than a value read per row, because a schedule reads one table and every
    /// reminder in it is about the same kind of record.
    /// </remarks>
    ReminderSubject Subject { get; }

    /// <summary>Reads the reminders that have come due and have not been announced at the instant they now fall at.</summary>
    /// <param name="asOf">The instant a reminder must have fallen at or before to be due.</param>
    /// <param name="notDueBefore">The instant a reminder must have fallen at or after to still be worth announcing.</param>
    /// <param name="limit">The greatest number of reminders one pass may read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The reminders, earliest first, and never more than <paramref name="limit" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <remarks>
    /// The lower bound is what keeps a deployment that was off for a day from announcing that day's reminders the
    /// moment it comes back: a reminder nobody could have acted on is left where it is rather than delivered late in a
    /// burst, and the thing it was about has already happened.
    /// </remarks>
    Task<IReadOnlyList<DueReminder>> ReadDueAsync(
        DateTimeOffset asOf,
        DateTimeOffset notDueBefore,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Records that one reminder was announced for the instant it currently falls at.</summary>
    /// <param name="due">The reminder as it was read, which names the instant the claim is made against.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns><see langword="true" /> when this call is what recorded it; <see langword="false" /> when the reminder is gone or already stands announced for that instant.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="due" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Conditional on the instant rather than unconditional, so two replicas reading one pass at the same time is the
    /// ordinary case it is meant to be: the one that loses is told so and writes nothing.
    /// </remarks>
    Task<bool> MarkRaisedAsync(DueReminder due, CancellationToken cancellationToken);
}
