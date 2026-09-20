// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Tasks;

namespace MailFathom.Application.Tasks;

/// <summary>An arrangement of one day that is offered and never applied.</summary>
/// <remarks>
/// <para>
/// Nothing here has happened. No task is moved, no due day is rewritten, and no event is put on a calendar: a
/// suggestion is read by the screen that asked for it and comes to nothing unless the person acts on it, through the
/// routes that own each of those acts. That is what makes this a value rather than a write, and it is why laying out a
/// day twice leaves the same two lists it started with.
/// </para>
/// <para>
/// It names tasks by identity and nothing else. The lines were sent to decide the arrangement and the arrangement does
/// not repeat them, so a client draws each row from the task it already holds — which is also what keeps an
/// arrangement from becoming a second copy of somebody's list.
/// </para>
/// </remarks>
/// <param name="Placements">What to do and when, in the order it is suggested, holding each task at most once.</param>
/// <param name="NotToday">The tasks that do not realistically fit the day, which the person is shown rather than told about afterwards.</param>
public sealed record DayLayoutSuggestion(
    IReadOnlyList<DayLayoutPlacement> Placements,
    IReadOnlyList<PersonalTaskId> NotToday);

/// <summary>Where one task is suggested to sit in the day.</summary>
/// <remarks>
/// A window rather than an order alone, because a day is laid out against a calendar: an order says what to do next
/// and a window says whether it fits between two meetings, and only the second answers the question the control was
/// pressed to ask.
/// </remarks>
/// <param name="Task">The task being placed.</param>
/// <param name="StartAt">When it is suggested to begin, which falls inside the day that was asked about.</param>
/// <param name="Duration">How long it is suggested to take, which stands between the two bounds below.</param>
public sealed record DayLayoutPlacement(PersonalTaskId Task, DateTimeOffset StartAt, TimeSpan Duration)
{
    /// <summary>The shortest window a placement is offered with.</summary>
    /// <remarks>Below this a suggestion is a gesture rather than a plan, and a day drawn out of one-minute blocks is a screen nobody can read.</remarks>
    public const int MinimumMinutes = 5;

    /// <summary>The longest window a placement is offered with.</summary>
    /// <remarks>A working day in one piece, which is the most any single thing can sensibly be given before the arrangement stops being one.</remarks>
    public const int MaximumMinutes = 480;

    /// <summary>The window suggested for something whose length nothing says anything about.</summary>
    /// <remarks>It is what the arrangement is told to fall back on rather than a value anything here writes, so a suggestion is never silently stretched to fill a gap.</remarks>
    public const int DefaultMinutes = 30;
}
