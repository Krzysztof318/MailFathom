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
            "AmbiguousDocumentOrFact",
            "Where are the Q3 numbers?",
            IsAmbiguous: true,
            static _ => null),
        new(
            "AmbiguousChangeOrFact",
            "What happened with the Contoso renewal?",
            IsAmbiguous: true,
            static _ => null),
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
}
