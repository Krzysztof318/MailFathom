// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Calendar;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Calendar;

/// <summary>Keeps the calendars in PostgreSQL, one person's at a time.</summary>
/// <remarks>
/// <para>
/// Every statement here carries the owner beside whatever identity it was given, so an event of somebody else's
/// calendar matches no row rather than being reached: a read, an amendment, and a deletion are equally unable to be
/// aimed across calendars by a caller that learned an identifier elsewhere.
/// </para>
/// <para>
/// The three writes go through the context enlisted in the caller's session, so nothing lands outside the transaction
/// that caller opened; the two reads use the scoped context and track nothing. Nothing logs: a title says who somebody
/// is meeting and the times say where they are not, so what a failure carries is the identifier.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class CalendarEventStore(MailFathomDbContext context) : ICalendarEventStore
{
    /// <inheritdoc />
    public async Task<CalendarEvent?> ReadAsync(
        UserId owner,
        CalendarEventId eventId,
        CancellationToken cancellationToken)
    {
        RequireNamed(owner);

        var ownerValue = owner.Value;
        var eventValue = eventId.Value;

        var stored = await context.CalendarEvents
            .AsNoTracking()
            .Include(calendarEvent => calendarEvent.Reminders)
            .FirstOrDefaultAsync(
                calendarEvent => calendarEvent.Id == eventValue && calendarEvent.UserId == ownerValue,
                cancellationToken);

        return stored is null ? null : CalendarEventMapping.ToDomain(stored);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEvent>> ReadRangeAsync(
        UserId owner,
        CalendarEventQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        RequireNamed(owner);

        var ownerValue = owner.Value;
        var from = query.From;
        var until = query.Until;

        // An event is in the window when any part of it is, which is two shapes rather than one: an event that states
        // an end overlaps while that end is still ahead of the window's opening, and one that states none is the
        // instant it begins. Written as one comparison over a coalesced end, an event ending exactly as the window
        // opens would be drawn in both of two consecutive windows.
        var window = context.CalendarEvents
            .AsNoTracking()
            .Include(calendarEvent => calendarEvent.Reminders)
            .Where(calendarEvent => calendarEvent.UserId == ownerValue && calendarEvent.StartsAt < until)
            .Where(calendarEvent =>
                (calendarEvent.EndsAt == null && calendarEvent.StartsAt >= from)
                || (calendarEvent.EndsAt != null && calendarEvent.EndsAt > from));

        if (query.Origin is { } origin)
        {
            window = window.Where(calendarEvent => calendarEvent.Origin == origin);
        }

        var stored = await window
            .OrderBy(calendarEvent => calendarEvent.StartsAt)
            .ThenBy(calendarEvent => calendarEvent.Id)
            .Take(query.Count)
            .ToArrayAsync(cancellationToken);

        return [.. stored.Select(CalendarEventMapping.ToDomain)];
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<ImportedCalendarEventUid>> ReadImportedUidsAsync(
        UserId owner,
        IReadOnlyCollection<ImportedCalendarEventUid> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        RequireNamed(owner);

        if (candidates.Count == 0)
        {
            return new HashSet<ImportedCalendarEventUid>();
        }

        var ownerValue = owner.Value;
        var stated = candidates.Select(uid => uid.Value).ToArray();

        // Only the column the answer is made of, so a file naming five hundred identifiers reads five hundred strings
        // rather than five hundred events and their reminders. The partial unique index on the pair is what serves it.
        var alreadyHeld = await context.CalendarEvents
            .AsNoTracking()
            .Where(calendarEvent =>
                calendarEvent.UserId == ownerValue
                && calendarEvent.ImportedUid != null
                && stated.Contains(calendarEvent.ImportedUid))
            .Select(calendarEvent => calendarEvent.ImportedUid!)
            .ToArrayAsync(cancellationToken);

        return alreadyHeld.Select(ImportedCalendarEventUid.Create).ToHashSet();
    }

    /// <inheritdoc />
    public async Task AddAsync(
        IPersistenceSession session,
        UserId owner,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(calendarEvent);
        RequireNamed(owner);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        writeContext.CalendarEvents.Add(CalendarEventMapping.ToEntity(owner, calendarEvent));
    }

    /// <inheritdoc />
    public async Task<bool> ReplaceAsync(
        IPersistenceSession session,
        UserId owner,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(calendarEvent);
        RequireNamed(owner);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var ownerValue = owner.Value;
        var eventValue = calendarEvent.Id.Value;

        // Tracked rather than projected, so the amendment is applied to the row as it stands and the concurrency token
        // travels with it: an event deleted between this read and the commit makes the write affect no row, which the
        // session reports as a conflict rather than writing the event back.
        var held = await writeContext.CalendarEvents
            .Include(stored => stored.Reminders)
            .FirstOrDefaultAsync(
                stored => stored.Id == eventValue && stored.UserId == ownerValue,
                cancellationToken);

        if (held is null)
        {
            return false;
        }

        held.Title = calendarEvent.Title.Value;
        held.StartsAt = calendarEvent.Start;
        held.EndsAt = calendarEvent.End;
        held.IsAllDay = calendarEvent.IsAllDay;
        held.Origin = calendarEvent.Origin;
        held.SourceStoredEmailId = calendarEvent.SourceMessage?.Value;
        held.ImportedUid = calendarEvent.ImportedUid?.Value;
        held.AmendedAt = calendarEvent.AmendedAt;

        Reconcile(writeContext, held, calendarEvent);

        return true;
    }

    /// <summary>Brings the held reminder rows to what the amended event states, keeping what each one already announced.</summary>
    /// <remarks>
    /// Reconciled rather than replaced, because a row carries the claim that it has already been announced and
    /// deleting it to write it back would announce every reminder a second time on the next pass. A lead the
    /// amendment keeps therefore keeps its row, and the claim on that row is cleared exactly when the instant the
    /// reminder falls at has moved — which is what makes an event moved forward reminded at its new time and an
    /// event merely retitled stay quiet.
    /// </remarks>
    private static void Reconcile(
        MailFathomDbContext writeContext,
        CalendarEventEntity held,
        CalendarEvent calendarEvent)
    {
        var stated = calendarEvent.Reminders.Select(reminder => reminder.MinutesBefore).ToHashSet();

        foreach (var dropped in held.Reminders.Where(row => !stated.Contains(row.MinutesBefore)).ToArray())
        {
            writeContext.CalendarEventReminders.Remove(dropped);
        }

        foreach (var reminder in calendarEvent.Reminders)
        {
            var dueAt = calendarEvent.RemindsAt(reminder);
            var row = held.Reminders.FirstOrDefault(row => row.MinutesBefore == reminder.MinutesBefore);

            if (row is null)
            {
                held.Reminders.Add(CalendarEventMapping.ToEntity(calendarEvent, reminder));
            }
            else if (row.DueAt != dueAt)
            {
                row.DueAt = dueAt;
                row.RaisedForDueAt = null;
            }
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        IPersistenceSession session,
        UserId owner,
        CalendarEventId eventId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        RequireNamed(owner);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var ownerValue = owner.Value;
        var eventValue = eventId.Value;

        var removed = await writeContext.CalendarEvents
            .Where(calendarEvent => calendarEvent.Id == eventValue && calendarEvent.UserId == ownerValue)
            .ExecuteDeleteAsync(cancellationToken);

        return removed > 0;
    }

    private static void RequireNamed(UserId owner)
    {
        if (!owner.IsSpecified)
        {
            throw new ArgumentException(
                "A calendar is read and written for a named person, and the value names nobody.",
                nameof(owner));
        }
    }
}
