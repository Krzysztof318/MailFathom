// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using MailFathom.AI.Orchestration;
using MailFathom.Application.Calendar.Extraction;
using MailFathom.Domain.Calendar;

namespace MailFathom.AI.CalendarEvents;

/// <summary>Turns what an extraction agent wrote into events, keeping only what an event is allowed to be.</summary>
/// <remarks>
/// <para>
/// Every reading below is a pure function of the answer and the instant it is resolved against, which is what makes
/// the cases a provider produces once in a thousand runs ordinary examples in a test rather than something only a live
/// endpoint reaches.
/// </para>
/// <para>
/// It drops rather than repairs. An event with no title, no start, or a start written in a form the instruction did
/// not ask for is one nothing could be shown for, and there is no honest way to invent the missing half — so it falls
/// away and the reading keeps what survived. The one exception is an end that is not after its start: the event is
/// kept without it, because what the text fixed was when the thing begins and a length that read wrongly is the part
/// to discard rather than the occasion.
/// </para>
/// <para>
/// The model writes a local date and time and never a zone, and the offset comes from the instant this reading is
/// anchored to — the message's own arrival for mail, and the person's own clock for a sentence they typed. That is
/// what keeps <em>Thursday at three</em> meaning three o'clock where whoever wrote it was standing, rather than three
/// o'clock wherever the deployment happens to run. An answer that carries an offset anyway is refused rather than
/// honoured, because a model that supplied one guessed it.
/// </para>
/// </remarks>
internal static class CalendarEventExtractionReading
{
    /// <summary>Reads the events out of an agent's answer.</summary>
    /// <param name="answerText">What the agent wrote, which may be empty, fenced, or surrounded by prose.</param>
    /// <param name="anchor">The instant the text belongs to, whose offset every written time is read in.</param>
    /// <param name="maximumEvents">How many events this reading may keep, best first.</param>
    /// <returns>The events that survived, which is empty where none did.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maximumEvents" /> is not positive.</exception>
    internal static IReadOnlyList<ExtractedCalendarEvent> Read(
        string? answerText,
        DateTimeOffset anchor,
        int maximumEvents)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumEvents);

        if (ReadDocument(answerText) is not { Events: { } written })
        {
            return [];
        }

        return
        [
            .. written
                .Select(entry => ToEvent(entry, anchor))
                .OfType<ExtractedCalendarEvent>()
                .Take(maximumEvents),
        ];
    }

    /// <summary>Turns one written event into an event, or into nothing where it is not one.</summary>
    private static ExtractedCalendarEvent? ToEvent(CalendarEventDocument? written, DateTimeOffset anchor)
    {
        if (written is null
            || !CalendarEventTitle.TryCreate(written.Title, out var title)
            || AnchoredInstant.Read(written.Start, anchor) is not { } start)
        {
            return null;
        }

        var end = AnchoredInstant.Read(written.End, anchor);

        return new ExtractedCalendarEvent(title, start, end > start ? end : null);
    }

    private static CalendarEventExtractionDocument? ReadDocument(string? answerText)
    {
        if (AgentJsonAnswer.Unfenced(answerText) is not { } json)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(
                json,
                CalendarEventExtractionJsonContext.Default.CalendarEventExtractionDocument);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
