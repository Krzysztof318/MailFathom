// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Calendar;

namespace MailFathom.Application.Calendar;

/// <summary>Asks for one bounded window of somebody's calendar.</summary>
/// <remarks>
/// <para>
/// Every read of the calendar is a window, because every view over it is: a month, a week, a day, and an agenda are
/// four spans rather than four questions. An event is in the window when any part of it falls inside it, so a meeting
/// that began before the window opened is still what the day it runs into shows.
/// </para>
/// <para>
/// The window is bounded by how many events it may answer with rather than by how long it may be. A span is what a
/// caller genuinely asks about — a year of an agenda view is an ordinary request — while the number of rows is what
/// decides the cost of answering, and a window whose events overflow the bound is reported as such by the count coming
/// back full rather than being silently cut to a shorter span.
/// </para>
/// </remarks>
public sealed record CalendarEventQuery
{
    /// <summary>How many events a request that names no count is answered with at most.</summary>
    public const int DefaultCount = 200;

    /// <summary>The greatest number of events one window may answer with.</summary>
    /// <remarks>
    /// Well above a month of a busy calendar, which is the longest span a drawn view asks for at once, and low enough
    /// that a window somebody widened by hand cannot ask this deployment to compose an unbounded answer.
    /// </remarks>
    public const int MaximumCount = 1000;

    private CalendarEventQuery(DateTimeOffset from, DateTimeOffset until, CalendarEventOrigin? origin, int count)
    {
        this.From = from;
        this.Until = until;
        this.Origin = origin;
        this.Count = count;
    }

    /// <summary>Gets the instant the window opens, which an event ending exactly there falls outside.</summary>
    public DateTimeOffset From { get; }

    /// <summary>Gets the instant the window closes, which an event beginning exactly there falls outside.</summary>
    /// <remarks>
    /// Half-open, so the windows of two consecutive days, weeks, or months answer for every event exactly once between
    /// them — which is what keeps an event from being drawn twice by a view that walks them.
    /// </remarks>
    public DateTimeOffset Until { get; }

    /// <summary>Gets the origin the window is narrowed to, or <see langword="null" /> for both.</summary>
    /// <remarks>
    /// Narrowing to <see cref="CalendarEventOrigin.Proposed" /> is what reads dates mail named and nobody has agreed
    /// to, and narrowing to <see cref="CalendarEventOrigin.Asserted" /> is what reads the calendar itself. The two are
    /// drawn in different places, which is why they are asked for separately rather than sorted out by the reader.
    /// </remarks>
    public CalendarEventOrigin? Origin { get; }

    /// <summary>Gets how many events the window answers with at most.</summary>
    public int Count { get; }

    /// <summary>Builds a validated window from what a caller asked for.</summary>
    /// <param name="from">The instant the window opens.</param>
    /// <param name="until">The instant it closes, which is after <paramref name="from" />.</param>
    /// <param name="origin">The origin to narrow to, or <see langword="null" /> for both.</param>
    /// <param name="count">How many events it may answer with, or <see langword="null" /> for <see cref="DefaultCount" />.</param>
    /// <returns>The window the store reads under.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="until" /> is not after <paramref name="from" />, when <paramref name="count" /> is below one or above <see cref="MaximumCount" />, or when <paramref name="origin" /> names no declared value.</exception>
    public static CalendarEventQuery Create(
        DateTimeOffset from,
        DateTimeOffset until,
        CalendarEventOrigin? origin,
        int? count)
    {
        if (until <= from)
        {
            throw new ArgumentOutOfRangeException(nameof(until), until, "A calendar window closes after it opens.");
        }

        var resolvedCount = count ?? DefaultCount;

        ArgumentOutOfRangeException.ThrowIfLessThan(resolvedCount, 1, nameof(count));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(resolvedCount, MaximumCount, nameof(count));

        if (origin is { } named && !Enum.IsDefined(named))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), "A calendar event origin must name a declared value.");
        }

        return new CalendarEventQuery(from, until, origin, resolvedCount);
    }
}
