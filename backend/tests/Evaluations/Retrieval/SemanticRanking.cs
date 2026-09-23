// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Domain.Emails;
using MailFathom.Evaluations.Corpus;
using MailFathom.Host.Configuration.Embeddings;
using MailFathom.Infrastructure.Persistence.Embeddings;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Evaluations.Retrieval;

/// <summary>Ranks a mailbox against one question the way a deployment's semantic search ranks it, from memory rather than from PostgreSQL.</summary>
/// <remarks>
/// A message is as close as its closest passage, measured under the distance a deployment declares by default, and a
/// tie is broken by the vector index's own ordering over the stored rows. The distances are computed here rather than
/// by pgvector's operators, which have no form outside PostgreSQL, so each follows the operator's definition. Every
/// message is ranked rather than the first few, so a case reports where its evidence landed however deep that is. A
/// message with no passage has no vector and is not ranked, as the index leaves it out.
/// </remarks>
internal static class SemanticRanking
{
    /// <summary>Ranks the mailbox against a question.</summary>
    /// <param name="mailbox">Each message beside the vectors of its passages.</param>
    /// <param name="question">The question's vector, in the same space.</param>
    /// <returns>The messages, closest first.</returns>
    public static IReadOnlyList<StoredEmailId> Rank(
        IReadOnlyList<(CorpusMessage Message, IReadOnlyList<EmbeddingVector> Passages)> mailbox,
        EmbeddingVector question)
    {
        var metric = new EmbeddingEndpointOptions().DistanceMetric;
        var distances = mailbox
            .Where(static entry => entry.Passages.Count > 0)
            .ToDictionary(
                static entry => entry.Message.Id.Value,
                entry => entry.Passages.Min(passage => DistanceBetween(passage, question, metric)));
        var ranked = mailbox
            .Where(entry => distances.ContainsKey(entry.Message.Id.Value))
            .Select(static entry => new StoredEmailEntity
            {
                Id = entry.Message.Id.Value,
                MailboxAccountId = string.Empty,
                MailFolder = null!,
                ReceivedAt = entry.Message.ReceivedAt,
            })
            .AsQueryable();

        return
        [
            .. EmailVectorSearchIndexReader
                .NewestFirstAmongEqual(ranked.OrderBy(email => distances[email.Id]))
                .AsEnumerable()
                .Select(static email => StoredEmailId.Create(email.Id)),
        ];
    }

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

    /// <summary>Measures a passage against the question the way pgvector's operator for the metric does: smaller is closer.</summary>
    private static double DistanceBetween(EmbeddingVector passage, EmbeddingVector question, EmbeddingDistanceMetric metric)
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
        double squaredDistance = 0;

        for (var index = 0; index < left.Length; index++)
        {
            dot += (double)left[index] * right[index];
            leftLength += (double)left[index] * left[index];
            rightLength += (double)right[index] * right[index];
            squaredDistance += ((double)left[index] - right[index]) * ((double)left[index] - right[index]);
        }

        return metric switch
        {
            EmbeddingDistanceMetric.Cosine => 1 - (dot / Math.Sqrt(leftLength * rightLength)),
            EmbeddingDistanceMetric.InnerProduct => -dot,
            EmbeddingDistanceMetric.EuclideanDistance => Math.Sqrt(squaredDistance),
            _ => throw new InvalidOperationException($"No distance is defined for {metric}."),
        };
    }
}
