// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Domain.Emails;
using MailFathom.Evaluations.Corpus;

namespace MailFathom.Evaluations.Retrieval;

/// <summary>Ranks a mailbox against one question the way a deployment's semantic search ranks it, from memory rather than from PostgreSQL.</summary>
/// <remarks>
/// A message is as close as its closest passage, which is the minimum cosine distance the vector index reads over a
/// message's chunks, and a tie goes to the newer message and then to the identifier, as it does there. Every message is
/// ranked rather than the first few, so a case reports where its evidence landed however deep that is. A message with
/// no passage has no vector and is not ranked, as the index leaves it out.
/// </remarks>
internal static class SemanticRanking
{
    /// <summary>Ranks the mailbox against a question.</summary>
    /// <param name="mailbox">Each message beside the vectors of its passages.</param>
    /// <param name="question">The question's vector, in the same space.</param>
    /// <returns>The messages, closest first.</returns>
    public static IReadOnlyList<StoredEmailId> Rank(
        IReadOnlyList<(CorpusMessage Message, IReadOnlyList<EmbeddingVector> Passages)> mailbox,
        EmbeddingVector question) =>
        [
            .. mailbox
                .Where(static entry => entry.Passages.Count > 0)
                .Select(entry => (entry.Message, Similarity: entry.Passages.Max(passage => CosineSimilarity(passage, question))))
                .OrderByDescending(static scored => scored.Similarity)
                .ThenByDescending(static scored => scored.Message.ReceivedAt)
                .ThenByDescending(static scored => scored.Message.Id.Value)
                .Select(static scored => scored.Message.Id),
        ];

    /// <summary>Reads where each piece of a case's evidence landed in a ranking.</summary>
    /// <param name="retrievalCase">The case.</param>
    /// <param name="ranking">The messages its question ranked, closest first.</param>
    /// <returns>The rank of the first message carrying each piece of evidence.</returns>
    public static RetrievalCaseRanks RanksOf(RetrievalCase retrievalCase, IReadOnlyList<StoredEmailId> ranking)
    {
        ArgumentNullException.ThrowIfNull(retrievalCase);

        return new RetrievalCaseRanks(
            retrievalCase.Name,
            [
                .. retrievalCase.Evidence.Select(evidence => ranking
                    .Select(static (message, index) => (message, Rank: (int?)(index + 1)))
                    .FirstOrDefault(ranked => evidence.Messages.Contains(ranked.message))
                    .Rank),
            ]);
    }

    private static double CosineSimilarity(EmbeddingVector passage, EmbeddingVector question)
    {
        var left = passage.Components.Span;
        var right = question.Components.Span;

        if (left.Length != right.Length)
        {
            throw new InvalidOperationException("A passage and a question were embedded into spaces of different widths.");
        }

        double dot = 0;
        double leftLength = 0;
        double rightLength = 0;

        for (var index = 0; index < left.Length; index++)
        {
            dot += (double)left[index] * right[index];
            leftLength += (double)left[index] * left[index];
            rightLength += (double)right[index] * right[index];
        }

        return dot / Math.Sqrt(leftLength * rightLength);
    }
}
