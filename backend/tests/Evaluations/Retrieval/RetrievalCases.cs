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
/// </remarks>
internal static class RetrievalCases
{
    /// <summary>Gets the mailbox every case is asked over.</summary>
    public static IReadOnlyList<CorpusMessage> Mailbox => PolishCorpus.MixedMailbox;

    /// <summary>Gets every case, the relevance filter's lookups first.</summary>
    /// <exception cref="InvalidOperationException">Thrown, naming the phrase, when no message of the mailbox carries a piece of evidence.</exception>
    public static IReadOnlyList<RetrievalCase> All =>
    [
        .. LabelledCandidates.Lookups.Select(static (lookup, position) => FromLabels(lookup, position)),
        .. MailAnsweringScenario.All.Where(static scenario => scenario.Evidence.Count > 0).Select(FromPhrases),
    ];

    private static RetrievalCase FromLabels(LabelledLookup lookup, int position) =>
        new(
            string.Create(CultureInfo.InvariantCulture, $"RelevanceFilter.Lookup{position + 1}"),
            lookup.QueryText,
            [
                .. lookup.Candidates
                    .Where(static candidate => candidate.Answers)
                    .Select(static candidate => new RetrievalEvidence(
                        candidate.Evidence,
                        new HashSet<StoredEmailId> { CorpusMessage.At(candidate.MessagePosition).Id })),
            ]);

    private static RetrievalCase FromPhrases(MailAnsweringScenario scenario) =>
        new(
            scenario.Name,
            scenario.Question,
            [.. scenario.Evidence.Select(static phrase => new RetrievalEvidence(phrase, MessagesCarrying(phrase)))]);

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
