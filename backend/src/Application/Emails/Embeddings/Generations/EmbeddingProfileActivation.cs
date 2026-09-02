// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Backfill;
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
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly EmbeddingBackfillSchedule backfillSchedule;

    /// <summary>Initializes a new activation.</summary>
    /// <param name="generationStore">Reads which generations exist and registers the one being started.</param>
    /// <param name="concurrencyRetryPolicy">Commits the registration, retrying a conflict with a competing writer.</param>
    /// <param name="backfillSchedule">Brings the next upkeep pass forward once there is a generation for it to walk towards.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmbeddingProfileActivation(
        IEmbeddingGenerationStore generationStore,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        EmbeddingBackfillSchedule backfillSchedule)
    {
        ArgumentNullException.ThrowIfNull(generationStore);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(backfillSchedule);

        this.generationStore = generationStore;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.backfillSchedule = backfillSchedule;
    }

    /// <summary>Registers the declared geometry as the generation to build, unless one of it is already there.</summary>
    /// <param name="declared">The geometry configuration declares, which the fingerprint resolves a profile through.</param>
    /// <param name="cancellationToken">Cancels the reads and the registration.</param>
    /// <returns>What the activation did, and which generation it did it to.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="declared" /> is <see langword="null" />.</exception>
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
                ? this.ResumeBuilding(building)
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

        this.AskForAPassNow();

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
            ? this.ResumeBuilding(building)
            : new EmbeddingProfileActivationResult(
                EmbeddingProfileActivationOutcome.DifferentReindexRunning,
                building.Id);
    }

    /// <summary>Reports the reindex already under way, and asks for the pass that carries it forward.</summary>
    /// <remarks>
    /// Repeating the command on a generation already being built is how an operator asks for a stalled reindex to be
    /// picked up again, so it brings a pass forward rather than answering and doing nothing.
    /// </remarks>
    private EmbeddingProfileActivationResult ResumeBuilding(RegisteredEmbeddingProfile building)
    {
        this.AskForAPassNow();

        return new EmbeddingProfileActivationResult(
            EmbeddingProfileActivationOutcome.AlreadyBuilding,
            building.Id);
    }

    /// <summary>Brings the next upkeep pass forward, because this call is what made one worth running.</summary>
    /// <remarks>
    /// The row committed above is the whole of the signal, and a sleeping worker cannot observe it: a pass is paced by
    /// what the previous one found, and every pass before an activation found no generation to walk towards and took
    /// the long idle interval. Without this, the first passages of a reindex go out whenever that unrelated interval
    /// happens to expire rather than because an operator asked for them. The two refusals ask for nothing — an
    /// activation of what is already serving changed no row, and one refused for a different running reindex is not the
    /// activation that happened.
    /// </remarks>
    private void AskForAPassNow() => this.backfillSchedule.BringForward();

    private static EmbeddingProfileFingerprint Fingerprints(RegisteredEmbeddingProfile profile) =>
        EmbeddingProfileFingerprint.Compute(profile.Identity);
}
