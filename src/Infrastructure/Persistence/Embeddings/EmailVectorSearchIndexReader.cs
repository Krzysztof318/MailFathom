// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>Ranks stored mail by pgvector distance from a query vector, under one profile and one set of filters.</summary>
/// <remarks>
/// <para>
/// The distance operator is chosen from the profile's own metric rather than fixed, because the metric is part of what
/// the profile's vectors mean: measuring a space built for inner product by cosine returns a number rather than an
/// error, and the results would be quietly worse instead of visibly wrong. All three operators pgvector offers order
/// the same way — a smaller value is nearer — so the ranking below is written once.
/// </para>
/// <para>
/// The structured filters join the ranking rather than trailing it. That is what the use case requires and it is also
/// what makes the query honest: post-filtering would measure the query against mail the caller may not see in order to
/// decide the order of mail they may, and would return fewer results than asked for exactly when the caller narrowed
/// the scope most.
/// </para>
/// <para>
/// It follows from that join that the approximate index a profile owns cannot serve this query: an HNSW scan orders the
/// whole table, and a filter on a joined table is not something the index can carry. The ranking is therefore exact,
/// which is the trade this feature is willing to make — an exact ranking is deterministic, and determinism is what the
/// fused order rests on. There is no measurement here saying an approximate ranking would be faster on a mailbox this
/// system holds; when there is one, it belongs in this method and nowhere else.
/// </para>
/// <para>
/// The query vector reaches PostgreSQL as a parameter, like every other value a request carries. Nothing about a query
/// is composed into the statement text.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailVectorSearchIndexReader(MailFathomDbContext dbContext) : IEmailVectorSearchIndexReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RankedEmailCandidate>> ReadNearestCandidatesAsync(
        MailboxEmailSelection selection,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var hits = await this.NearestHitsQuery(selection, profile, queryVector, limit)
            .ToArrayAsync(cancellationToken);

        return
        [
            .. hits.Select(static hit => new RankedEmailCandidate(
                new EmailTimelinePosition(hit.ReceivedAt, StoredEmailId.Create(hit.StoredEmailId)),
                (float)hit.Distance)),
        ];
    }

    /// <summary>Composes the query that ranks the eligible emails by their nearest embedded passage.</summary>
    /// <param name="selection">The validated structural filters.</param>
    /// <param name="profile">The profile whose vectors are searched and whose metric measures the distance.</param>
    /// <param name="queryVector">Where the query lands in that profile's space.</param>
    /// <param name="limit">The greatest number of emails to return.</param>
    /// <returns>The composed query, which nothing has executed yet.</returns>
    /// <remarks>
    /// Exposed for the reason the lexical ranking query is: that the query vector arrives as a parameter and that the
    /// profile narrows the rows before any distance is measured are claims about the generated command, and a test
    /// asserting them against anything else would pass whatever this method did.
    /// </remarks>
    internal IQueryable<StoredEmailVectorHitRow> NearestHitsQuery(
        MailboxEmailSelection selection,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit)
    {
        var profileId = profile.Id.Value;
        var target = new Vector(queryVector.Components);

        // Mail this profile has no vector for is excluded here rather than ranked as infinitely distant: the nearest
        // passage of a message with no embedded passage is nothing at all, and letting the aggregate answer that would
        // be a null where a distance belongs.
        var eligibleEmails = StoredEmailSelectionPredicate
            .Matching(dbContext.StoredEmails.AsNoTracking(), selection)
            .Where(email => email.Chunks.Any(chunk =>
                chunk.Embeddings.Any(vector => vector.EmbeddingProfileId == profileId)));

        return NearestFirst(eligibleEmails, profile.Identity.DistanceMetric, profileId, target, limit);
    }

    /// <summary>Ranks the eligible emails nearest first, each measured by its own nearest passage.</summary>
    /// <remarks>
    /// <para>
    /// One branch per metric, each restating the whole query, because the distance operator has to be part of the
    /// expression tree PostgreSQL is handed: a method or a local choosing it would either fail to translate or be
    /// evaluated once on the client against vectors that never left the database. The alternative — projecting the
    /// distance into a row and grouping over that row — is what the provider refuses to translate, so the shape here
    /// is a consequence of the query rather than a preference.
    /// </para>
    /// <para>
    /// A correlated minimum over the message's own passages is what makes each email one row, scored by its nearest
    /// one. Ranking passages instead would let a single long message fill a window with its own paragraphs while a
    /// shorter message that answers the query better never appeared. The same subquery is written into the ordering and
    /// into the projection, exactly as the lexical ranking writes its rank expression twice, and PostgreSQL computes it
    /// once.
    /// </para>
    /// </remarks>
    private static IQueryable<StoredEmailVectorHitRow> NearestFirst(
        IQueryable<StoredEmailEntity> eligibleEmails,
        EmbeddingDistanceMetric distanceMetric,
        Guid profileId,
        Vector target,
        int limit) => distanceMetric switch
        {
            EmbeddingDistanceMetric.Cosine => eligibleEmails
                .OrderBy(email => email.Chunks
                    .SelectMany(chunk => chunk.Embeddings)
                    .Where(vector => vector.EmbeddingProfileId == profileId)
                    .Min(vector => vector.Embedding.CosineDistance(target)))
                .ThenBy(email => email.ReceivedAt == null)
                .ThenByDescending(email => email.ReceivedAt)
                .ThenByDescending(email => email.Id)
                .Take(limit)
                .Select(email => new StoredEmailVectorHitRow(
                    email.Id,
                    email.ReceivedAt,
                    email.Chunks
                        .SelectMany(chunk => chunk.Embeddings)
                        .Where(vector => vector.EmbeddingProfileId == profileId)
                        .Min(vector => vector.Embedding.CosineDistance(target)))),
            EmbeddingDistanceMetric.InnerProduct => eligibleEmails
                .OrderBy(email => email.Chunks
                    .SelectMany(chunk => chunk.Embeddings)
                    .Where(vector => vector.EmbeddingProfileId == profileId)
                    .Min(vector => vector.Embedding.MaxInnerProduct(target)))
                .ThenBy(email => email.ReceivedAt == null)
                .ThenByDescending(email => email.ReceivedAt)
                .ThenByDescending(email => email.Id)
                .Take(limit)
                .Select(email => new StoredEmailVectorHitRow(
                    email.Id,
                    email.ReceivedAt,
                    email.Chunks
                        .SelectMany(chunk => chunk.Embeddings)
                        .Where(vector => vector.EmbeddingProfileId == profileId)
                        .Min(vector => vector.Embedding.MaxInnerProduct(target)))),
            EmbeddingDistanceMetric.EuclideanDistance => eligibleEmails
                .OrderBy(email => email.Chunks
                    .SelectMany(chunk => chunk.Embeddings)
                    .Where(vector => vector.EmbeddingProfileId == profileId)
                    .Min(vector => vector.Embedding.L2Distance(target)))
                .ThenBy(email => email.ReceivedAt == null)
                .ThenByDescending(email => email.ReceivedAt)
                .ThenByDescending(email => email.Id)
                .Take(limit)
                .Select(email => new StoredEmailVectorHitRow(
                    email.Id,
                    email.ReceivedAt,
                    email.Chunks
                        .SelectMany(chunk => chunk.Embeddings)
                        .Where(vector => vector.EmbeddingProfileId == profileId)
                        .Min(vector => vector.Embedding.L2Distance(target)))),
            _ => throw new ArgumentOutOfRangeException(
                nameof(distanceMetric),
                distanceMetric,
                "The distance metric has no pgvector operator."),
        };
}
