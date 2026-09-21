// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Calendar.Import;

/// <summary>What reading an offered file produced: the entries that could be events, and one reason per entry that could not.</summary>
/// <remarks>
/// <para>
/// The skips are a reason per skipped entry rather than a tally, because the reader answers about entries and the
/// import adds its own — an entry this calendar already holds is a skip nothing in the file could have known about.
/// Counting happens once, where the report is composed, so the two sources cannot be counted differently.
/// </para>
/// <para>
/// A refusal of the whole file carries neither: an unreadable file has no entries to offer and nothing to report a
/// skip about, and saying so with empty lists is what keeps a caller from drawing a summary of nothing.
/// </para>
/// </remarks>
public sealed record CalendarFileReading
{
    private CalendarFileReading(
        CalendarImportOutcome outcome,
        IReadOnlyList<CalendarFileEntry> entries,
        IReadOnlyList<CalendarImportSkipReason> skipped)
    {
        this.Outcome = outcome;
        this.Entries = entries;
        this.Skipped = skipped;
    }

    /// <summary>Gets what became of the file as a whole.</summary>
    public CalendarImportOutcome Outcome { get; }

    /// <summary>Gets the entries that could become events, in the order the file named them.</summary>
    public IReadOnlyList<CalendarFileEntry> Entries { get; }

    /// <summary>Gets why each entry that could not become one did not, one reason per entry.</summary>
    public IReadOnlyList<CalendarImportSkipReason> Skipped { get; }

    /// <summary>Gets the reading of octets that are not iCalendar this reader could finish.</summary>
    public static CalendarFileReading NotCalendarData { get; } =
        new(CalendarImportOutcome.NotCalendarData, [], []);

    /// <summary>Gets the reading of a file naming more entries than one import writes.</summary>
    public static CalendarFileReading TooManyEntries { get; } =
        new(CalendarImportOutcome.TooManyEntries, [], []);

    /// <summary>States what a readable file offered.</summary>
    /// <param name="entries">The entries that could become events.</param>
    /// <param name="skipped">Why each entry that could not did not, one reason per entry.</param>
    /// <returns>The reading.</returns>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null" />.</exception>
    public static CalendarFileReading Read(
        IReadOnlyList<CalendarFileEntry> entries,
        IReadOnlyList<CalendarImportSkipReason> skipped)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(skipped);

        return new CalendarFileReading(CalendarImportOutcome.Read, entries, skipped);
    }
}
