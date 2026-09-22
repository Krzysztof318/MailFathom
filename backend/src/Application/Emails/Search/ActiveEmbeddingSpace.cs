// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Application.Emails.Embeddings;

namespace MailFathom.Application.Emails.Search;

/// <summary>Places text in the vector space this instance retrieves under, or reports why it cannot right now.</summary>
/// <remarks>
/// <para>
/// Everything that has to hold before a distance means anything is established here and in one place: that this
/// instance has activated a profile, that it has an embedding provider at all, that the provider it would call reaches
/// the same vector space that profile's stored vectors were written under, and that the provider is currently
/// answering. A caller receives either vectors in that space or none, and never a vector placed in a space the stored
/// vectors do not belong to — which is what lets a search over mail and a search over the Agent's history measure
/// against what was stored rather than against a second geometry nobody wrote.
/// </para>
/// <para>
/// None of those is a failure. An instance with no active profile serves lexical search and is a supported deployment;
/// an instance whose provider is briefly unreachable serves lexical search for the length of the outage. What every
/// caller does receive is the <see cref="SemanticSearchCapability" /> separating those cases, so a degraded instance is
/// legible rather than merely quieter.
/// </para>
/// <para>
/// A provider known to be failing is not called again by every caller that arrives while it refuses. The health state
/// is what the embedding workers' own calls established, so consulting it costs nothing and spends nothing, and a
/// refused credential stops buying one rejected request per query. What it is not is a latch: after
/// <see cref="ProviderRecheckInterval" /> without a fresh observation, one call is allowed through to find out, so
/// recovery is automatic and needs no restart even on an instance whose mail is fully embedded and whose workers are
/// therefore calling nobody.
/// </para>
/// <para>
/// What is placed is the most revealing value either caller carries — a query, or somebody's own words to the Agent —
/// so nothing here records it: no failure this type raises repeats it, and the vectors it produces are returned to the
/// caller and published to nobody.
/// </para>
/// </remarks>
public sealed class ActiveEmbeddingSpace
{
    /// <summary>How long a recorded failure keeps callers from calling the provider before one is let through again.</summary>
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
    private readonly IAiProviderHealthReader providerHealthReader;
    private readonly TimeProvider timeProvider;
    private readonly ITextEmbeddingGenerator? textEmbeddingGenerator;

    /// <summary>Initializes placement over whatever this deployment configured.</summary>
    /// <param name="profileReader">Answers which vector space this instance retrieves under, if any.</param>
    /// <param name="providerHealthReader">Answers what the last call to the embedding provider established about it.</param>
    /// <param name="timeProvider">Measures how long ago that was, which is what keeps a recorded failure from latching.</param>
    /// <param name="textEmbeddingGenerator">Places text in that space, or <see langword="null" /> when this deployment configured no embedding provider.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profileReader" />, <paramref name="providerHealthReader" />, or <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The generator is the one optional dependency, and its absence is the deployment decision rather than a missing
    /// registration: the composition root registers an embedding adapter only for an instance that declared an endpoint
    /// chain, so resolving one here would make lexical-only deployments fail to start a search rather than serve it.
    /// </remarks>
    public ActiveEmbeddingSpace(
        IActiveEmbeddingProfileReader profileReader,
        IAiProviderHealthReader providerHealthReader,
        TimeProvider timeProvider,
        ITextEmbeddingGenerator? textEmbeddingGenerator)
    {
        ArgumentNullException.ThrowIfNull(profileReader);
        ArgumentNullException.ThrowIfNull(providerHealthReader);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.profileReader = profileReader;
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
        (await this.ResolveAsync(cancellationToken)).Capability;

    /// <summary>Places each text in the active space, all of them or none.</summary>
    /// <param name="texts">The texts to place, none of them blank.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>What semantic retrieval could do, and one vector per text in order when it could produce them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="texts" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="texts" /> is empty.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels or the host is shutting down, which is neither a provider failure nor an absence of semantic retrieval.</exception>
    /// <remarks>
    /// The texts are sent in as many calls as the generator's own bound requires. A call that fails leaves the whole
    /// placement empty rather than partly filled, because a caller holding vectors for some of what it asked about would
    /// have to reconcile them one by one, and every caller here treats a missing vector as the lexical answer anyway.
    /// </remarks>
    public async Task<EmbeddingPlacement> PlaceAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentOutOfRangeException.ThrowIfZero(texts.Count, nameof(texts));

        var (capability, profile, generator) = await this.ResolveAsync(cancellationToken);
        if (profile is null || generator is null)
        {
            return new EmbeddingPlacement(capability, Profile: null, Vectors: null);
        }

        var vectors = await GenerateAsync(generator, texts, cancellationToken);

        // This call is the freshest evidence there is, so it is what the placement reports against rather than the state
        // it was admitted under — whether that was a provider believed to be serving or one whose last failure had aged
        // past the recheck interval.
        return vectors is null
            ? new EmbeddingPlacement(SemanticSearchCapability.Degraded, Profile: null, Vectors: null)
            : new EmbeddingPlacement(SemanticSearchCapability.Available, profile, vectors);
    }

    private static async Task<IReadOnlyList<EmbeddingVector>?> GenerateAsync(
        ITextEmbeddingGenerator generator,
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken)
    {
        List<EmbeddingVector> vectors = new(texts.Count);

        try
        {
            foreach (var call in texts.Chunk(generator.MaximumPassagesPerCall))
            {
                var placed = await generator.GenerateAsync(call, cancellationToken);
                if (placed.Count != call.Length)
                {
                    return null;
                }

                vectors.AddRange(placed);
            }
        }
        catch (EmbeddingGenerationFailedException)
        {
            return null;
        }

        return vectors;
    }

    private async Task<Gate> ResolveAsync(CancellationToken cancellationToken)
    {
        var profile = await this.profileReader.FindActiveProfileAsync(cancellationToken);
        if (profile is null)
        {
            return new Gate(SemanticSearchCapability.Inactive, null, null);
        }

        if (this.textEmbeddingGenerator is not { } generator)
        {
            // Vectors exist and nothing can place text beside them, because the endpoint chain the profile was activated
            // from is no longer declared. An operator has to restore it or activate what is declared now.
            return new Gate(SemanticSearchCapability.Degraded, null, null);
        }

        // Compared through the fingerprint rather than property by property, for the reason generation compares it that
        // way: the digest is what the profile table is unique on, so agreeing here is the same statement as resolving to
        // this row at activation. A generator that disagreed would place text in a second space, and every distance
        // measured against the stored vectors would be a number with no meaning rather than an error.
        if (EmbeddingProfileFingerprint.Compute(generator.Identity)
            != EmbeddingProfileFingerprint.Compute(profile.Identity))
        {
            return new Gate(SemanticSearchCapability.Degraded, null, null);
        }

        var health = this.providerHealthReader.Read(AiProviderRole.Embedding);
        var capability = health.State is AiProviderHealthState.Unavailable or AiProviderHealthState.Misconfigured
            ? SemanticSearchCapability.Degraded
            : SemanticSearchCapability.Available;

        // The capability reports the recorded state and admission decides separately, because the two are different
        // questions once the recheck window is in play: a provider that failed an hour ago is still degraded as far as
        // any reader is concerned, and is still due the one call that would establish otherwise.
        return this.IsRefusingRecently(health)
            ? new Gate(capability, null, null)
            : new Gate(capability, profile, generator);
    }

    private bool IsRefusingRecently(AiProviderHealth health) =>
        health.State is AiProviderHealthState.Unavailable or AiProviderHealthState.Misconfigured
        && health.ObservedAt is { } observedAt
        && this.timeProvider.GetUtcNow() - observedAt < ProviderRecheckInterval;

    /// <summary>What admission decided: the capability to report, and what to place with when a call may be made.</summary>
    private sealed record Gate(
        SemanticSearchCapability Capability,
        RegisteredEmbeddingProfile? Profile,
        ITextEmbeddingGenerator? Generator);
}
