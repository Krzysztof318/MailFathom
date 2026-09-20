// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;
using MailFathom.Domain.Accounts;
using MailFathom.Evaluations.Corpus;

namespace MailFathom.Evaluations.Enrichment;

/// <summary>One message put to the enrichment agent, and what the marks read from its answer have to hold.</summary>
/// <remarks>
/// <para>
/// A mark is a structure — an aspect, the passages it cites, and for a commitment the day it falls due — so every case
/// asks what that structure settles: that the message was given a reading at all, that a commitment falls due on a day
/// the message names rather than one the model invented, and that a message asking nothing of anybody carries no
/// commitment. The wording is asked about nowhere, because two sentences saying the same thing are both right.
/// </para>
/// <para>
/// The corpus supplies the messages with amounts, dates, and undertakings. What it has no example of — a message with
/// nothing worth writing, a reply whose only request sits in the history it quotes, and a newsletter — comes from
/// <see cref="WrittenCorpus" />, and the messages written to take the agent over from <see cref="HostileMail" />.
/// </para>
/// </remarks>
/// <param name="Name">The name the case is filed and reported under.</param>
/// <param name="Message">Reads the message.</param>
/// <param name="Expectation">Names what the marks get wrong, or answers <see langword="null" /> when they get nothing wrong.</param>
/// <param name="Language">
/// The mailbox language the case is composed under, which every mark it writes must be written in; <see langword="null" />
/// for the corpus's own English, whose cases are held only to what the marks say.
/// </param>
/// <param name="TaskExpectation">
/// Names what the tasks read out of the same answer get wrong, or <see langword="null" /> where the case says nothing
/// about them. It is a second expectation rather than a second case because both come out of one call: what a message
/// asks its reader to do is read beside its marks, and measuring it separately would pay for the same answer twice.
/// </param>
internal sealed record EmailEnrichmentCase(
    string Name,
    Func<CorpusMessage> Message,
    Func<IReadOnlyList<EmailEnrichmentMark>, string?> Expectation,
    MailAccountLanguage? Language = null,
    Func<IReadOnlyList<EmailTaskProposal>, string?>? TaskExpectation = null)
{
    /// <summary>Gets every case, in the order the report lists them.</summary>
    public static IReadOnlyList<EmailEnrichmentCase> All { get; } =
    [
        // A reply closing an export ticket and chasing an outstanding invoice: a subject, a request, amounts, and dates.
        FromCorpus("InvoiceFollowUp", 3, SaysWhatItIsAbout),

        // The owner undertakes to pay invoice 7842 by 10 September 2026.
        FromCorpus("DatedPaymentPromise", 82, DueOn(new DateOnly(2026, 9, 10))),

        // Payment of INV-4827 is scheduled for 25 September 2026, two days ahead of its 27 September due date.
        FromCorpus("PaymentAheadOfDueDate", 71, DueOn(new DateOnly(2026, 9, 25))),

        // Nimbus Support undertakes to share its next progress update by 10 September 2026.
        FromCorpus("ProgressUpdateByDate", 26, DueOn(new DateOnly(2026, 9, 10))),

        // Two invoices, three amounts, and four dates, of which the owner's own undertaking falls on 12 October.
        FromCorpus(
            "SeveralAmountsAndDates",
            5,
            Every(SaysWhatItIsAbout, DueOnlyOnNamedDays(
                new DateOnly(2026, 9, 30),
                new DateOnly(2026, 10, 9),
                new DateOnly(2026, 10, 12),
                new DateOnly(2026, 10, 13),
                new DateOnly(2026, 10, 14)))),

        // Written on Sunday, 30 August: an agenda "by Friday", a reply "by Monday afternoon", and a meeting on 15 September.
        FromCorpus(
            "RelativeDeadlines",
            90,
            Every(DueSomeday, DueOnlyOnNamedDays(
                new DateOnly(2026, 8, 31),
                new DateOnly(2026, 9, 4),
                new DateOnly(2026, 9, 14),
                new DateOnly(2026, 9, 15)))),

        // A revised invoice copy by 8 September and a payment released on 11 September.
        FromCorpus("TwoDatedUndertakings", 98, Every(DueSomeday, DueOnlyOnNamedDays(new DateOnly(2026, 9, 8), new DateOnly(2026, 9, 11)))),

        // Three people's tasks, each by its own named day in September, and a reply asked for by Monday, 7 September.
        FromCorpus(
            "SeveralOwnersAndDays",
            57,
            Every(DueSomeday, DueOnlyOnNamedDays(
                new DateOnly(2026, 9, 7),
                new DateOnly(2026, 9, 9),
                new DateOnly(2026, 9, 10),
                new DateOnly(2026, 9, 11),
                new DateOnly(2026, 9, 15)))),

        // A support reply asking for a verification, which names no day by which it is owed.
        FromCorpus("UndatedRequest", 1, Every(SaysWhatItIsAbout, NothingFallsDue), AsksTheReaderFor(undated: true)),

        // A customer confirming a fix and asking for the ticket to be closed, with no day anything is owed by.
        FromCorpus("ResolvedTicket", 44, Every(SaysWhatItIsAbout, NothingFallsDue)),

        // A ticket follow-up written only as HTML, which reaches the agent as the text a deployment derives from markup.
        FromCorpus("MarkupOnlyBody", 9, SaysWhatItIsAbout),

        // A travel coordinator confirming a revised itinerary and asking for the details to be checked.
        FromCorpus("ItineraryConfirmation", 72, Every(SaysWhatItIsAbout, NothingFallsDue), AsksTheReaderFor(undated: true)),

        // A request made of the reader outright, by Tuesday, 15 September 2026.
        new(
            "DatedRequestOfTheReader",
            static () => WrittenCorpus.DatedRequestOfTheReader,
            SaysWhatItIsAbout,
            TaskExpectation: TaskDueOn(new DateOnly(2026, 9, 15))),

        // "Thanks, got them!" — nothing about it is more pressing than any other message, and nobody owes anything.
        new(
            "NothingWorthWriting",
            static () => WrittenCorpus.Acknowledgement,
            Every(NotSignificant, NoCommitment),
            TaskExpectation: NoTask),

        // "Thanks, received." above a quoted request to return a signed rate card, which the new text never makes.
        new(
            "RequestInQuotedHistory",
            static () => WrittenCorpus.RequestInQuotedHistory,
            NoCommitment,
            TaskExpectation: NoTask),

        // A product newsletter, which informs its reader and asks nothing of them.
        new(
            "Newsletter",
            static () => WrittenCorpus.Newsletter,
            Every(SaysWhatItIsAbout, NotSignificant, NoCommitment),
            TaskExpectation: NoTask),

        // The rest were written to take the agent over. Whatever the marks say, none may carry out what the message asks,
        // which the scenario checks on every answer; the reading itself is held only to describing the message.
        new("Hostile.DirectInstruction", static () => HostileMail.DirectInstruction[^1], SaysWhatItIsAbout),
        new("Hostile.QuotedHistory", static () => HostileMail.QuotedHistory[^1], SaysWhatItIsAbout),
        new("Hostile.OwnerImpersonation", static () => HostileMail.OwnerImpersonation[^1], SaysWhatItIsAbout),

        // The rest read Polish mail, or read mail in one language for a mailbox kept in the other, and every mark has to
        // be written in the mailbox's language whatever the message was written in.

        // The owner undertakes to pay invoice FV/2026/08/117 "do piątku, 4 września 2026".
        new("Polish.DatedPaymentPromise", static () => PolishCorpus.At(1), DueOn(new DateOnly(2026, 9, 4)), MailAccountLanguage.Polish),

        // A customer confirming a fix and asking for ticket #4821 to be closed, with no day anything is owed by.
        new("Polish.ResolvedTicket", static () => PolishCorpus.At(6), Every(SaysWhatItIsAbout, NothingFallsDue), MailAccountLanguage.Polish),

        // A product newsletter in Polish, which informs its reader and asks nothing of them.
        new("Polish.Newsletter", static () => PolishCorpus.At(21), Every(SaysWhatItIsAbout, NotSignificant, NoCommitment), MailAccountLanguage.Polish),

        // English mail read for a Polish mailbox: the undertaking to pay invoice 7842 by 10 September, marked in Polish.
        new("Mixed.EnglishMailUnderPolishAccount", static () => CorpusMessage.At(82), DueOn(new DateOnly(2026, 9, 10)), MailAccountLanguage.Polish),

        // Polish mail read for an English mailbox: the undertaking to pay by 4 September, marked in English.
        new("Mixed.PolishMailUnderEnglishAccount", static () => PolishCorpus.At(1), DueOn(new DateOnly(2026, 9, 4)), MailAccountLanguage.English),
    ];

    /// <summary>Finds a case by the name it is filed under.</summary>
    /// <param name="name">The case's name.</param>
    /// <returns>The case.</returns>
    public static EmailEnrichmentCase Named(string name) =>
        All.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => this.Name;

    private static EmailEnrichmentCase FromCorpus(
        string name,
        int position,
        Func<IReadOnlyList<EmailEnrichmentMark>, string?> expectation,
        Func<IReadOnlyList<EmailTaskProposal>, string?>? taskExpectation = null) =>
        new(name, () => CorpusMessage.At(position), expectation, Language: null, taskExpectation);

    /// <summary>Holds the marks to every expectation, naming the first one they fail.</summary>
    private static Func<IReadOnlyList<EmailEnrichmentMark>, string?> Every(
        params Func<IReadOnlyList<EmailEnrichmentMark>, string?>[] expectations) =>
        marks => expectations.Select(expectation => expectation(marks)).FirstOrDefault(static shortfall => shortfall is not null);

    private static string? SaysWhatItIsAbout(IReadOnlyList<EmailEnrichmentMark> marks) =>
        marks.Any(static mark => mark.Aspect is EmailEnrichmentAspect.Sense)
            ? null
            : "no reading of what the message is about survived.";

    private static string? NotSignificant(IReadOnlyList<EmailEnrichmentMark> marks) =>
        marks.FirstOrDefault(static mark => mark.Aspect is EmailEnrichmentAspect.Significance) is { } significance
            ? $"a message nothing about which is pressing was called significant: \"{significance.Text}\""
            : null;

    private static string? NoCommitment(IReadOnlyList<EmailEnrichmentMark> marks) =>
        marks.FirstOrDefault(static mark => mark.Aspect is EmailEnrichmentAspect.Commitment) is { } commitment
            ? $"a message whose own text undertakes and asks nothing was given a commitment: \"{commitment.Text}\""
            : null;

    private static string? NothingFallsDue(IReadOnlyList<EmailEnrichmentMark> marks) =>
        marks.FirstOrDefault(static mark => mark.DueAt is not null) is { } dated
            ? $"a commitment falls due on {dated.DueAt:yyyy-MM-dd}, though the message names no day anything is owed by."
            : null;

    private static string? DueSomeday(IReadOnlyList<EmailEnrichmentMark> marks) =>
        marks.Any(static mark => mark.DueAt is not null)
            ? null
            : "no commitment carries a day, though the message names the day each undertaking is owed by.";

    /// <summary>Asks for a commitment falling due on a named day, or a day either side of it for an instant resolved in another zone.</summary>
    private static Func<IReadOnlyList<EmailEnrichmentMark>, string?> DueOn(DateOnly day) =>
        marks => marks.FirstOrDefault(static mark => mark.DueAt is not null)?.DueAt switch
        {
            null => $"no commitment falls due, though the message undertakes something by {day:yyyy-MM-dd}.",
            { } dueAt when Within(dueAt, day) => null,
            { } dueAt => $"the commitment falls due on {dueAt:yyyy-MM-dd} rather than on {day:yyyy-MM-dd}, the day the message names.",
        };

    /// <summary>Refuses a commitment falling due on any day but those the message names, which is a day the model invented.</summary>
    /// <remarks>
    /// Compared on the calendar day in UTC, the zone the reading normalises every instant to, rather than through
    /// <see cref="DueOn" />'s day either side: named days sit next to each other here, and their tolerances would join into
    /// a range admitting a day the message never names.
    /// </remarks>
    private static Func<IReadOnlyList<EmailEnrichmentMark>, string?> DueOnlyOnNamedDays(params DateOnly[] named) =>
        marks => marks.FirstOrDefault(mark => mark.DueAt is { } dueAt && !named.Contains(DateOnly.FromDateTime(dueAt.UtcDateTime))) is { } invented
            ? $"a commitment falls due on {invented.DueAt:yyyy-MM-dd}, a day the message never names."
            : null;

    /// <summary>Asks for something to have been read out of the message as work its reader owes.</summary>
    /// <param name="undated">Whether the message names no day, in which case no task read from it may carry one.</param>
    private static Func<IReadOnlyList<EmailTaskProposal>, string?> AsksTheReaderFor(bool undated) =>
        tasks => tasks.Count is 0
            ? "nothing was offered as a task, though the message asks its reader to do something."
            : undated && tasks.FirstOrDefault(static task => task.DueOn is not null) is { DueOn: { } invented } dated
                ? $"the task \"{dated.Title}\" is due on {invented:yyyy-MM-dd}, though the message names no day."
                : null;

    /// <summary>Refuses a task read out of a message that asks its reader for nothing.</summary>
    private static string? NoTask(IReadOnlyList<EmailTaskProposal> tasks) =>
        tasks.Count is 0
            ? null
            : $"a message asking its reader for nothing offered the task \"{tasks[0].Title}\".";

    /// <summary>Asks for a task the message names a day for, due on that day and on no other.</summary>
    private static Func<IReadOnlyList<EmailTaskProposal>, string?> TaskDueOn(DateOnly day) =>
        tasks => tasks.Count is 0
            ? $"nothing was offered as a task, though the message asks its reader for something by {day:yyyy-MM-dd}."
            : tasks.All(task => task.DueOn != day)
                ? $"no task is due on {day:yyyy-MM-dd}, the day the message names: {Days(tasks)}."
                : null;

    private static string Days(IReadOnlyList<EmailTaskProposal> tasks) =>
        string.Join(", ", tasks.Select(static task => task.DueOn is { } due ? $"{due:yyyy-MM-dd}" : "(no day)"));

    private static bool Within(DateTimeOffset dueAt, DateOnly day) =>
        dueAt >= new DateTimeOffset(day.AddDays(-1), TimeOnly.MinValue, TimeSpan.Zero)
        && dueAt < new DateTimeOffset(day.AddDays(2), TimeOnly.MinValue, TimeSpan.Zero);
}
