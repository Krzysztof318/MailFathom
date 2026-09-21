// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Calendar.Import;

/// <summary>What a file would put on somebody's calendar, or what it just did.</summary>
/// <remarks>
/// <para>
/// One type answers both acts deliberately. What a person is shown before they confirm has to be the same reading
/// that is then performed, and two types would be two readings a change could put out of step — the summary promising
/// seventy events and the import writing sixty-eight, with nothing in either saying which was right.
/// </para>
/// <para>
/// It names counts and a span of dates, never an entry. The titles, the days, and the identifiers a file carries are
/// somebody's appointments, so what comes back is how many and between which two instants; the events themselves are
/// read from the calendar afterwards, by the same window every other view over it is.
/// </para>
/// </remarks>
public sealed record CalendarImportSummary
{
    private CalendarImportSummary(
        CalendarImportOutcome outcome,
        int events,
        DateTimeOffset? earliest,
        DateTimeOffset? latest,
        IReadOnlyList<CalendarImportSkipTally> skipped)
    {
        this.Outcome = outcome;
        this.Events = events;
        this.Earliest = earliest;
        this.Latest = latest;
        this.Skipped = skipped;
    }

    /// <summary>Gets what became of the file as a whole.</summary>
    public CalendarImportOutcome Outcome { get; }

    /// <summary>Gets how many events the file puts on the calendar, which a summary states as what it would put there.</summary>
    public int Events { get; }

    /// <summary>Gets the instant the earliest of those events begins, or <see langword="null" /> where there are none.</summary>
    public DateTimeOffset? Earliest { get; }

    /// <summary>Gets the instant the latest of those events begins, or <see langword="null" /> where there are none.</summary>
    /// <remarks>Where it begins rather than where it ends, so that the pair is the span of days the import covers rather than the reach of the longest entry in it.</remarks>
    public DateTimeOffset? Latest { get; }

    /// <summary>Gets how many entries were skipped, per reason, ordered by the reason so two readings report alike.</summary>
    public IReadOnlyList<CalendarImportSkipTally> Skipped { get; }

    /// <summary>States that the file was refused as a whole.</summary>
    /// <param name="outcome">Why it was refused.</param>
    /// <returns>The summary.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="outcome" /> names no refusal.</exception>
    public static CalendarImportSummary Refused(CalendarImportOutcome outcome)
    {
        if (outcome is CalendarImportOutcome.Read || !Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A refusal names why the file was refused.");
        }

        return new CalendarImportSummary(outcome, 0, null, null, []);
    }

    /// <summary>States what a readable file amounts to.</summary>
    /// <param name="starts">When each event the import writes begins, in any order.</param>
    /// <param name="skipped">Why each skipped entry was skipped, one reason per entry.</param>
    /// <returns>The summary.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    public static CalendarImportSummary Of(
        IReadOnlyCollection<DateTimeOffset> starts,
        IReadOnlyCollection<CalendarImportSkipReason> skipped)
    {
        ArgumentNullException.ThrowIfNull(starts);
        ArgumentNullException.ThrowIfNull(skipped);

        var tallies = skipped
            .GroupBy(reason => reason)
            .OrderBy(byReason => byReason.Key)
            .Select(byReason => new CalendarImportSkipTally(byReason.Key, byReason.Count()))
            .ToArray();

        return new CalendarImportSummary(
            CalendarImportOutcome.Read,
            starts.Count,
            starts.Count == 0 ? null : starts.Min(),
            starts.Count == 0 ? null : starts.Max(),
            tallies);
    }
}
