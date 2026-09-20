// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One lead somebody asked to be reminded of an event at, and the instant it currently falls at.</summary>
/// <remarks>
/// <para>
/// A row per lead rather than a list on the event, because the one query that matters here is the deployment-wide
/// question <em>what has come due</em> — asked every interval, across every calendar, and answerable from an index
/// only if the instant is a column of its own.
/// </para>
/// <para>
/// Only an unannounced reminder is ever read, so the index the producer's query rides is partial on the claim being
/// absent — which leaves out every reminder of every event already past, and that is nearly all of them once a
/// calendar has any history.
/// </para>
/// <para>
/// <see cref="DueAt" /> is derived from the event and written beside the lead rather than computed in the query: the
/// anchor an all-day event is measured from is a rule in the domain, and a database expression restating it would be
/// the same rule written twice in two languages. Every write of the event writes it again, which is what makes a
/// moved event's reminders move with it.
/// </para>
/// <para>
/// <see cref="RaisedForDueAt" /> is the claim, and it holds the instant announced rather than a bare flag: two
/// replicas reading one pass together both try to record it, and the write each of them makes is conditional on the
/// instant it read, so the one that lost is told so instead of announcing a second time. An event whose move changes
/// <see cref="DueAt" /> has the claim cleared with the same write that moves it, which is what makes it due again at
/// its new time; a move that leaves the instant where it was clears nothing and stays quiet.
/// </para>
/// <para>
/// The row keys onto the event and cascades from it, which is the whole of what the delete confirmation promises: an
/// event deleted takes its reminders with it, and nothing is left to announce a commitment that is gone.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class CalendarEventReminderEntity
{
    /// <summary>Gets or sets the event this reminder is on.</summary>
    public Guid CalendarEventId { get; set; }

    /// <summary>Gets or sets the event this reminder is on.</summary>
    public CalendarEventEntity? CalendarEvent { get; set; }

    /// <summary>Gets or sets how many minutes before the event the person asked to be reminded, which is zero at the event itself.</summary>
    public int MinutesBefore { get; set; }

    /// <summary>Gets or sets the instant this reminder falls at as the event currently stands.</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>Gets or sets the instant this reminder was announced for, and <see langword="null" /> while it has never been announced.</summary>
    public DateTimeOffset? RaisedForDueAt { get; set; }
}
