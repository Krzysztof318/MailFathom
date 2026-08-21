// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Persistence;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds the profile rows and enforces the invariant the partial unique index enforces in PostgreSQL.</summary>
/// <remarks>
/// Hand-written rather than substituted because what the tests are about is the state machine: a switch has to promote
/// one row and supersede another together, a second generation may never be building, and a row that stops being
/// superseded has to stop being reported for removal. A substitute answering each call from a script could not be wrong
/// about any of that. Its vectors are the ones the generator actually wrote, so removal is observed rather than
/// asserted.
/// </remarks>
internal sealed class InMemoryEmbeddingGenerationStore : IEmbeddingGenerationStore
{
    private readonly InMemoryEmailEmbeddingStore embeddingStore;
    private readonly Dictionary<EmbeddingProfileId, GenerationRow> rows = [];

    /// <summary>Initializes a store over the vectors the generator writes.</summary>
    public InMemoryEmbeddingGenerationStore(InMemoryEmailEmbeddingStore embeddingStore) =>
        this.embeddingStore = embeddingStore;

    /// <summary>Gets the batch sizes removal was asked for, in order.</summary>
    public List<int> RequestedRemovalBatchSizes { get; } = [];

    /// <summary>Gets or sets a generation abandoned the moment a switch to it is attempted.</summary>
    /// <remarks>
    /// The one way to arrange the race the switch guards against: a cancellation that lands after a pass counted the
    /// generation complete and before it committed the transition. Against a real database that ordering is settled by
    /// a row lock; here it is settled by this hook, so the guard is exercised rather than assumed.
    /// </remarks>
    public EmbeddingProfileId? AbandonWhenSwitched { get; set; }

    /// <summary>Registers a row in the state a test needs it in, the way an earlier activation would have left it.</summary>
    public RegisteredEmbeddingProfile Add(
        EmbeddingProfileIdentity identity,
        EmbeddingProfileLifecycleState lifecycleState)
    {
        var profile = new RegisteredEmbeddingProfile(
            EmbeddingProfileId.Create(Guid.CreateVersion7()),
            identity);

        this.rows[profile.Id] = new GenerationRow(profile) { LifecycleState = lifecycleState };

        return profile;
    }

    /// <summary>Reads what state one row is in, which is what a transition is asserted against.</summary>
    public EmbeddingProfileLifecycleState StateOf(EmbeddingProfileId profileId) =>
        this.rows[profileId].LifecycleState;

    /// <summary>Leaves one row where a cancelled reindex leaves it, so a later pass finds what it would find.</summary>
    public void Supersede(EmbeddingProfileId profileId) =>
        this.rows[profileId].LifecycleState = EmbeddingProfileLifecycleState.Superseded;

    /// <inheritdoc />
    public Task<EmbeddingGenerations> ReadGenerationsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new EmbeddingGenerations(
            Serving: this.SingleIn(EmbeddingProfileLifecycleState.Active),
            Building: this.SingleIn(EmbeddingProfileLifecycleState.Building)));
    }

    /// <inheritdoc />
    public Task<RegisteredEmbeddingProfile> RegisterBuildingAsync(
        IPersistenceSession session,
        EmbeddingProfileIdentity identity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var fingerprint = EmbeddingProfileFingerprint.Compute(identity);
        var registered = this.rows.Values.SingleOrDefault(
            row => EmbeddingProfileFingerprint.Compute(row.Profile.Identity) == fingerprint);

        if (registered is null)
        {
            var profile = this.Add(identity, EmbeddingProfileLifecycleState.Building);

            return Task.FromResult(profile);
        }

        registered.LifecycleState = EmbeddingProfileLifecycleState.Building;

        return Task.FromResult(registered.Profile);
    }

    /// <inheritdoc />
    public Task<bool> SwitchToAsync(
        IPersistenceSession session,
        EmbeddingProfileId built,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (this.AbandonWhenSwitched is { } cancelled)
        {
            this.rows[cancelled].LifecycleState = EmbeddingProfileLifecycleState.Superseded;
        }

        if (this.rows[built].LifecycleState != EmbeddingProfileLifecycleState.Building)
        {
            return Task.FromResult(false);
        }

        // A loop rather than a projection, because every match is a state change on the row it names.
        foreach (var row in this.rows.Values.Where(candidate =>
            candidate.LifecycleState == EmbeddingProfileLifecycleState.Active && candidate.Profile.Id != built))
        {
            row.LifecycleState = EmbeddingProfileLifecycleState.Superseded;
        }

        this.rows[built].LifecycleState = EmbeddingProfileLifecycleState.Active;

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> AbandonAsync(
        IPersistenceSession session,
        EmbeddingProfileId building,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (this.rows[building].LifecycleState != EmbeddingProfileLifecycleState.Building)
        {
            return Task.FromResult(false);
        }

        this.rows[building].LifecycleState = EmbeddingProfileLifecycleState.Superseded;

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<EmbeddingProfileId?> FindSupersededProfileHoldingVectorsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var superseded = this.rows.Values
            .Where(row => row.LifecycleState == EmbeddingProfileLifecycleState.Superseded)
            .Select(row => row.Profile.Id)
            .Where(profileId => this.embeddingStore.CountVectors(profileId) > 0)
            .Cast<EmbeddingProfileId?>()
            .FirstOrDefault();

        return Task.FromResult(superseded);
    }

    /// <inheritdoc />
    public Task<int> RemoveVectorsAsync(
        IPersistenceSession session,
        EmbeddingProfileId profileId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        cancellationToken.ThrowIfCancellationRequested();

        this.RequestedRemovalBatchSizes.Add(batchSize);

        // The state is re-checked at the delete for the reason the real store re-checks it: a generation activated
        // again between the read that chose it and this write keeps whatever vectors it still holds.
        if (this.rows[profileId].LifecycleState != EmbeddingProfileLifecycleState.Superseded)
        {
            return Task.FromResult(0);
        }

        return Task.FromResult(this.embeddingStore.RemoveVectors(profileId, batchSize));
    }

    /// <summary>Answers with the one row in a state, refusing two the way the partial unique index does.</summary>
    private RegisteredEmbeddingProfile? SingleIn(EmbeddingProfileLifecycleState lifecycleState) => this.rows.Values
        .SingleOrDefault(row => row.LifecycleState == lifecycleState)
        ?.Profile;

    /// <summary>One profile row: an identity that never moves and a lifecycle that does.</summary>
    private sealed record GenerationRow(RegisteredEmbeddingProfile Profile)
    {
        public EmbeddingProfileLifecycleState LifecycleState { get; set; }
    }
}
