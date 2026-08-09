// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Indexing;
using MailFathom.Application.Persistence;

namespace MailFathom.Application.Emails.Embeddings.Generations;

/// <summary>Stops a reindex, leaving the generation that was serving exactly where it was.</summary>
/// <remarks>
/// The operation exists because a reindex is a decision an operator can regret while it is still running — the wrong
/// model, a bill growing faster than expected — and the honest answer to that is to stop, not to wait for the switch and
/// then pay for a second one. Nothing about retrieval changes: the generation being abandoned was never read.
/// </remarks>
public sealed class EmbeddingReindexCancellation
{
    private readonly IEmbeddingGenerationStore generationStore;
    private readonly IEmbeddingProfileVectorIndex vectorIndex;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly EmbeddingBackfillSchedule backfillSchedule;

    /// <summary>Initializes a new cancellation.</summary>
    /// <param name="generationStore">Reads which generation is being built and abandons it.</param>
    /// <param name="vectorIndex">Removes the approximate index the abandoned generation would have been searched through.</param>
    /// <param name="concurrencyRetryPolicy">Commits the transition, retrying a conflict with a competing writer.</param>
    /// <param name="backfillSchedule">Brings the next upkeep pass forward, which is the pass that removes what the abandoned generation holds.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmbeddingReindexCancellation(
        IEmbeddingGenerationStore generationStore,
        IEmbeddingProfileVectorIndex vectorIndex,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        EmbeddingBackfillSchedule backfillSchedule)
    {
        ArgumentNullException.ThrowIfNull(generationStore);
        ArgumentNullException.ThrowIfNull(vectorIndex);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(backfillSchedule);

        this.generationStore = generationStore;
        this.vectorIndex = vectorIndex;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.backfillSchedule = backfillSchedule;
    }

    /// <summary>Abandons the generation being built, if one is.</summary>
    /// <param name="cancellationToken">Cancels the read and the transition.</param>
    /// <returns>Whether a reindex was abandoned.</returns>
    /// <exception cref="EmbeddingVectorIndexFailedException">
    /// Thrown when the abandonment committed but the database refused to remove the generation's approximate index. The
    /// reindex is stopped either way; the index goes on occupying storage for a generation nothing reads until the
    /// removal of its vectors reaches the end and drops it.
    /// </exception>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when a competing writer wins a race the bounded retries could not resolve.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels.</exception>
    public async Task<EmbeddingReindexCancellationOutcome> CancelAsync(CancellationToken cancellationToken)
    {
        var generations = await this.generationStore.ReadGenerationsAsync(cancellationToken);
        if (generations.Building is not { } building)
        {
            return EmbeddingReindexCancellationOutcome.NothingBuilding;
        }

        var abandoned = await this.concurrencyRetryPolicy.CommitAsync(
            (persistenceSession, attemptCancellationToken) => this.generationStore.AbandonAsync(
                persistenceSession,
                building.Id,
                attemptCancellationToken),
            cancellationToken);

        // A reindex that completed between the read and the write took its generation into service, and abandoning that
        // is not what this command means. Nothing was changed, so nothing — least of all the index searches are now
        // answered through — is removed.
        if (!abandoned)
        {
            return EmbeddingReindexCancellationOutcome.NothingBuilding;
        }

        // Dropped before the vectors rather than after them, because every batched delete would otherwise maintain an
        // index nothing will ever read. The removal drops it again when it empties the generation, which is what covers
        // a process that stopped between these two steps.
        await this.vectorIndex.RemoveAsync(building.Id, cancellationToken);

        // What this leaves behind is a generation nothing reads whose partial vectors are personal data with no purpose
        // left, and the pass that removes them is the one an idle interval has just put as much as a quarter of an hour
        // away. The worker cannot observe the row this changed, so the removal is asked for here.
        this.backfillSchedule.BringForward();

        return EmbeddingReindexCancellationOutcome.Cancelled;
    }
}
