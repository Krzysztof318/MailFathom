// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Calendar;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Calendar;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds calendars in memory, one person's at a time, with the window semantics the persisted store has.</summary>
/// <remarks>
/// Every operation carries the owner beside the identity it was given, exactly as the persisted store does, so a use
/// case that forgot to name whose calendar it was acting on reaches nobody's rather than everybody's. The window is
/// applied here rather than recorded and ignored, because what a caller asks for and what comes back is the behaviour
/// the use case above is read for; the query is kept beside it so a test can assert the bound the use case composed.
/// </remarks>
internal sealed class InMemoryCalendarEventStore : ICalendarEventStore
{
    private readonly Dictionary<(Guid Owner, Guid Event), CalendarEvent> held = [];

    /// <summary>Gets the window the last range read was composed with.</summary>
    internal CalendarEventQuery? LastQuery { get; private set; }

    /// <summary>Gets how often the imported identifiers this calendar holds have been asked for.</summary>
    /// <remarks>
    /// Counted because a caller that has to decide again from a fresh read — an import whose commit lost a race — is
    /// only doing so if it asked again, and nothing else it produces distinguishes that from replaying its first read.
    /// </remarks>
    internal int ImportedUidReads { get; private set; }

    /// <summary>Puts an event into somebody's calendar without a session, which is how a test arranges one.</summary>
    /// <param name="owner">Whose calendar holds it.</param>
    /// <param name="calendarEvent">The event.</param>
    internal void Hold(MailUserId owner, CalendarEvent calendarEvent) =>
        this.held[(owner.Value, calendarEvent.Id.Value)] = calendarEvent;

    /// <inheritdoc />
    public Task<CalendarEvent?> ReadAsync(
        MailUserId owner,
        CalendarEventId eventId,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.held.GetValueOrDefault((owner.Value, eventId.Value)));

    /// <inheritdoc />
    public Task<IReadOnlyList<CalendarEvent>> ReadRangeAsync(
        MailUserId owner,
        CalendarEventQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        this.LastQuery = query;

        IReadOnlyList<CalendarEvent> window =
        [
            .. this.held
                .Where(entry => entry.Key.Owner == owner.Value)
                .Select(entry => entry.Value)
                .Where(calendarEvent => Overlaps(calendarEvent, query))
                .Where(calendarEvent => query.Origin is not { } origin || calendarEvent.Origin == origin)
                .OrderBy(calendarEvent => calendarEvent.Start)
                .ThenBy(calendarEvent => calendarEvent.Id.Value)
                .Take(query.Count),
        ];

        return Task.FromResult(window);
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<ImportedCalendarEventUid>> ReadImportedUidsAsync(
        MailUserId owner,
        IReadOnlyCollection<ImportedCalendarEventUid> candidates,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        this.ImportedUidReads++;

        IReadOnlySet<ImportedCalendarEventUid> alreadyHeld = this.held
            .Where(entry => entry.Key.Owner == owner.Value)
            .Select(entry => entry.Value.ImportedUid)
            .OfType<ImportedCalendarEventUid>()
            .Where(candidates.Contains)
            .ToHashSet();

        return Task.FromResult(alreadyHeld);
    }

    /// <inheritdoc />
    public Task AddAsync(
        IPersistenceSession session,
        MailUserId owner,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(calendarEvent);

        this.held[(owner.Value, calendarEvent.Id.Value)] = calendarEvent;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ReplaceAsync(
        IPersistenceSession session,
        MailUserId owner,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(calendarEvent);

        var key = (owner.Value, calendarEvent.Id.Value);

        if (!this.held.ContainsKey(key))
        {
            return Task.FromResult(false);
        }

        this.held[key] = calendarEvent;

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(
        IPersistenceSession session,
        MailUserId owner,
        CalendarEventId eventId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        return Task.FromResult(this.held.Remove((owner.Value, eventId.Value)));
    }

    /// <summary>Reports whether any part of an event falls inside the window, which is the half-open reading the persisted store performs.</summary>
    private static bool Overlaps(CalendarEvent calendarEvent, CalendarEventQuery query) =>
        calendarEvent.Start < query.Until
        && (calendarEvent.End is { } ends ? ends > query.From : calendarEvent.Start >= query.From);
}
