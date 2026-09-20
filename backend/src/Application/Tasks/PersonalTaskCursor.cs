// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>Marks where one page of a person's tasks ended, so the next page continues from it.</summary>
/// <remarks>
/// <para>
/// The list is ordered by the day a task is due with the identifier breaking a tie, and this pairs those two values.
/// That is what makes the walk keyset-based rather than offset-based, which matters on a list somebody is editing
/// while they read it: a task completed, dated, or accepted between two pages would shift an offset window and repeat
/// or skip a row on every page after it.
/// </para>
/// <para>
/// A boundary with no day is the undated block, which the order puts last. It is a position like any other rather than
/// an absent one, so continuing from it walks the undated tasks by identifier alone.
/// </para>
/// <para>
/// It carries the position and nothing else. The opaque encoded form every keyset cursor takes at a transport
/// boundary, and the fingerprint that refuses one presented by somebody it was not issued to, belong to the surface
/// that issues it rather than to this port, and arrive with the route that serves this list.
/// </para>
/// </remarks>
public readonly record struct PersonalTaskCursor
{
    private PersonalTaskCursor(DateOnly? dueOn, PersonalTaskId task)
    {
        this.DueOn = dueOn;
        this.Task = task;
    }

    /// <summary>Gets the day the last task the page returned is due on, and <see langword="null" /> for the undated block.</summary>
    public DateOnly? DueOn { get; }

    /// <summary>Gets the identity of that task, which breaks a tie between two due on the same day.</summary>
    public PersonalTaskId Task { get; }

    /// <summary>Creates the cursor that continues a walk after one position in the list.</summary>
    /// <param name="dueOn">The day the page ended on, or <see langword="null" /> where it ended inside the undated block.</param>
    /// <param name="task">The identity of the task at that position.</param>
    /// <returns>The cursor.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="task" /> is the struct default.</exception>
    /// <remarks>
    /// The identity has to name a task, because it is half of the position rather than a decoration on it: a boundary
    /// carrying only a day would repeat or skip every task sharing that day.
    /// </remarks>
    public static PersonalTaskCursor After(DateOnly? dueOn, PersonalTaskId task)
    {
        if (!task.IsSpecified)
        {
            throw new ArgumentException("A cursor continues a walk after a specified task.", nameof(task));
        }

        return new PersonalTaskCursor(dueOn, task);
    }
}
