// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText;
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
/// It follows from that join that an approximate index cannot serve this query: an HNSW scan orders the whole table,
/// and a filter on a joined table is not something the index can carry. The ranking is therefore exact, which is the
/// trade this feature is willing to make — an exact ranking is deterministic, and determinism is what the fused order
/// rests on.
/// </para>
/// <para>
/// That trade has been measured rather than assumed, on 100 000 messages and 259 034 vectors at 1536 dimensions, and
/// the exactness stands: a five-hundred-row approximate window comes back as forty rows and keeps a median of four of
/// them once the caller's own folder and date filters are applied, and none at all once they name a sender.
/// A profile-partial HNSW index built over that corpus was never chosen by this query's plan, and it cost 3073 MB
/// beside a 3137 MB table, forty-five minutes to build, and a hundredfold slowdown on every embedding write — so
/// MailFathom builds none. <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/architecture/semantic-ranking-cost.md">What
/// a semantic search costs</see> holds the whole measurement, including the one thing it found that is worth changing:
/// the ordering key below is a correlated subquery re-entered per message, and the same exact answer written as one
/// pass costs less than a quarter of it.
/// </para>
/// <para>
/// Two rankings come out of one eligible set, on the partition
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// requires: what a person wrote — a body, or a document attachment's own words — and what a model said a picture
/// shows. The two are separated by the kind recorded on the attachment row the passage hangs on, so what decides it is
/// a stored column rather than a media type re-read per query, and a passage cut from the body belongs to the written
/// side by carrying no attachment position at all.
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
    public async Task<SemanticEmailRankings> ReadNearestCandidatesAsync(
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

        // Sequential rather than concurrent: both statements reach the same scoped context, which serves one operation
        // at a time, so starting them together would fault instead of overlapping.
        var written = await this.RankAsync(selection, profile, queryVector, limit, depicted: false, cancellationToken);
        var depicted = await this.RankAsync(selection, profile, queryVector, limit, depicted: true, cancellationToken);

        return new SemanticEmailRankings(written, depicted);
    }

    /// <summary>Composes the query that ranks the eligible emails by their nearest embedded passage of one kind.</summary>
    /// <param name="selection">The validated structural filters.</param>
    /// <param name="profile">The profile whose vectors are searched and whose metric measures the distance.</param>
    /// <param name="queryVector">Where the query lands in that profile's space.</param>
    /// <param name="limit">The greatest number of emails to return.</param>
    /// <param name="depicted">Whether the ranking measures descriptions of pictures rather than words a person wrote.</param>
    /// <returns>The composed query, which nothing has executed yet.</returns>
    /// <remarks>
    /// Exposed for the reason the lexical ranking query is: that the query vector arrives as a parameter, that the
    /// profile narrows the rows before any distance is measured, and that the two rankings read disjoint passages of one
    /// eligible set are claims about the generated command, and a test asserting them against anything else would pass
    /// whatever this method did.
    /// </remarks>
    internal IQueryable<StoredEmailVectorHitRow> NearestHitsQuery(
        MailboxEmailSelection selection,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        bool depicted)
    {
        var profileId = profile.Id.Value;
        var target = new Vector(queryVector.Components);

        // Mail this profile has no vector for is excluded here rather than ranked as infinitely distant: the nearest
        // passage of a message with no embedded passage is nothing at all, and letting the aggregate answer that would
        // be a null where a distance belongs. The kind narrows the same way, so a message whose only vectors are
        // descriptions is absent from the written ranking rather than present in it at some distance.
        var eligibleEmails = StoredEmailSelectionPredicate
            .Matching(dbContext.StoredEmails.AsNoTracking(), selection)
            .Where(email => email.Chunks.Any(chunk =>
                email.AttachmentTexts.Any(text =>
                    text.AttachmentPosition == chunk.AttachmentPosition
                    && text.Kind == AttachmentTextKind.ImageDescription) == depicted
                && chunk.Embeddings.Any(vector => vector.EmbeddingProfileId == profileId)));

        return NearestFirst(eligibleEmails, profile.Identity.DistanceMetric, profileId, target, depicted, limit);
    }

    /// <summary>Runs one of the two rankings and reads it into candidates.</summary>
    private async Task<IReadOnlyList<RankedEmailCandidate>> RankAsync(
        MailboxEmailSelection selection,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        bool depicted,
        CancellationToken cancellationToken)
    {
        var hits = await this.NearestHitsQuery(selection, profile, queryVector, limit, depicted)
            .ToArrayAsync(cancellationToken);

        return
        [
            .. hits.Select(static hit => new RankedEmailCandidate(
                new EmailTimelinePosition(hit.ReceivedAt, StoredEmailId.Create(hit.StoredEmailId)),
                (float)hit.Distance)),
        ];
    }

    /// <summary>Ranks the eligible emails nearest first, each measured by its own nearest passage of the chosen kind.</summary>
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
    /// <para>
    /// The kind test is written as an equality against the caller's choice rather than as two predicates, because the
    /// two sides are exactly complementary: a passage hangs on a described picture or it does not, and a body passage —
    /// which hangs on no attachment at all — falls to the written side by the same test.
    /// </para>
    /// </remarks>
    private static IQueryable<StoredEmailVectorHitRow> NearestFirst(
        IQueryable<StoredEmailEntity> eligibleEmails,
        EmbeddingDistanceMetric distanceMetric,
        Guid profileId,
        Vector target,
        bool depicted,
        int limit) => distanceMetric switch
        {
            EmbeddingDistanceMetric.Cosine => eligibleEmails
                .OrderBy(email => email.Chunks
                    .Where(chunk => email.AttachmentTexts.Any(text =>
                        text.AttachmentPosition == chunk.AttachmentPosition
                        && text.Kind == AttachmentTextKind.ImageDescription) == depicted)
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
                        .Where(chunk => email.AttachmentTexts.Any(text =>
                            text.AttachmentPosition == chunk.AttachmentPosition
                            && text.Kind == AttachmentTextKind.ImageDescription) == depicted)
                        .SelectMany(chunk => chunk.Embeddings)
                        .Where(vector => vector.EmbeddingProfileId == profileId)
                        .Min(vector => vector.Embedding.CosineDistance(target)))),
            EmbeddingDistanceMetric.InnerProduct => eligibleEmails
                .OrderBy(email => email.Chunks
                    .Where(chunk => email.AttachmentTexts.Any(text =>
                        text.AttachmentPosition == chunk.AttachmentPosition
                        && text.Kind == AttachmentTextKind.ImageDescription) == depicted)
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
                        .Where(chunk => email.AttachmentTexts.Any(text =>
                            text.AttachmentPosition == chunk.AttachmentPosition
                            && text.Kind == AttachmentTextKind.ImageDescription) == depicted)
                        .SelectMany(chunk => chunk.Embeddings)
                        .Where(vector => vector.EmbeddingProfileId == profileId)
                        .Min(vector => vector.Embedding.MaxInnerProduct(target)))),
            EmbeddingDistanceMetric.EuclideanDistance => eligibleEmails
                .OrderBy(email => email.Chunks
                    .Where(chunk => email.AttachmentTexts.Any(text =>
                        text.AttachmentPosition == chunk.AttachmentPosition
                        && text.Kind == AttachmentTextKind.ImageDescription) == depicted)
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
                        .Where(chunk => email.AttachmentTexts.Any(text =>
                            text.AttachmentPosition == chunk.AttachmentPosition
                            && text.Kind == AttachmentTextKind.ImageDescription) == depicted)
                        .SelectMany(chunk => chunk.Embeddings)
                        .Where(vector => vector.EmbeddingProfileId == profileId)
                        .Min(vector => vector.Embedding.L2Distance(target)))),
            _ => throw new ArgumentOutOfRangeException(
                nameof(distanceMetric),
                distanceMetric,
                "The distance metric has no pgvector operator."),
        };
}
