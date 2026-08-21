// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;

namespace MailFathom.Application.Emails.Search;

/// <summary>Ranks mail by meaning, or reports why this search cannot be answered that way.</summary>
/// <remarks>
/// <para>
/// Everything that has to hold before a distance means anything is established here and in one place: that this
/// instance has activated a profile, that it has an embedding provider at all, that the provider it would call reaches
/// the same vector space that profile's stored vectors were written under, and that the provider is currently
/// answering. A caller receives either a ranking or nothing, and never a ranking computed against a space the stored
/// vectors do not belong to.
/// </para>
/// <para>
/// None of those is a failure. An instance with no active profile serves lexical search and is a supported deployment;
/// an instance whose provider is briefly unreachable serves lexical search for the length of the outage. Raising would
/// turn a mailbox search into an error because an external service was busy, which is a worse answer than the results
/// the local index can already give. What every caller does receive is the
/// <see cref="SemanticSearchCapability" /> separating those cases, so a degraded instance is legible rather than merely
/// quieter.
/// </para>
/// <para>
/// A provider known to be failing is not called again by every search that arrives while it refuses. The health state
/// is what the embedding workers' own calls established, so consulting it costs nothing and spends nothing, and a
/// refused credential stops buying one rejected request per query. What it is not is a latch: after
/// <see cref="ProviderRecheckInterval" /> without a fresh observation, one search is allowed through to find out, so
/// recovery is automatic and needs no restart even on an instance whose mail is fully embedded and whose workers are
/// therefore calling nobody.
/// </para>
/// <para>
/// The query text is the most revealing value any search carries, so nothing here records it: no failure this type
/// raises repeats it, and the vector it produces is held for the length of one call and published to nobody.
/// </para>
/// </remarks>
public sealed class SemanticEmailSearch
{
    /// <summary>How long a recorded failure keeps searches from calling the provider before one is let through again.</summary>
    /// <remarks>
    /// <para>
    /// A window rather than a latch, because nothing else is guaranteed to call the provider. The embedding workers
    /// establish the state as a by-product of work they had to do anyway, so an instance whose mail is fully embedded
    /// and whose mailbox is quiet makes no provider call at all — and a search that trusted the recorded state
    /// unconditionally would stay lexical for as long as that lasted, however long ago the credential was repaired.
    /// </para>
    /// <para>
    /// One minute is what makes both halves true at once. A refusing provider is asked at most once a minute by the
    /// whole read path however much traffic arrives, which is far below what the resilience budget alone would let
    /// through, and a repaired one is picked up inside a minute, which nobody watching a search notices. It is a
    /// constant rather than a setting for the reason the fusion depth is: an operator has nothing to gain from tuning a
    /// number whose whole range sits between "immediately" and "within a minute".
    /// </para>
    /// </remarks>
    private static readonly TimeSpan ProviderRecheckInterval = TimeSpan.FromMinutes(1);

    private readonly IActiveEmbeddingProfileReader profileReader;
    private readonly IEmailVectorSearchIndexReader vectorSearchIndexReader;
    private readonly IAiProviderHealthReader providerHealthReader;
    private readonly TimeProvider timeProvider;
    private readonly ITextEmbeddingGenerator? textEmbeddingGenerator;

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
        ArgumentNullException.ThrowIfNull(profileReader);
        ArgumentNullException.ThrowIfNull(vectorSearchIndexReader);
        ArgumentNullException.ThrowIfNull(providerHealthReader);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.profileReader = profileReader;
        this.vectorSearchIndexReader = vectorSearchIndexReader;
        this.providerHealthReader = providerHealthReader;
        this.timeProvider = timeProvider;
        this.textEmbeddingGenerator = textEmbeddingGenerator;
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
    public async Task<SemanticSearchCapability> ReadCapabilityAsync(CancellationToken cancellationToken) =>
        (await this.ResolveCapabilityAsync(cancellationToken)).Capability;

    /// <summary>Ranks the eligible mail by how near it sits to what the query means.</summary>
    /// <param name="selection">Which emails are eligible before any distance is measured.</param>
    /// <param name="queryText">The validated free text to place in the vector space.</param>
    /// <param name="limit">The greatest number of candidates to return, at least one.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What semantic retrieval could do for this query, and the ranking when it could produce one.</returns>
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

        var (capability, profile, generator) = await this.ResolveCapabilityAsync(cancellationToken);
        if (profile is null || generator is null)
        {
            return new SemanticEmailSearchOutcome(capability, Candidates: null);
        }

        var queryVector = await PlaceQueryAsync(generator, queryText, cancellationToken);
        if (queryVector is null)
        {
            // This call is the freshest evidence there is, so it is what the query reports against rather than the state
            // it was admitted under — whether that was a provider believed to be serving or one whose last failure had
            // aged past the recheck interval.
            return new SemanticEmailSearchOutcome(SemanticSearchCapability.Degraded, Candidates: null);
        }

        var candidates = await this.vectorSearchIndexReader.ReadNearestCandidatesAsync(
            selection,
            profile,
            queryVector,
            limit,
            cancellationToken);

        return new SemanticEmailSearchOutcome(SemanticSearchCapability.Available, candidates);
    }

    /// <summary>Decides what semantic retrieval can do, and hands back what a ranking would need to run.</summary>
    /// <remarks>
    /// The profile is read first because it is what separates "this instance does not embed" from "this instance embeds
    /// and currently cannot". Every other condition is about an instance that has already activated one, so reporting
    /// any of them without the profile in hand would call a supported lexical-only deployment degraded. The profile and
    /// the generator come back beside the capability so that a caller which is about to rank neither reads the profile
    /// twice nor has to assert that a capability implies one.
    /// </remarks>
    private async Task<SemanticSearchGate> ResolveCapabilityAsync(CancellationToken cancellationToken)
    {
        var profile = await this.profileReader.FindActiveProfileAsync(cancellationToken);
        if (profile is null)
        {
            return new SemanticSearchGate(SemanticSearchCapability.Inactive, null, null);
        }

        if (this.textEmbeddingGenerator is not { } generator)
        {
            // Vectors exist and nothing can place a query beside them, because the endpoint chain the profile was
            // activated from is no longer declared. An operator has to restore it or activate what is declared now.
            return new SemanticSearchGate(SemanticSearchCapability.Degraded, null, null);
        }

        // Compared through the fingerprint rather than property by property, for the reason generation compares it that
        // way: the digest is what the profile table is unique on, so agreeing here is the same statement as resolving to
        // this row at activation. A generator that disagreed would place the query in a second space, and every distance
        // measured against the stored vectors would be a number with no meaning rather than an error.
        if (EmbeddingProfileFingerprint.Compute(generator.Identity)
            != EmbeddingProfileFingerprint.Compute(profile.Identity))
        {
            return new SemanticSearchGate(SemanticSearchCapability.Degraded, null, null);
        }

        var health = this.providerHealthReader.Read(AiProviderRole.Embedding);
        var capability = health.State is AiProviderHealthState.Unavailable or AiProviderHealthState.Misconfigured
            ? SemanticSearchCapability.Degraded
            : SemanticSearchCapability.Available;

        // The capability reports the recorded state and admission decides separately, because the two are different
        // questions once the recheck window is in play: a provider that failed an hour ago is still degraded as far as
        // any reader is concerned, and is still due the one call that would establish otherwise.
        return this.IsRefusingRecently(health)
            ? new SemanticSearchGate(capability, null, null)
            : new SemanticSearchGate(capability, profile, generator);
    }

    /// <summary>Reports whether the provider refused recently enough that asking again now would only buy the same answer.</summary>
    /// <remarks>
    /// Unobserved is never recent: a freshly started instance has failed at nothing, and the first call to arrive is
    /// what establishes the state every later one reads. An observation with no moment attached is treated as old
    /// rather than as fresh, so an unstamped state can never be what withholds retrieval indefinitely.
    /// </remarks>
    private bool IsRefusingRecently(AiProviderHealth health) =>
        health.State is AiProviderHealthState.Unavailable or AiProviderHealthState.Misconfigured
        && health.ObservedAt is { } observedAt
        && this.timeProvider.GetUtcNow() - observedAt < ProviderRecheckInterval;

    /// <summary>Places the query text in the active profile's space, or reports that the provider did not.</summary>
    /// <remarks>
    /// A provider failure ends semantic retrieval for this one call and nothing else: the adapter behind the port has
    /// already spent its own bounded attempts, classified what went wrong, and recorded it, and a search has no better
    /// answer to add than falling back to the index it can read locally. A generator that answered with no vector at all
    /// is treated the same way rather than indexed into, because a search must not fail on a provider's malformed
    /// success.
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

    /// <summary>What the gate decided, and the two collaborators a ranking needs when it let one through.</summary>
    /// <remarks>
    /// Both are non-null exactly when this query may call the provider, which is not the same as the capability being
    /// <see cref="SemanticSearchCapability.Available" />: a degraded provider whose last observation has aged past the
    /// recheck interval is admitted precisely so that the observation can be renewed. They are carried rather than
    /// re-resolved so a caller reads them instead of restating the condition that produced them.
    /// </remarks>
    private sealed record SemanticSearchGate(
        SemanticSearchCapability Capability,
        RegisteredEmbeddingProfile? Profile,
        ITextEmbeddingGenerator? Generator);
}
