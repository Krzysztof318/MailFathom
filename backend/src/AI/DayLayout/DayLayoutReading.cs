// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Tasks;

namespace MailFathom.AI.DayLayout;

/// <summary>Turns what a day-layout agent wrote into an arrangement, keeping only what the day it was asked about can back.</summary>
/// <remarks>
/// <para>
/// Every reading below is a pure function of the answer and the day the turn published, which is what makes the cases
/// a provider produces once in a thousand runs ordinary examples in a test rather than something only a live endpoint
/// reaches.
/// </para>
/// <para>
/// It drops rather than repairs, with one exception it states. A placement naming a task the turn did not publish, or
/// an instant outside the day that was asked about, is one nothing can be drawn from — there is no honest way to guess
/// which task was meant or which hour, so it falls away and the rest of the arrangement survives. The exception is the
/// length: a window is brought inside the bounds instead of being dropped, because how long something takes is the
/// part of a suggestion a person adjusts anyway, and dropping it would lose the hour it was placed at as well.
/// </para>
/// <para>
/// A task is cited by its position in the list the turn composed, so a number outside that list names no task and
/// takes its placement with it. That is the whole of why the model is shown numbers rather than identifiers: an
/// out-of-range number is unusable, while an identifier a model wrote would be somebody's task and would look valid.
/// </para>
/// </remarks>
internal static class DayLayoutReading
{
    /// <summary>Reads the arrangement out of an agent's answer.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <param name="question">The day the turn published, whose task order is what the answer's numbers name.</param>
    /// <returns>The arrangement that survived, which is empty in both lists where none did.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="question" /> is <see langword="null" />.</exception>
    internal static DayLayoutSuggestion Read(string? answerText, DayLayoutQuestion question)
    {
        ArgumentNullException.ThrowIfNull(question);

        if (ReadDocument(answerText) is not { } document)
        {
            return new DayLayoutSuggestion([], []);
        }

        var placements = ToPlacements(document.Placements, question);
        var placed = placements.Select(static placement => placement.Task).ToHashSet();

        return new DayLayoutSuggestion(
            placements,
            [
                .. (document.NotToday ?? [])
                    .Where(ordinal => ordinal >= 0 && ordinal < question.Tasks.Count)
                    .Select(ordinal => question.Tasks[ordinal].Id)
                    .Distinct()
                    .Where(task => !placed.Contains(task)),
            ]);
    }

    /// <summary>Turns the written placements into the ones the day can back, earliest first.</summary>
    /// <remarks>
    /// Read into the order the windows themselves state rather than the order the answer listed them in, because the
    /// two are the same claim and only one of them is checkable: an arrangement whose rows are drawn in one order and
    /// timed in another is a screen contradicting itself. A task placed twice keeps its first window, a later one
    /// being a second answer to a question already answered.
    /// </remarks>
    private static IReadOnlyList<DayLayoutPlacement> ToPlacements(
        IReadOnlyList<DayLayoutPlacementDocument>? written,
        DayLayoutQuestion question) =>
    [
        .. (written ?? [])
            .Select(entry => ToPlacement(entry, question))
            .OfType<DayLayoutPlacement>()
            .DistinctBy(static placement => placement.Task)
            .OrderBy(static placement => placement.StartAt)
            .Take(question.Tasks.Count),
    ];

    /// <summary>Turns one written placement into a suggestion, or into nothing where the day cannot back it.</summary>
    private static DayLayoutPlacement? ToPlacement(DayLayoutPlacementDocument? written, DayLayoutQuestion question)
    {
        if (written?.Task is not { } ordinal || ordinal < 0 || ordinal >= question.Tasks.Count)
        {
            return null;
        }

        // Assumed universal where the model wrote no offset, rather than read as the host's own time: the turn states
        // the day in offsets, so an instant written without one is a model dropping what it was given, and reading it
        // as local would make the same answer mean different hours on different machines.
        if (!DateTimeOffset.TryParse(
            written.StartAt,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var startAt)
            || startAt < question.DayStart
            || startAt >= question.DayEnd)
        {
            return null;
        }

        return new DayLayoutPlacement(question.Tasks[ordinal].Id, startAt, Bounded(written.Minutes));
    }

    /// <summary>Brings a written length inside what a placement is offered with.</summary>
    private static TimeSpan Bounded(int? minutes) => TimeSpan.FromMinutes(minutes switch
    {
        null => DayLayoutPlacement.DefaultMinutes,
        < DayLayoutPlacement.MinimumMinutes => DayLayoutPlacement.MinimumMinutes,
        > DayLayoutPlacement.MaximumMinutes => DayLayoutPlacement.MaximumMinutes,
        var stated => stated.Value,
    });

    private static DayLayoutDocument? ReadDocument(string? answerText)
    {
        if (AgentJsonAnswer.Unfenced(answerText) is not { } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, DayLayoutJsonContext.Default.DayLayoutDocument);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
