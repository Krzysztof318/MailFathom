// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
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
    /// <summary>The forms a written instant is accepted in, all of them local and none carrying a zone.</summary>
    /// <remarks>
    /// The seconds are admitted although nothing asks for them, because writing <c>:00</c> onto an instant is the one
    /// departure from the stated form that says nothing about whether the model understood the date. Anything else —
    /// a <c>Z</c>, an offset, a day alone, a month name — is a reading that failed.
    /// </remarks>
    private static readonly string[] WrittenInstantFormats = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"];

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
            || ToInstant(written.Start, anchor) is not { } start)
        {
            return null;
        }

        var end = ToInstant(written.End, anchor);

        return new ExtractedCalendarEvent(title, start, end > start ? end : null);
    }

    /// <summary>Reads one written local time into the instant it names where the text was written.</summary>
    /// <remarks>
    /// The two guarded days at the ends of the range are what applying an offset can push past: a local time within a
    /// day of either bound of <see cref="DateTime" /> has no instant in some zones, and constructing one raises rather
    /// than answering. A model writing a year one date has misread the text, so refusing it is the accurate reading
    /// as well as the safe one.
    /// </remarks>
    private static DateTimeOffset? ToInstant(string? written, DateTimeOffset anchor)
    {
        if (!DateTime.TryParseExact(
            written,
            WrittenInstantFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var local))
        {
            return null;
        }

        return local >= DateTime.MinValue.AddDays(1) && local <= DateTime.MaxValue.AddDays(-1)
            ? new DateTimeOffset(local, anchor.Offset)
            : null;
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
