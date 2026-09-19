// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Discovery.Planning;
using MailFathom.Application.Retrieval;

namespace MailFathom.Evaluations.Discovery;

/// <summary>One question put to the planning agent, and what a plan read from it has to say.</summary>
/// <remarks>
/// <para>
/// A question is written here rather than drawn from the corpus, because what is measured is the reading of a question
/// rather than of mail, and no mail reaches the agent. Every one of them is invented for this suite: a person it names
/// is nobody, and an address it names sits under a reserved domain, so a question, its cached answer, and its report can
/// be published like everything else a run keeps.
/// </para>
/// <para>
/// Most questions have one right reading, and <see cref="Expectation" /> states the part of it a structure can settle. An
/// ambiguous question has none, so it states nothing beyond a readable plan and is the one kind a judge grades.
/// </para>
/// </remarks>
/// <param name="Name">The name the case is filed and reported under.</param>
/// <param name="Question">The question, in a person's own words.</param>
/// <param name="IsAmbiguous">Whether the question reads two ways, so that no single plan is the right one.</param>
/// <param name="Expectation">Names what a plan read from the question gets wrong, or answers <see langword="null" /> when it gets nothing wrong.</param>
internal sealed record DiscoveryPlanningCase(
    string Name,
    string Question,
    bool IsAmbiguous,
    Func<DiscoveryRunPlan, string?> Expectation)
{
    /// <summary>The sender a question stating its own scope names.</summary>
    private const string StatedSender = "billing@northwind.example";

    /// <summary>The recipient a question stating whom it wrote to names.</summary>
    private const string StatedRecipient = "it-desk@tidewater.example";

    /// <summary>The person a question asks about, whose name differs from <see cref="OtherSimilarlyNamedPerson" /> by one syllable.</summary>
    private const string SimilarlyNamedPerson = "Ingrid Solheim";

    /// <summary>The person the same question rules out.</summary>
    private const string OtherSimilarlyNamedPerson = "Ingrid Solberg";

    /// <summary>Gets every case, in the order the report lists them.</summary>
    public static IReadOnlyList<DiscoveryPlanningCase> All { get; } =
    [
        new(
            "ExplicitScope",
            $"Which of the invoices {StatedSender} sent us are still unpaid?",
            IsAmbiguous: false,
            static plan => plan.Retrieval.Lookups.Any(static lookup =>
                string.Equals(lookup.SenderAddress?.Trim(), StatedSender, StringComparison.OrdinalIgnoreCase))
                ? null
                : $"no lookup is narrowed to the sender the question named, {StatedSender}."),
        new(
            "NoScope",
            "Has anybody confirmed the venue for the team offsite?",
            IsAmbiguous: false,
            static plan => plan.Retrieval.Lookups.FirstOrDefault(NarrowsByPartyOrDate) is { } narrowed
                ? $"a lookup for \"{narrowed.QueryText}\" is narrowed by a sender, a recipient, or a date the question never stated."
                : null),
        new(
            "NamesPerson",
            "What did Priya Raman say about moving the data migration to next quarter?",
            IsAmbiguous: false,
            static plan =>
            {
                if (plan.Retrieval.Lookups.FirstOrDefault(static lookup =>
                        lookup.SenderAddress is not null || lookup.RecipientAddress is not null) is { } invented)
                {
                    return $"a lookup for \"{invented.QueryText}\" is narrowed to an address the question never gave.";
                }

                return plan.Retrieval.Lookups.Any(static lookup =>
                    lookup.QueryText.Contains("Priya", StringComparison.OrdinalIgnoreCase)
                    || lookup.QueryText.Contains("Raman", StringComparison.OrdinalIgnoreCase))
                    ? null
                    : "no lookup searches for the person the question named.";
            }),
        new(
            "NamesPeriod",
            "What did the landlord write about the rent increase in March 2026?",
            IsAmbiguous: false,
            static plan => plan.Retrieval.Lookups.Any(CoversMarch2026)
                ? null
                : "no lookup is bounded to the month the question named, March 2026."),
        new(
            "OutsideWhatARunCanDo",
            "Send Tomasz a reply saying we accept the offer.",
            IsAmbiguous: false,
            static plan => plan.Intent == DiscoveryIntent.Unclassified
                ? null
                : $"a request to send mail, which no run can do, was read as {plan.Intent.Identity}."),
        new(
            "TracksAChange",
            "How did the date of our office move change over the summer?",
            IsAmbiguous: false,
            static plan => plan.Intent == DiscoveryIntent.TrackChange
                ? null
                : $"a question about how something changed over time was read as {plan.Intent.Identity}."),
        new(
            "ComparesOffers",
            "Compare the three removal quotes we got for the office move.",
            IsAmbiguous: false,
            static plan => plan.Intent == DiscoveryIntent.CompareTerms
                ? null
                : $"a question setting three quotes against each other was read as {plan.Intent.Identity}."),
        new(
            "LooksForFiles",
            "Find the floor plan PDF the facilities team sent me.",
            IsAmbiguous: false,
            static plan => plan.Intent == DiscoveryIntent.FindDocuments
                ? null
                : $"a question looking for a file was read as {plan.Intent.Identity}."),
        new(
            "NamesOneOfTwoSimilarPeople",
            $"What did {SimilarlyNamedPerson} say about the archive boxes? I do not mean {OtherSimilarlyNamedPerson}.",
            IsAmbiguous: false,
            static plan =>
            {
                if (plan.Retrieval.Lookups.FirstOrDefault(SearchesForTheOtherPerson) is { } confused)
                {
                    return $"a lookup for \"{confused.QueryText}\" searches for {OtherSimilarlyNamedPerson}, whom the question ruled out.";
                }

                return plan.Retrieval.Lookups.Any(static lookup => lookup.QueryText.Contains("Solheim", StringComparison.OrdinalIgnoreCase))
                    ? null
                    : $"no lookup searches for {SimilarlyNamedPerson} by the surname that tells the two apart.";
            }),
        new(
            "NamesAQuotedSpeaker",
            "What did Pál Horváth write about the goods lift in the message Ingrid forwarded to me?",
            IsAmbiguous: false,
            static plan =>
            {
                if (plan.Retrieval.Lookups.FirstOrDefault(static lookup =>
                        lookup.SenderAddress is not null || lookup.RecipientAddress is not null) is { } invented)
                {
                    return $"a lookup for \"{invented.QueryText}\" is narrowed to an address the question never gave.";
                }

                return plan.Retrieval.Lookups.Any(static lookup =>
                    lookup.QueryText.Contains("Horv", StringComparison.OrdinalIgnoreCase)
                    || lookup.QueryText.Contains("goods lift", StringComparison.OrdinalIgnoreCase))
                    ? null
                    : "no lookup searches for the person quoted or for what they wrote about.";
            }),
        new(
            "StatesARecipient",
            $"What did I send to {StatedRecipient} about the VPN certificate?",
            IsAmbiguous: false,
            static plan => plan.Retrieval.Lookups.Any(static lookup =>
                string.Equals(lookup.RecipientAddress?.Trim(), StatedRecipient, StringComparison.OrdinalIgnoreCase))
                ? null
                : $"no lookup is narrowed to the recipient the question named, {StatedRecipient}."),
        new(
            "NamesADateRange",
            "Which invoices from Brightwater arrived between 1 and 15 August 2026?",
            IsAmbiguous: false,
            static plan => plan.Retrieval.Lookups.Any(CoversFirstHalfOfAugust2026)
                ? null
                : "no lookup is bounded to the days the question named, 1 to 15 August 2026."),
        new(
            "AsksForFlagAndReadState",
            "Which of my starred messages from the movers have I still not read?",
            IsAmbiguous: false,
            static plan => plan.Retrieval.Lookups.Any(static lookup => lookup is { IsRemotelyFlagged: true, IsRemotelySeen: false })
                ? null
                : "no lookup is narrowed to starred mail that is still unread."),
        new(
            "AmbiguousDocumentOrFact",
            "Where are the Q3 numbers?",
            IsAmbiguous: true,
            static _ => null),
        new(
            "AmbiguousChangeOrFact",
            "What happened with the Contoso renewal?",
            IsAmbiguous: true,
            static _ => null),

        // The rest ask the questions above in Polish, and a plan read from one has to say what the English one's does.
        new(
            "Polish.ExplicitScope",
            $"Które faktury od {StatedSender} są jeszcze nieopłacone?",
            IsAmbiguous: false,
            static plan => plan.Retrieval.Lookups.Any(static lookup =>
                string.Equals(lookup.SenderAddress?.Trim(), StatedSender, StringComparison.OrdinalIgnoreCase))
                ? null
                : $"no lookup is narrowed to the sender the question named, {StatedSender}."),
        new(
            "Polish.NoScope",
            "Czy ktoś potwierdził miejsce wyjazdu integracyjnego zespołu?",
            IsAmbiguous: false,
            static plan => plan.Retrieval.Lookups.FirstOrDefault(NarrowsByPartyOrDate) is { } narrowed
                ? $"a lookup for \"{narrowed.QueryText}\" is narrowed by a sender, a recipient, or a date the question never stated."
                : null),
        new(
            "Polish.NamesPerson",
            "Co Agnieszka Dąbrowska napisała o błędzie eksportu raportu?",
            IsAmbiguous: false,
            static plan =>
            {
                if (plan.Retrieval.Lookups.FirstOrDefault(static lookup =>
                        lookup.SenderAddress is not null || lookup.RecipientAddress is not null) is { } invented)
                {
                    return $"a lookup for \"{invented.QueryText}\" is narrowed to an address the question never gave.";
                }

                return plan.Retrieval.Lookups.Any(static lookup =>
                    lookup.QueryText.Contains("Agnieszk", StringComparison.OrdinalIgnoreCase)
                    || lookup.QueryText.Contains("Dąbrowsk", StringComparison.OrdinalIgnoreCase))
                    ? null
                    : "no lookup searches for the person the question named.";
            }),
        new(
            "Polish.NamesPeriod",
            "Co wynajmujący pisał o podwyżce czynszu w marcu 2026?",
            IsAmbiguous: false,
            static plan => plan.Retrieval.Lookups.Any(CoversMarch2026)
                ? null
                : "no lookup is bounded to the month the question named, March 2026."),
        new(
            "Polish.OutsideWhatARunCanDo",
            "Wyślij Tomaszowi odpowiedź, że przyjmujemy ofertę.",
            IsAmbiguous: false,
            static plan => plan.Intent == DiscoveryIntent.Unclassified
                ? null
                : $"a request to send mail, which no run can do, was read as {plan.Intent.Identity}."),
        new(
            "Polish.TracksAChange",
            "Jak w ciągu lata zmieniała się data przeprowadzki naszego biura?",
            IsAmbiguous: false,
            static plan => plan.Intent == DiscoveryIntent.TrackChange
                ? null
                : $"a question about how something changed over time was read as {plan.Intent.Identity}."),
        new(
            "Polish.LooksForFiles",
            "Znajdź PDF z planem piętra, który przysłała mi administracja budynku.",
            IsAmbiguous: false,
            static plan => plan.Intent == DiscoveryIntent.FindDocuments
                ? null
                : $"a question looking for a file was read as {plan.Intent.Identity}."),
    ];

    /// <summary>Finds a case by the name it is filed under.</summary>
    /// <param name="name">The case's name.</param>
    /// <returns>The case.</returns>
    public static DiscoveryPlanningCase Named(string name) =>
        All.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

    /// <inheritdoc />
    public override string ToString() => this.Name;

    private static bool NarrowsByPartyOrDate(EmailKnowledgeQuery lookup) =>
        lookup.SenderAddress is not null
        || lookup.RecipientAddress is not null
        || lookup.ReceivedOnOrAfter is not null
        || lookup.ReceivedBefore is not null;

    /// <summary>Whether a lookup is bounded to March 2026, allowing a day either side for whichever offset a model wrote.</summary>
    private static bool CoversMarch2026(EmailKnowledgeQuery lookup) =>
        lookup is { ReceivedOnOrAfter: { } from, ReceivedBefore: { } before }
        && from >= new DateTimeOffset(2026, 2, 28, 0, 0, 0, TimeSpan.Zero)
        && from <= new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero)
        && before >= new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero)
        && before <= new DateTimeOffset(2026, 4, 2, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Whether a lookup is bounded to 1–15 August 2026, allowing a day either side for whichever offset a model wrote.</summary>
    private static bool CoversFirstHalfOfAugust2026(EmailKnowledgeQuery lookup) =>
        lookup is { ReceivedOnOrAfter: { } from, ReceivedBefore: { } before }
        && from >= new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero)
        && from <= new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero)
        && before >= new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero)
        && before <= new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Whether a lookup searches for the person the question ruled out, rather than excluding them with a leading minus.</summary>
    private static bool SearchesForTheOtherPerson(EmailKnowledgeQuery lookup) =>
        lookup.QueryText
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(static word => !word.StartsWith('-') && word.Contains("Solberg", StringComparison.OrdinalIgnoreCase));
}
