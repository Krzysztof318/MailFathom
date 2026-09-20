// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Calendar.Extraction;

/// <summary>What one extraction produced: the events it settled, or the reason it produced none this time.</summary>
/// <remarks>
/// The two outcomes are unequal, and what a caller does with one follows from which it holds. A settled answer —
/// including a settled answer of no events at all — is acted on and takes the text out of whatever queue it was in. A
/// withheld one is not, because every reason an extraction is withheld outlives one message and would be met again by
/// the next.
/// </remarks>
public sealed record CalendarEventExtraction
{
    /// <summary>The greatest number of events one reading of a message may find.</summary>
    /// <remarks>
    /// Small, because a message naming five separate meetings is rare and a reading that answers with five is far more
    /// often one that read a quoted thread as a diary. What the bound costs in that rare case is the sixth date, which
    /// stays in the message; what it saves is a person opening their proposals to a list nobody will work through.
    /// </remarks>
    public const int MaximumEvents = 4;

    private CalendarEventExtraction(
        IReadOnlyList<ExtractedCalendarEvent> events,
        CalendarEventExtractionWithholding? withheld)
    {
        this.Events = events;
        this.Withheld = withheld;
    }

    /// <summary>Gets what was found, which is empty for a settled answer of nothing and for a withheld one alike.</summary>
    public IReadOnlyList<ExtractedCalendarEvent> Events { get; }

    /// <summary>Gets why nothing was found, or <see langword="null" /> where the answer is settled.</summary>
    public CalendarEventExtractionWithholding? Withheld { get; }

    /// <summary>Gets whether the answer is one to act on.</summary>
    public bool IsSettled => this.Withheld is null;

    /// <summary>Records a settled extraction.</summary>
    /// <param name="events">What was found, and empty where the text named no date at all.</param>
    /// <returns>The extraction.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="events" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when more than <see cref="MaximumEvents" /> events are supplied.</exception>
    public static CalendarEventExtraction Settled(IReadOnlyList<ExtractedCalendarEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count > MaximumEvents)
        {
            throw new ArgumentException(
                $"One reading settles at most {MaximumEvents} events.",
                nameof(events));
        }

        return new CalendarEventExtraction(events, withheld: null);
    }

    /// <summary>Records that nothing was extracted, and why.</summary>
    /// <param name="withholding">The condition that stopped it.</param>
    /// <returns>The extraction.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="withholding" /> is not a defined member.</exception>
    public static CalendarEventExtraction Withholding(CalendarEventExtractionWithholding withholding)
    {
        if (!Enum.IsDefined(withholding))
        {
            throw new ArgumentOutOfRangeException(
                nameof(withholding),
                withholding,
                "A withheld extraction names one of the conditions this system stops on.");
        }

        return new CalendarEventExtraction([], withholding);
    }
}
