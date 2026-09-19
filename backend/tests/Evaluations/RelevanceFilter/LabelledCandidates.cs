// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Evaluations.RelevanceFilter;

/// <summary>The relevance filter's ground truth: lookups over the synthetic corpus, each beside passages labelled as answering it or not.</summary>
/// <remarks>
/// <para>
/// A candidate is named by the message it comes from and by a phrase only the passage holding it carries, rather than by
/// a chunk's ordinal, so a label says which text answers and survives a change to the chunking rules that moves where a
/// passage ends. A phrase no passage of its message carries fails the run naming it rather than labelling nothing.
/// </para>
/// <para>
/// Most of what does not answer a lookup is chosen to resemble what does: another invoice outstanding, another trip's
/// hotel, another product's export workaround, the same fault mentioned without its reproduction. A filter that only has
/// to tell travel from invoices is not being measured on what it is for, which is telling the one message that settles a
/// question from the several that discuss its subject.
/// </para>
/// </remarks>
internal static class LabelledCandidates
{
    /// <summary>Gets every labelled lookup, each with at least one passage that answers it and several that do not.</summary>
    public static IReadOnlyList<LabelledLookup> Lookups { get; } =
    [
        new(
            "When does flight NV 418 leave Port Alder?",
            [
                Answering(28, "Flight NV 418 will depart Port Alder at 12:05"),
                NotAnswering(43, "Flight NB218: Northbay to Solmere"),
                NotAnswering(16, "arrival in Larkhaven on 18 March"),
                NotAnswering(6, "Wednesday, 14 October 2026, from 10:00 to 10:45 CET"),
            ]),
        new(
            "Has invoice INV-ATLAS-1031 been paid?",
            [
                Answering(6, "Payment for invoice INV-ATLAS-1031"),
                Answering(7, "Payment for INV-ATLAS-1031"),
                NotAnswering(26, "INV-260825"),
                NotAnswering(3, "Invoice INV-6044, for $860.00"),
                NotAnswering(28, "Lantern Quay Hotel, booking LQ-60418"),
            ]),
        new(
            "What is the workaround for the QuillDesk export timeout on filtered projects?",
            [
                Answering(10, "clearing the status filter"),
                NotAnswering(47, "exporting with a single team selected"),
                NotAnswering(16, "running one export at a time"),
                NotAnswering(43, "Silverline Cars pickup at Solmere Airport"),
            ]),
        new(
            "How is the SurveyDesk confirmation-panel timeout reproduced?",
            [
                Answering(27, "After approximately 30 seconds, the panel times out"),
                NotAnswering(26, "investigate the confirmation-panel timeout"),
                NotAnswering(47, "Set the date range to August 25"),
                NotAnswering(11, "642-task project"),
                NotAnswering(7, "Cedar conference room"),
            ]),
        new(
            "Which hotel is booked for the Solmere trip?",
            [
                Answering(43, "Juniper Quay Hotel in Solmere"),
                NotAnswering(28, "Lantern Quay Hotel, booking LQ-60418"),
                NotAnswering(16, "Harbor Quay Hotel reservation"),
                NotAnswering(10, "A project containing 642 tasks"),
            ]),
        new(
            "When is the €1,150.00 payment for INV-4798 scheduled to settle?",
            [
                Answering(38, "scheduled for settlement on 10 September 2026"),
                Answering(39, "Invoice INV-4798 remains scheduled for settlement on 10 September 2026"),
                NotAnswering(68, "Payment for the outstanding balance on INV-4798 is scheduled for 10 September 2026"),
                NotAnswering(77, "remittance reference is KHL-0306-92"),
                NotAnswering(98, "scheduled payment of EUR 2,140.00 for September 11, 2026"),
            ]),
        new(
            "Which hotel is booked for the Norvale trip?",
            [
                Answering(87, "Harbor Lantern Hotel has been confirmed for four nights"),
                Answering(88, "four-night Harbor Lantern Hotel reservation"),
                NotAnswering(85, "Four nights at Juniper Quay Hotel"),
                NotAnswering(95, "The Lantern House, Lydmere"),
                NotAnswering(72, "Hotel: Harbor Glass Hotel"),
            ]),
        new(
            "How was the billing portal fixed after it dropped Suite 310 from INV-4827?",
            [
                Answering(21, "corrected the postal-code validation rule"),
                Answering(22, "The manual workaround and the corrected postal-code validation rule resolve the issue"),
                NotAnswering(62, "Billing entity validation failed"),
                NotAnswering(66, "clear that value, save the invoice"),
                NotAnswering(34, "regenerated the PDF and synchronized the billing record"),
            ]),
    ];

    private static LabelledCandidate Answering(int messagePosition, string evidence) =>
        new(messagePosition, evidence, Answers: true);

    private static LabelledCandidate NotAnswering(int messagePosition, string evidence) =>
        new(messagePosition, evidence, Answers: false);
}
