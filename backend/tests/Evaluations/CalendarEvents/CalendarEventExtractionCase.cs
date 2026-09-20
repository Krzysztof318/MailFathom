// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.CalendarEvents;
using MailFathom.Application.Calendar.Extraction;
using MailFathom.Evaluations.Corpus;

namespace MailFathom.Evaluations.CalendarEvents;

/// <summary>One text put to the extraction agent, and what the events read from its answer have to hold.</summary>
/// <remarks>
/// <para>
/// An event is a structure — a title, a start, and sometimes an end — so every case asks what that structure settles:
/// which day and hour the text fixed, whether a length the text stated came back, and whether a text naming no occasion
/// was left alone. The title is asked about nowhere, because two names for the same meeting are both right.
/// </para>
/// <para>
/// Half the cases are messages and half are sentences somebody typed, because the agent reads both under one
/// instruction and a reword that helps one can quietly stop answering the other. The messages come from the corpus,
/// which already holds meetings with an hour, meetings with a length, and mail that names only deadlines; the sentences
/// are written here, because nothing in the corpus is one. The ones written to take the agent over come from
/// <see cref="HostileMail" />, and a calendar is a place somebody would like to put an appointment nobody agreed to.
/// </para>
/// <para>
/// A deadline is what most of the refusals are about. It is the shape the agent is likeliest to be wrong in the
/// expensive direction on: a day somebody has to have finished something by reads exactly like a day something happens,
/// and putting one in a day's column tells them they are busy when they are not.
/// </para>
/// </remarks>
/// <param name="Name">The name the case is filed and reported under.</param>
/// <param name="Compose">Composes the turn the text is put to the agent as.</param>
/// <param name="Expectation">Names what the events get wrong, or answers <see langword="null" /> when they get nothing wrong.</param>
internal sealed record CalendarEventExtractionCase(
    string Name,
    Func<CalendarEventExtractionTurn> Compose,
    Func<IReadOnlyList<ExtractedCalendarEvent>, string?> Expectation)
{
    /// <summary>The instant every typed sentence here is written at: a Monday morning, with an offset of its own.</summary>
    /// <remarks>
    /// The offset is not the deployment's and not UTC, because that is the whole of what the typed half has to get
    /// right: the model writes a local wall clock and the offset comes from whoever typed the sentence, so an anchor
    /// written in UTC would pass a reading that had quietly resolved the hour somewhere else.
    /// </remarks>
    private static readonly DateTimeOffset Typed = new(2026, 9, 21, 9, 30, 0, TimeSpan.FromHours(2));

    /// <summary>Gets every case, in the order the report lists them.</summary>
    public static IReadOnlyList<CalendarEventExtractionCase> All { get; } =
    [
        // A berth inspection pilot meeting on Tuesday, 15 September 2026 from 10:30 to 11:15, beside an agenda due "by
        // Friday" and a reply asked for "by Monday afternoon" — neither of which is an occasion.
        FromMail(
            "MeetingWithStartAndEnd",
            static () => CorpusMessage.At(90),
            Every(
                RunsFrom(Utc(2026, 9, 15, 10, 30), Utc(2026, 9, 15, 11, 15)),
                NothingOn(new DateOnly(2026, 9, 4), "the agenda is due that Friday, which is a deadline rather than an occasion"),
                NothingOn(new DateOnly(2026, 8, 31), "a reply is asked for that Monday, which is a deadline rather than an occasion"))),

        // A 30-minute validation review confirmed for 21 September 2026 at 14:00 UTC.
        FromMail("MeetingAtNamedHour", static () => CorpusMessage.At(11), Begins(Utc(2026, 9, 21, 14, 0))),

        // Two invoices, a payment undertaken by 10 September and one that arrived on 7 September: days, and no occasion
        // on any of them.
        FromMail("DeadlinesOnly", static () => CorpusMessage.At(82), Nothing),

        // Written to take the agent over. Whatever the answer holds, none of it may carry out what the message asked,
        // which the scenario checks on every answer; the reading itself is held to nothing more.
        FromMail("Hostile.DirectInstruction", static () => HostileMail.DirectInstruction[^1], static _ => null),
        FromMail("Hostile.ForgedTurn", static () => HostileMail.ForgedTurn[^1], static _ => null),
        FromMail("Hostile.OwnerImpersonation", static () => HostileMail.OwnerImpersonation[^1], static _ => null),

        // A move confirmed for Saturday, 26 September 2026, with the goods lift booked from 8:00 to 12:00 — a day the
        // message fixes and an hour a reader may or may not take from the lift, so only the day is asked about.
        FromMail("Polish.MoveDay", static () => PolishCorpus.At(10), BeginsOn(new DateOnly(2026, 9, 26))),

        // A trip leaving on Wednesday, 14 October, by a train named with its departure time, and returning on the 16th.
        FromMail("Polish.TripDeparture", static () => PolishCorpus.At(7), BeginsOn(new DateOnly(2026, 10, 14))),

        // The rest are sentences somebody typed into the dialog, each written against the Monday morning above.

        // A weekday and an hour, resolved forward from the day the sentence was typed.
        FromDescription("Sentence.NamedDayAndHour", "site inspection on Thursday at 8:30", Begins(TypedDay(24, 8, 30))),

        // The same in Polish, because the dialog is filled in whatever language the person thinks in.
        FromDescription("Sentence.Polish", "przegląd instalacji w czwartek o 8:30", Begins(TypedDay(24, 8, 30))),

        // "Tomorrow" is the relative day the anchor exists for.
        FromDescription("Sentence.Tomorrow", "team stand-up tomorrow at 9:15", Begins(TypedDay(22, 9, 15))),

        // A length the sentence states, which is the one thing that puts an end on an event.
        FromDescription(
            "Sentence.WithLength",
            "racking survey on 29 September from 10:00 to 11:30",
            RunsFrom(TypedDay(29, 10, 0), TypedDay(29, 11, 30))),

        // A day with no hour, which is shown as the day rather than guessed at an hour.
        FromDescription(
            "Sentence.DayWithoutHour",
            "school open day on 2 October",
            Every(Begins(new DateTimeOffset(2026, 10, 2, 0, 0, 0, Typed.Offset)), NoLength)),

        // Nothing is fixed to a day, so nothing is offered.
        FromDescription("Sentence.NoOccasion", "we should catch up some time", Nothing),

        // A day something has to be finished by, which is the refusal the dialog most needs.
        FromDescription("Sentence.Deadline", "send the VAT return by Friday", Nothing),
    ];

    /// <summary>Finds a case by the name it is filed under.</summary>
    /// <param name="name">The case's name.</param>
    /// <returns>The case.</returns>
    public static CalendarEventExtractionCase Named(string name) =>
        All.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => this.Name;

    /// <summary>Describes a message, put to the agent as the turn a message is read under.</summary>
    private static CalendarEventExtractionCase FromMail(
        string name,
        Func<CorpusMessage> message,
        Func<IReadOnlyList<ExtractedCalendarEvent>, string?> expectation) =>
        new(
            name,
            () =>
            {
                var read = message();

                return new CalendarEventExtractionTurn(
                    CalendarEventExtractionInstructions.ComposeMailTurn(
                        read.Subject,
                        read.ReceivedAt,
                        [.. read.Passages.Select(static passage => passage.Text)]),
                    read.ReceivedAt,
                    CalendarEventExtraction.MaximumEvents);
            },
            expectation);

    /// <summary>Describes a typed sentence, put to the agent as the turn a sentence is read under.</summary>
    private static CalendarEventExtractionCase FromDescription(
        string name,
        string sentence,
        Func<IReadOnlyList<ExtractedCalendarEvent>, string?> expectation) =>
        new(
            name,
            () => new CalendarEventExtractionTurn(
                CalendarEventExtractionInstructions.ComposeDescriptionTurn(sentence, Typed),
                Typed,
                CalendarEventExtractionAgent.MaximumEventsPerDescription),
            expectation);

    /// <summary>Reads a day of the month the sentences are typed in, under the offset whoever typed them is standing in.</summary>
    private static DateTimeOffset TypedDay(int day, int hour, int minute) =>
        new(Typed.Year, Typed.Month, day, hour, minute, 0, Typed.Offset);

    /// <summary>Reads an instant written where the corpus is dated, which carries no offset of its own.</summary>
    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>Holds the events to every expectation in turn, and names the first they fall short of.</summary>
    private static Func<IReadOnlyList<ExtractedCalendarEvent>, string?> Every(
        params Func<IReadOnlyList<ExtractedCalendarEvent>, string?>[] expectations) =>
        events => expectations.Select(expectation => expectation(events)).FirstOrDefault(static shortfall => shortfall is not null);

    /// <summary>Asks for an event beginning at a named instant.</summary>
    private static Func<IReadOnlyList<ExtractedCalendarEvent>, string?> Begins(DateTimeOffset start) =>
        events => events.Any(occasion => occasion.Start == start)
            ? null
            : $"no event begins at {start:yyyy-MM-dd HH:mm}, which is the instant the text fixes; what came back {Listed(events)}.";

    /// <summary>Asks for an event beginning on a named day, whatever hour it was given.</summary>
    /// <remarks>Used where the text fixes the day beyond doubt and leaves the hour to a reader, so an hour would measure a preference.</remarks>
    private static Func<IReadOnlyList<ExtractedCalendarEvent>, string?> BeginsOn(DateOnly day) =>
        events => events.Any(occasion => DateOnly.FromDateTime(occasion.Start.DateTime) == day)
            ? null
            : $"no event begins on {day:yyyy-MM-dd}, which is the day the text fixes; what came back {Listed(events)}.";

    /// <summary>Asks for an event running between two named instants, which is what a stated length produces.</summary>
    private static Func<IReadOnlyList<ExtractedCalendarEvent>, string?> RunsFrom(DateTimeOffset start, DateTimeOffset end) =>
        events => events.Any(occasion => occasion.Start == start && occasion.End == end)
            ? null
            : $"no event runs from {start:yyyy-MM-dd HH:mm} to {end:HH:mm}, though the text says how long it lasts; what came back {Listed(events)}.";

    /// <summary>Refuses an event on a day the text names only as one something has to be finished by.</summary>
    private static Func<IReadOnlyList<ExtractedCalendarEvent>, string?> NothingOn(DateOnly day, string because) =>
        events => events.FirstOrDefault(occasion => DateOnly.FromDateTime(occasion.Start.DateTime) == day) is { } invented
            ? $"an event was put on {day:yyyy-MM-dd}, though {because}: \"{invented.Title.Value}\"."
            : null;

    /// <summary>Refuses an end on an event whose text never said how long it lasts.</summary>
    private static string? NoLength(IReadOnlyList<ExtractedCalendarEvent> events) =>
        events.FirstOrDefault(static occasion => occasion.End is not null) is { } invented
            ? $"the text names no length, yet \"{invented.Title.Value}\" was given one, ending at {invented.End:yyyy-MM-dd HH:mm}."
            : null;

    /// <summary>Refuses any event at all, which is what a text naming no occasion is owed.</summary>
    private static string? Nothing(IReadOnlyList<ExtractedCalendarEvent> events) =>
        events.Count is 0
            ? null
            : $"the text names no occasion, yet {events.Count} event(s) came back: {Listed(events)}.";

    /// <summary>Lists what came back, so a failed run reads as what the model answered rather than as a count.</summary>
    private static string Listed(IReadOnlyList<ExtractedCalendarEvent> events) =>
        events.Count is 0
            ? "was nothing at all"
            : $"was {string.Join(", ", events.Select(static occasion => $"\"{occasion.Title.Value}\" at {occasion.Start:yyyy-MM-dd HH:mm}"))}";
}
