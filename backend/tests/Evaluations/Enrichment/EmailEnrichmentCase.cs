// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Enrichment;
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
/// <see cref="WrittenCorpus" />.
/// </para>
/// </remarks>
/// <param name="Name">The name the case is filed and reported under.</param>
/// <param name="Message">Reads the message.</param>
/// <param name="Expectation">Names what the marks get wrong, or answers <see langword="null" /> when they get nothing wrong.</param>
internal sealed record EmailEnrichmentCase(
    string Name,
    Func<CorpusMessage> Message,
    Func<IReadOnlyList<EmailEnrichmentMark>, string?> Expectation)
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
        FromCorpus("UndatedRequest", 1, Every(SaysWhatItIsAbout, NothingFallsDue)),

        // A customer confirming a fix and asking for the ticket to be closed, with no day anything is owed by.
        FromCorpus("ResolvedTicket", 44, Every(SaysWhatItIsAbout, NothingFallsDue)),

        // A ticket follow-up written only as HTML, which reaches the agent as the text a deployment derives from markup.
        FromCorpus("MarkupOnlyBody", 9, SaysWhatItIsAbout),

        // A travel coordinator confirming a revised itinerary and asking for the details to be checked.
        FromCorpus("ItineraryConfirmation", 72, Every(SaysWhatItIsAbout, NothingFallsDue)),

        // "Thanks, got them!" — nothing about it is more pressing than any other message, and nobody owes anything.
        new("NothingWorthWriting", static () => WrittenCorpus.Acknowledgement, Every(NotSignificant, NoCommitment)),

        // "Thanks, received." above a quoted request to return a signed rate card, which the new text never makes.
        new("RequestInQuotedHistory", static () => WrittenCorpus.RequestInQuotedHistory, NoCommitment),

        // A product newsletter, which informs its reader and asks nothing of them.
        new("Newsletter", static () => WrittenCorpus.Newsletter, Every(SaysWhatItIsAbout, NotSignificant, NoCommitment)),
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
        Func<IReadOnlyList<EmailEnrichmentMark>, string?> expectation) =>
        new(name, () => CorpusMessage.At(position), expectation);

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
    private static Func<IReadOnlyList<EmailEnrichmentMark>, string?> DueOnlyOnNamedDays(params DateOnly[] named) =>
        marks => marks.FirstOrDefault(mark => mark.DueAt is { } dueAt && !named.Any(day => Within(dueAt, day))) is { } invented
            ? $"a commitment falls due on {invented.DueAt:yyyy-MM-dd}, a day the message never names."
            : null;

    private static bool Within(DateTimeOffset dueAt, DateOnly day) =>
        dueAt >= new DateTimeOffset(day.AddDays(-1), TimeOnly.MinValue, TimeSpan.Zero)
        && dueAt < new DateTimeOffset(day.AddDays(2), TimeOnly.MinValue, TimeSpan.Zero);
}
