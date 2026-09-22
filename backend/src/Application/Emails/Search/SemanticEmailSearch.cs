// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.Emails.Search;

/// <summary>Ranks mail by meaning, or reports why this search cannot be answered that way.</summary>
/// <remarks>
/// <para>
/// What has to hold before a distance means anything — an active profile, a provider, the same vector space, and a
/// provider that is answering — is established by <see cref="ActiveEmbeddingSpace" />, which the Agent's history search
/// places its query through as well. A caller receives either a ranking or nothing, and never a ranking computed against
/// a space the stored vectors do not belong to.
/// </para>
/// <para>
/// None of the reasons for nothing is a failure: an instance with no active profile, or whose provider is briefly
/// unreachable, serves lexical search. Raising would turn a mailbox search into an error because an external service
/// was busy, which is a worse answer than the results the local index can already give.
/// </para>
/// </remarks>
public sealed class SemanticEmailSearch
{
    private readonly ActiveEmbeddingSpace space;
    private readonly IEmailVectorSearchIndexReader vectorSearchIndexReader;

    /// <summary>Initializes semantic retrieval over whatever this deployment configured.</summary>
    /// <param name="profileReader">Answers which vector space this instance retrieves under, if any.</param>
    /// <param name="vectorSearchIndexReader">Ranks the eligible mail by distance from a point in that space.</param>
    /// <param name="providerHealthReader">Answers what the last call to the embedding provider established about it.</param>
    /// <param name="timeProvider">Measures how long ago that was, which is what keeps a recorded failure from latching.</param>
    /// <param name="textEmbeddingGenerator">Places a query in that space, or <see langword="null" /> when this deployment configured no embedding provider.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profileReader" />, <paramref name="vectorSearchIndexReader" />, <paramref name="providerHealthReader" />, or <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The generator is the one optional dependency in the read path, and its absence is the deployment decision rather
    /// than a missing registration: the composition root registers an embedding adapter only for an instance that
    /// declared an endpoint chain, so resolving one here would make lexical-only deployments fail to start a search
    /// rather than serve it.
    /// </remarks>
    public SemanticEmailSearch(
        IActiveEmbeddingProfileReader profileReader,
        IEmailVectorSearchIndexReader vectorSearchIndexReader,
        IAiProviderHealthReader providerHealthReader,
        TimeProvider timeProvider,
        ITextEmbeddingGenerator? textEmbeddingGenerator)
    {
        ArgumentNullException.ThrowIfNull(vectorSearchIndexReader);

        this.space = new ActiveEmbeddingSpace(profileReader, providerHealthReader, timeProvider, textEmbeddingGenerator);
        this.vectorSearchIndexReader = vectorSearchIndexReader;
    }

    /// <summary>Reads what semantic retrieval can do for this instance, without calling a provider.</summary>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The capability, which is what a search reports when it returns before ranking anything.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels or the host is shutting down.</exception>
    /// <remarks>
    /// One committed read of local state and one read of process-local health. It is deliberately cheap and deliberately
    /// free: a capability that had to spend a provider call to be reported would put an operator's money behind every
    /// question about whether their instance is working.
    /// </remarks>
    public Task<SemanticSearchCapability> ReadCapabilityAsync(CancellationToken cancellationToken) =>
        this.space.ReadCapabilityAsync(cancellationToken);

    /// <summary>Ranks the eligible mail by how near it sits to what the query means.</summary>
    /// <param name="selection">Which emails are eligible before any distance is measured.</param>
    /// <param name="queryText">The validated free text to place in the vector space.</param>
    /// <param name="limit">The greatest number of candidates to return, at least one.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What semantic retrieval could do for this query, and the two orderings when it could produce them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="selection" /> or <paramref name="queryText" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is below one.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels or the host is shutting down, which is neither a provider failure nor an absence of semantic retrieval.</exception>
    public async Task<SemanticEmailSearchOutcome> FindNearestCandidatesAsync(
        MailboxEmailSelection selection,
        EmailSearchQueryText queryText,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        var placement = await this.space.PlaceAsync([queryText.Value], cancellationToken);
        if (placement is not { Profile: { } profile, Vectors: [var queryVector] })
        {
            return new SemanticEmailSearchOutcome(placement.Capability, Rankings: null);
        }

        var rankings = await this.vectorSearchIndexReader.ReadNearestCandidatesAsync(
            selection,
            profile,
            queryVector,
            limit,
            cancellationToken);

        return new SemanticEmailSearchOutcome(SemanticSearchCapability.Available, rankings);
    }
}
