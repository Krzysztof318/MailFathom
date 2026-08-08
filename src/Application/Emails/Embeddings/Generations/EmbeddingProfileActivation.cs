// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Indexing;
using MailFathom.Application.Persistence;

namespace MailFathom.Application.Emails.Embeddings.Generations;

/// <summary>Turns a declared geometry into the generation this instance is building towards.</summary>
/// <remarks>
/// <para>
/// This is the one writer of a profile row, and the only thing in MailFathom that starts spending against a provider on
/// purpose. Editing configuration declares a model and costs nothing;
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// makes this separate act the one that materializes the declaration and pays for it.
/// </para>
/// <para>
/// What it never does is take semantic search away while it works. A generation begins its life being built, is never
/// read while it is there, and replaces whatever was serving only once it is complete — so an operator changing model
/// on a Tuesday is not answering for it on Wednesday. The counting and the confirmation that belong in front of the
/// spending are the command surface's, not this type's: by the time it is called, the decision has been made.
/// </para>
/// </remarks>
public sealed class EmbeddingProfileActivation
{
    private readonly IEmbeddingGenerationStore generationStore;
    private readonly IEmbeddingProfileVectorIndex vectorIndex;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;

    /// <summary>Initializes a new activation.</summary>
    /// <param name="generationStore">Reads which generations exist and registers the one being started.</param>
    /// <param name="vectorIndex">Builds the approximate index the new generation will be searched through.</param>
    /// <param name="concurrencyRetryPolicy">Commits the registration, retrying a conflict with a competing writer.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmbeddingProfileActivation(
        IEmbeddingGenerationStore generationStore,
        IEmbeddingProfileVectorIndex vectorIndex,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy)
    {
        ArgumentNullException.ThrowIfNull(generationStore);
        ArgumentNullException.ThrowIfNull(vectorIndex);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);

        this.generationStore = generationStore;
        this.vectorIndex = vectorIndex;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
    }

    /// <summary>Registers the declared geometry as the generation to build, unless one of it is already there.</summary>
    /// <param name="declared">The geometry configuration declares, which the fingerprint resolves a profile through.</param>
    /// <param name="cancellationToken">Cancels the reads and the registration.</param>
    /// <returns>What the activation did, and which generation it did it to.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declared" /> is <see langword="null" />.</exception>
    /// <exception cref="EmbeddingVectorIndexFailedException">
    /// Thrown when the registration committed but the database refused to build the generation's approximate index. The
    /// generation is registered and the reindex will run; searching it stays exact until an activation of the same
    /// declaration builds the index, which is why repeating the command is what repairs this.
    /// </exception>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race the bounded retries could not resolve and which was not another
    /// activation: losing to one of those is reported as an outcome rather than raised, because what the operator has
    /// to know is which generation is being built rather than that two commands collided.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public async Task<EmbeddingProfileActivationResult> ActivateAsync(
        EmbeddingProfileIdentity declared,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(declared);

        var generations = await this.generationStore.ReadGenerationsAsync(cancellationToken);
        var declaredFingerprint = EmbeddingProfileFingerprint.Compute(declared);

        // Compared through the fingerprint rather than property by property, because that digest is what the profile
        // table is unique on: agreeing here is the same statement as resolving to that row.
        if (generations.Serving is { } serving && Fingerprints(serving) == declaredFingerprint)
        {
            return new EmbeddingProfileActivationResult(
                EmbeddingProfileActivationOutcome.AlreadyServing,
                serving.Id);
        }

        if (generations.Building is { } building)
        {
            return Fingerprints(building) == declaredFingerprint
                ? await this.ResumeBuildingAsync(building, cancellationToken)
                : new EmbeddingProfileActivationResult(
                    EmbeddingProfileActivationOutcome.DifferentReindexRunning,
                    building.Id);
        }

        RegisteredEmbeddingProfile registered;
        try
        {
            registered = await this.concurrencyRetryPolicy.CommitAsync(
                (persistenceSession, attemptCancellationToken) => this.generationStore.RegisterBuildingAsync(
                    persistenceSession,
                    declared,
                    attemptCancellationToken),
                cancellationToken);
        }
        catch (PersistenceConcurrencyConflictException conflict)
        {
            return await this.ReportTheReindexThatWonAsync(conflict, declaredFingerprint, cancellationToken);
        }

        // Built while the generation is empty, which is the cheapest moment it can be built and the reason no migration
        // creates it: the index covers one profile and one width, neither of which exists before this call.
        await this.vectorIndex.EnsureBuiltAsync(registered, cancellationToken);

        return new EmbeddingProfileActivationResult(
            EmbeddingProfileActivationOutcome.ReindexStarted,
            registered.Id);
    }

    /// <summary>Answers a lost registration race with what the winner made true, or rethrows when nothing did.</summary>
    /// <remarks>
    /// The check above and the write are not one act, so two activations can both find nothing being built and both try
    /// to register. The database refuses the second — one generation may be built at a time — and where the two
    /// geometries differ no retry resolves it, because the losing row is the same row on every attempt. Re-reading is
    /// what turns the conflict into the answer the operator needs, and which answer that is follows from whose geometry
    /// won. A conflict with nothing being built afterwards was not this race at all, and is raised.
    /// </remarks>
    private async Task<EmbeddingProfileActivationResult> ReportTheReindexThatWonAsync(
        PersistenceConcurrencyConflictException conflict,
        EmbeddingProfileFingerprint declaredFingerprint,
        CancellationToken cancellationToken)
    {
        var generations = await this.generationStore.ReadGenerationsAsync(cancellationToken);
        if (generations.Building is not { } building)
        {
            throw conflict;
        }

        return Fingerprints(building) == declaredFingerprint
            ? await this.ResumeBuildingAsync(building, cancellationToken)
            : new EmbeddingProfileActivationResult(
                EmbeddingProfileActivationOutcome.DifferentReindexRunning,
                building.Id);
    }

    /// <summary>Reports the reindex already under way, having made sure it has the index it will be read through.</summary>
    /// <remarks>
    /// The index build is repeated rather than skipped because it is idempotent and because a failed one is exactly what
    /// brings an operator back to this command. Skipping it would leave the only repair path doing nothing.
    /// </remarks>
    private async Task<EmbeddingProfileActivationResult> ResumeBuildingAsync(
        RegisteredEmbeddingProfile building,
        CancellationToken cancellationToken)
    {
        await this.vectorIndex.EnsureBuiltAsync(building, cancellationToken);

        return new EmbeddingProfileActivationResult(
            EmbeddingProfileActivationOutcome.AlreadyBuilding,
            building.Id);
    }

    private static EmbeddingProfileFingerprint Fingerprints(RegisteredEmbeddingProfile profile) =>
        EmbeddingProfileFingerprint.Compute(profile.Identity);
}
