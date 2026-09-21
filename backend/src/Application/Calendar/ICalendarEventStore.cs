// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;

namespace MailFathom.Application.Calendar;

/// <summary>Keeps one person's calendar: what they put on it, and what mail proposed to it.</summary>
/// <remarks>
/// <para>
/// A calendar belongs to the person whose it is rather than to a mailbox, because a person with two mail accounts has
/// one day. So every operation here names the owner beside whatever else it was given, and the store applies it rather
/// than trusting the record it was handed: an identifier naming somebody else's event reaches a calendar that holds no
/// such event, which makes a read, an amendment, and a deletion equally unable to be aimed across owners by a caller
/// who learned an identifier elsewhere.
/// </para>
/// <para>
/// The three writes stage through the caller's session and commit nothing, as
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
/// requires of a write port: the caller decides the transaction, and the retry policy above it decides what a lost race
/// means. The two reads join none, so they take no session.
/// </para>
/// <para>
/// Accepting a proposal is not an operation here. It is <see cref="CalendarEvent.Accepted" /> applied to the event and
/// written back through <see cref="ReplaceAsync" />, because what acceptance changes is the event rather than the
/// store's idea of it — and dismissing one is <see cref="DeleteAsync" />, since a proposal nobody wants is a row
/// nobody is keeping.
/// </para>
/// </remarks>
public interface ICalendarEventStore
{
    /// <summary>Reads one event of somebody's calendar.</summary>
    /// <param name="owner">The person whose calendar is read.</param>
    /// <param name="eventId">The event to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The event, or <see langword="null" /> when that calendar holds no such event.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    Task<CalendarEvent?> ReadAsync(MailUserId owner, CalendarEventId eventId, CancellationToken cancellationToken);

    /// <summary>Reads the events of somebody's calendar that fall in one window, earliest first.</summary>
    /// <param name="owner">The person whose calendar is read.</param>
    /// <param name="query">The window, what it is narrowed to, and how many events it may answer with.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The events, ordered by when they begin and then by their identity, and never more than the window asked for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="query" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// The identity settles two events beginning at the same instant, which makes the order total and therefore the
    /// same on every read of one window rather than whichever order the database answered in.
    /// </remarks>
    Task<IReadOnlyList<CalendarEvent>> ReadRangeAsync(
        MailUserId owner,
        CalendarEventQuery query,
        CancellationToken cancellationToken);

    /// <summary>Reports which of the stated imported identifiers somebody's calendar already holds.</summary>
    /// <param name="owner">The person whose calendar is read.</param>
    /// <param name="candidates">The identifiers an offered file names, which bounds the read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The subset of <paramref name="candidates" /> that calendar already holds, which is empty where it holds none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidates" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// Asked about the identifiers of one file rather than about the calendar, so the read is bounded by what somebody
    /// chose to import instead of growing with the events they have imported before. It is what turns a second import
    /// of one file into a count of entries skipped rather than a doubled calendar, and it is not what makes that
    /// safe — the unique index on <see cref="ICalendarEventStore" />'s own insert is, because two imports running at
    /// once both read nothing here.
    /// </remarks>
    Task<IReadOnlySet<ImportedCalendarEventUid>> ReadImportedUidsAsync(
        MailUserId owner,
        IReadOnlyCollection<ImportedCalendarEventUid> candidates,
        CancellationToken cancellationToken);

    /// <summary>Stages an event the calendar does not yet hold.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="owner">The person whose calendar the event is written into.</param>
    /// <param name="calendarEvent">The event to add.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns>A task that completes once the insert is staged; nothing is committed here.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the session cannot supply this store's persistence context, or when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// That one calendar holds an imported identifier once is a unique index rather than a check before the insert:
    /// two imports of one file read nothing twice, so only the constraint closes that window, and the second writer
    /// is refused by the database instead of doubling the calendar.
    /// </remarks>
    Task AddAsync(
        IPersistenceSession session,
        MailUserId owner,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken);

    /// <summary>Stages the held event being replaced by the one supplied.</summary>
    /// <param name="session">The session the write joins.</param>
    /// <param name="owner">The person whose calendar holds the event being replaced.</param>
    /// <param name="calendarEvent">The event as it is to stand, identified by its own identity.</param>
    /// <param name="cancellationToken">Cancels the staging.</param>
    /// <returns><see langword="true" /> when that calendar held the event and the replacement was staged; <see langword="false" /> when it holds none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the session cannot supply this store's persistence context, or when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// This is how an amendment and an acceptance are both written, since both answer with the event as it is to stand.
    /// An event deleted while either was in flight is a write affecting no row, which the row's concurrency token turns
    /// into a conflict — so the retry reads a calendar that holds nothing and answers so rather than putting the event
    /// back.
    /// </remarks>
    Task<bool> ReplaceAsync(
        IPersistenceSession session,
        MailUserId owner,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken);

    /// <summary>Removes one event of somebody's calendar outright, inside the caller's transaction.</summary>
    /// <param name="session">The session the deletion joins.</param>
    /// <param name="owner">The person whose calendar the event is removed from.</param>
    /// <param name="eventId">The event to remove.</param>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns><see langword="true" /> when that calendar held the event and it was removed; <see langword="false" /> when it holds none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the session cannot supply this store's persistence context, or when <paramref name="owner" /> names nobody.</exception>
    /// <remarks>
    /// The row goes rather than being marked, which is what deleting an event means here: there is no state a deleted
    /// event is in, nothing restores one, and a calendar that appears to hold nothing at a date holds nothing at it.
    /// Dismissing a proposal is this operation, for the same reason — a date nobody wanted is not a fact worth keeping.
    /// </remarks>
    Task<bool> DeleteAsync(
        IPersistenceSession session,
        MailUserId owner,
        CalendarEventId eventId,
        CancellationToken cancellationToken);
}
