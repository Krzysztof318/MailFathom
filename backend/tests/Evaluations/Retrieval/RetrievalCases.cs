// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Domain.Emails;
using MailFathom.Evaluations.Answering;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.RelevanceFilter;

namespace MailFathom.Evaluations.Retrieval;

/// <summary>Semantic search's ground truth, read from the labels two other scenarios already hold rather than written a third time.</summary>
/// <remarks>
/// <para>
/// The relevance filter's lookups name the messages that answer them outright, beside near misses chosen to resemble
/// them, so each answering candidate is one piece of evidence carried by its own message. The answering scenario's
/// questions name phrases instead, and a phrase is carried by whichever messages of the mailbox hold it — a reply
/// quoting the answer reaches it as surely as the message that first gave it — so each phrase is one piece of evidence
/// carried by all of them. A question the corpus does not answer has nothing to reach and is not a case here.
/// </para>
/// <para>
/// Every case is asked over <see cref="Mailbox" />, the English, hand-written, hostile, and Polish mail together, so a
/// question has to find its answer among everything the other scenarios read and a Polish question has to cross into
/// English mail where that is where its answer is.
/// </para>
/// <para>
/// Each case also carries the keyword query a model writes from its question, because that, and not the sentence, is
/// what reaches the full-text index when a model searches. The queries are written here from the question alone, the
/// way <c>EmailSearchQueryText.MatchingDescription</c> asks a model to write one, and never from the evidence, so a
/// lexical ranking is not credited with words only somebody who had read the answer would choose.
/// </para>
/// </remarks>
internal static class RetrievalCases
{
    private static readonly Dictionary<string, string> KeywordQueries = new(StringComparer.Ordinal)
    {
        ["RelevanceFilter.Lookup1"] = "NV 418 Port Alder",
        ["RelevanceFilter.Lookup2"] = "INV-ATLAS-1031",
        ["RelevanceFilter.Lookup3"] = "QuillDesk export timeout",
        ["RelevanceFilter.Lookup4"] = "SurveyDesk confirmation-panel timeout",
        ["RelevanceFilter.Lookup5"] = "Solmere hotel",
        ["RelevanceFilter.Lookup6"] = "INV-4798 payment",
        ["RelevanceFilter.Lookup7"] = "Norvale hotel",
        ["RelevanceFilter.Lookup8"] = "INV-4827 billing portal",
        ["MailAnswering.OneMessageAnswers"] = "LumenDesk export error",
        ["MailAnswering.SeveralMessagesAnswer"] = "LumenDesk OR Atlas build",
        ["MailAnswering.NamesAPerson"] = "Halina Pettersen Solmere",
        ["MailAnswering.NamesADateRange"] = "itinerary confirmed",
        ["MailAnswering.KeywordMatchesTheWrongMessage"] = "INV-4827 billing address",
        ["MailAnswering.LaterMessageCorrectsAnEarlierOne"] = "Kestrel Quay move",
        ["MailAnswering.AnswerSitsFarFromTheThreadStart"] = "Kestrel Quay parking",
        ["MailAnswering.QuotedHistoryWithSeveralSpeakers"] = "Kestrel Quay goods lift",
        ["MailAnswering.RelativeDateResolvedAgainstTheMessage"] = "Kestrel Quay key cards",
        ["MailAnswering.TwoPeopleWithSimilarNames"] = "Ingrid Solheim courier",
        ["MailAnswering.NamesWhoDoesWhatInALongThread"] = "Kestrel Quay fibre",
        ["MailAnswering.GathersFactsFromSeveralTurns"] = "Kestrel Quay desks",
        ["MailAnswering.Hostile.DirectInstruction"] = "Brightwater House visitors",
        ["MailAnswering.Hostile.ForgedTurn"] = "Tidewell Print flyers",
        ["MailAnswering.Hostile.Disclosure"] = "Quayside Supplies paper",
        ["MailAnswering.Polish.OneMessageAnswers"] = "4821 eksport",
        ["MailAnswering.Polish.PromisedPaymentDay"] = "FV/2026/08/117",
        ["MailAnswering.Polish.LaterMessageCorrectsAnEarlierOne"] = "Wrzosowa OR Wrzosową OR Wrzosowej",
        ["MailAnswering.Mixed.PolishQuestionAboutEnglishMail"] = "LumenDesk eksport OR export",
        ["MailAnswering.Mixed.EnglishQuestionAboutPolishMail"] = "Gdańsk OR Gdańska train OR pociąg",
        ["MailAnswering.Mixed.EnglishQuestionAboutAnInflectedWord"] = "Wrzosowa OR Wrzosową OR Wrzosowej",
        ["MailAnswering.Mixed.PolishRequestToQuoteEnglishMail"] = "LumenDesk eksport OR export",
        ["MailAnswering.Mixed.EnglishRequestToQuotePolishMail"] = "4821 export OR eksport",
    };

    /// <summary>Gets the mailbox every case is asked over.</summary>
    public static IReadOnlyList<CorpusMessage> Mailbox => PolishCorpus.MixedMailbox;

    /// <summary>Gets every case, the relevance filter's lookups first.</summary>
    /// <exception cref="InvalidOperationException">Thrown, naming the phrase or the case, when no message of the mailbox carries a piece of evidence or a case has no keyword query.</exception>
    public static IReadOnlyList<RetrievalCase> All =>
    [
        .. LabelledCandidates.Lookups.Select(static (lookup, position) => FromLabels(lookup, position)),
        .. MailAnsweringScenario.All.Where(static scenario => scenario.Evidence.Count > 0).Select(FromPhrases),
    ];

    private static RetrievalCase FromLabels(LabelledLookup lookup, int position)
    {
        var name = string.Create(CultureInfo.InvariantCulture, $"RelevanceFilter.Lookup{position + 1}");

        return new(
            name,
            lookup.QueryText,
            KeywordsOf(name),
            [
                .. lookup.Candidates
                    .Where(static candidate => candidate.Answers)
                    .Select(static candidate => new RetrievalEvidence(
                        candidate.Evidence,
                        new HashSet<StoredEmailId> { CorpusMessage.At(candidate.MessagePosition).Id })),
            ]);
    }

    private static RetrievalCase FromPhrases(MailAnsweringScenario scenario) =>
        new(
            scenario.Name,
            scenario.Question,
            KeywordsOf(scenario.Name),
            [.. scenario.Evidence.Select(static phrase => new RetrievalEvidence(phrase, MessagesCarrying(phrase)))]);

    private static string KeywordsOf(string caseName) =>
        KeywordQueries.TryGetValue(caseName, out var keywords)
            ? keywords
            : throw new InvalidOperationException($"The retrieval case {caseName} has no keyword query.");

    /// <summary>Finds every message carrying a phrase, the way the answering scenario holds a citation to it.</summary>
    private static HashSet<StoredEmailId> MessagesCarrying(string phrase)
    {
        var carriers = Mailbox
            .Where(message => message.GroundingText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            .Select(static message => message.Id)
            .ToHashSet();

        return carriers.Count > 0
            ? carriers
            : throw new InvalidOperationException($"No message of the mailbox carries \"{phrase}\", so the evidence names nothing.");
    }
}
