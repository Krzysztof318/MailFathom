// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One lead somebody asked to be reminded of a task at, and the instant it currently falls at.</summary>
/// <remarks>
/// <para>
/// A row per lead rather than a list on the task, because the one query that matters here is the deployment-wide
/// question <em>what has come due</em> — asked every interval, across every list, and answerable from an index only
/// if the instant is a column of its own. It is the same arrangement the calendar's reminders take, for the same
/// reason.
/// </para>
/// <para>
/// Only an unannounced reminder is ever read, so the index the producer's query rides is partial on the claim being
/// absent — which leaves out every reminder of every task already past, and that is nearly all of them once a list
/// has any history.
/// </para>
/// <para>
/// <see cref="DueAt" /> is derived from the task and written beside the lead rather than computed in the query: the
/// hour a due day is measured back from, and the offset that day is read in, are rules in the domain, and a database
/// expression restating them would be the same rules written twice in two languages. Every write of the task writes
/// it again, which is what makes a due date somebody moved carry its reminders with it.
/// </para>
/// <para>
/// <see cref="RaisedForDueAt" /> is the claim, and it holds the instant announced rather than a bare flag: two
/// replicas reading one pass together both try to record it, and the write each of them makes is conditional on the
/// instant it read, so the one that lost is told so instead of announcing a second time. A task whose due date moves
/// has the claim cleared with the same write that moves it, which is what makes it due again on its new day; a
/// rename or a completion leaves the instant where it was and clears nothing.
/// </para>
/// <para>
/// Whether the task is done is deliberately not a column here. Completion moves on the task's own row, and the
/// producer's query reads it through the association rather than through a copy that a completion would have to
/// remember to update.
/// </para>
/// <para>
/// The row keys onto the task and cascades from it, so a task erased takes its reminders with it and nothing is left
/// to announce a commitment that is gone.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class PersonalTaskReminderEntity
{
    /// <summary>Gets or sets the task this reminder is on.</summary>
    public Guid PersonalTaskId { get; set; }

    /// <summary>Gets or sets the task this reminder is on.</summary>
    public PersonalTaskEntity? PersonalTask { get; set; }

    /// <summary>Gets or sets how many minutes before the task's anchor the person asked to be reminded, which is zero at that hour itself.</summary>
    public int MinutesBefore { get; set; }

    /// <summary>Gets or sets the instant this reminder falls at as the task currently stands.</summary>
    public DateTimeOffset DueAt { get; set; }

    /// <summary>Gets or sets the instant this reminder was announced for, and <see langword="null" /> while it has never been announced.</summary>
    public DateTimeOffset? RaisedForDueAt { get; set; }
}
