// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.Emails.Search;

/// <summary>Ranks mail by meaning, or reports that this search cannot be answered that way.</summary>
/// <remarks>
/// <para>
/// Everything that has to hold before a distance means anything is established here and in one place: that this
/// deployment has an embedding provider at all, that it has activated a profile, that the provider it would call reaches
/// the same vector space that profile's stored vectors were written under, and that the call produced a point. A caller
/// receives either a ranking or nothing, and never a ranking computed against a space the stored vectors do not belong
/// to.
/// </para>
/// <para>
/// Every one of those is an ordinary state rather than a failure. An instance with no provider serves lexical search and
/// is a supported deployment; an instance whose provider is briefly unreachable serves lexical search for the length of
/// the outage. Raising would turn a mailbox search into an error because an external service was busy, which is a worse
/// answer than the results the local index can already give.
/// </para>
/// <para>
/// The query text is the most revealing value any search carries, so nothing here records it: no failure this type
/// raises repeats it, and the vector it produces is held for the length of one call and published to nobody.
/// </para>
/// </remarks>
public sealed class SemanticEmailSearch
{
    private readonly IActiveEmbeddingProfileReader profileReader;
    private readonly IEmailVectorSearchIndexReader vectorSearchIndexReader;
    private readonly ITextEmbeddingGenerator? textEmbeddingGenerator;

    /// <summary>Initializes semantic retrieval over whatever this deployment configured.</summary>
    /// <param name="profileReader">Answers which vector space this instance retrieves under, if any.</param>
    /// <param name="vectorSearchIndexReader">Ranks the eligible mail by distance from a point in that space.</param>
    /// <param name="textEmbeddingGenerator">Places a query in that space, or <see langword="null" /> when this deployment configured no embedding provider.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profileReader" /> or <paramref name="vectorSearchIndexReader" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The generator is the one optional dependency in the read path, and its absence is the deployment decision rather
    /// than a missing registration: the composition root registers an embedding adapter only for an instance that
    /// declared an endpoint chain, so resolving one here would make lexical-only deployments fail to start a search
    /// rather than serve it.
    /// </remarks>
    public SemanticEmailSearch(
        IActiveEmbeddingProfileReader profileReader,
        IEmailVectorSearchIndexReader vectorSearchIndexReader,
        ITextEmbeddingGenerator? textEmbeddingGenerator)
    {
        ArgumentNullException.ThrowIfNull(profileReader);
        ArgumentNullException.ThrowIfNull(vectorSearchIndexReader);

        this.profileReader = profileReader;
        this.vectorSearchIndexReader = vectorSearchIndexReader;
        this.textEmbeddingGenerator = textEmbeddingGenerator;
    }

    /// <summary>Ranks the eligible mail by how near it sits to what the query means.</summary>
    /// <param name="selection">Which emails are eligible before any distance is measured.</param>
    /// <param name="queryText">The validated free text to place in the vector space.</param>
    /// <param name="limit">The greatest number of candidates to return, at least one.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The ranking, nearest first, or <see langword="null" /> when this search cannot be answered semantically.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selection" /> or <paramref name="queryText" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is below one.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels or the host is shutting down, which is neither a provider failure nor an absence of semantic retrieval.</exception>
    /// <remarks>
    /// <see langword="null" /> and an empty ranking are different answers. Null says this search was not ranked
    /// semantically at all, so its results are lexical; empty says it was, and nothing eligible carries a vector yet.
    /// A caller that folded the two together would report a mailbox mid-backfill as though it had never been configured
    /// to embed.
    /// </remarks>
    public async Task<IReadOnlyList<RankedEmailCandidate>?> FindNearestCandidatesAsync(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        if (this.textEmbeddingGenerator is not { } generator)
        {
            return null;
        }

        var profile = await this.profileReader.FindActiveProfileAsync(cancellationToken);
        if (profile is null)
        {
            return null;
        }

        // Compared through the fingerprint rather than property by property, for the reason generation compares it that
        // way: the digest is what the profile table is unique on, so agreeing here is the same statement as resolving to
        // this row at activation. A generator that disagrees would place the query in a second space, and every distance
        // measured against the stored vectors would be a number with no meaning rather than an error.
        if (EmbeddingProfileFingerprint.Compute(generator.Identity)
            != EmbeddingProfileFingerprint.Compute(profile.Identity))
        {
            return null;
        }

        var queryVector = await PlaceQueryAsync(generator, queryText, cancellationToken);
        if (queryVector is null)
        {
            return null;
        }

        return await this.vectorSearchIndexReader.ReadNearestCandidatesAsync(
            selection,
            profile,
            queryVector,
            limit,
            cancellationToken);
    }

    /// <summary>Places the query text in the active profile's space, or reports that the provider did not.</summary>
    /// <remarks>
    /// A provider failure ends semantic retrieval for this one call and nothing else: the adapter behind the port has
    /// already spent its own bounded attempts and classified what went wrong, and a search has no better answer to add
    /// than falling back to the index it can read locally. A generator that answered with no vector at all is treated
    /// the same way rather than indexed into, because a search must not fail on a provider's malformed success.
    /// </remarks>
    private static async Task<EmbeddingVector?> PlaceQueryAsync(
        ITextEmbeddingGenerator generator,
        EmailSearchQueryText queryText,
        CancellationToken cancellationToken)
    {
        try
        {
            var vectors = await generator.GenerateAsync([queryText.Value], cancellationToken);

            return vectors.Count is 0 ? null : vectors[0];
        }
        catch (EmbeddingGenerationFailedException)
        {
            return null;
        }
    }
}
