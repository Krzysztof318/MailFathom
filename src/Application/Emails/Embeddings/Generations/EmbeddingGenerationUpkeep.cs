// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Indexing;
using MailFathom.Application.Persistence;

namespace MailFathom.Application.Emails.Embeddings.Generations;

/// <summary>One bounded pass of everything this instance's vector generations need doing to them.</summary>
/// <remarks>
/// <para>
/// Three steps in one pass, in the order they depend on each other: fill whichever generation is behind, complete a
/// generation that has stopped being behind, and remove the vectors of one that nothing reads any more. They ride one
/// loop rather than three workers because they are one pipeline — the third step exists only because the second one
/// happened — and because each is bounded, so a pass that finds all three outstanding still ends.
/// </para>
/// <para>
/// The completion is what makes a model change an operation without an outage. A new generation is filled while the old
/// one goes on answering searches, and the moment it is complete it takes over in one recorded transition; the old
/// generation's vectors then go, in batches, because they are personal data whose purpose ended there. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>.
/// </para>
/// </remarks>
public sealed class EmbeddingGenerationUpkeep
{
    /// <summary>How many superseded vectors one pass removes before it ends.</summary>
    /// <remarks>
    /// A constant rather than a setting, because it bounds a local delete rather than describing a deployment: nothing
    /// here reaches a provider or costs an operator money, and what paces the removal of a large generation is the
    /// interval between passes, which is configured. The bound exists so one statement cannot hold a transaction, a lock
    /// set, and a write-ahead burst open for as long as a mailbox's worth of vectors takes to delete.
    /// </remarks>
    private const int SupersededVectorRemovalBatchSize = 10_000;

    private readonly IEmbeddingGenerationStore generationStore;
    private readonly IStoredEmailEmbeddingBackfillStore backfillStore;
    private readonly StoredEmailEmbeddingBackfill backfill;
    private readonly IEmbeddingProfileVectorIndex vectorIndex;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;

    /// <summary>Initializes a new upkeep pass.</summary>
    /// <param name="generationStore">Reads the generations and writes the transitions between them.</param>
    /// <param name="backfillStore">Answers how much of the target generation is still outstanding.</param>
    /// <param name="backfill">Walks the stored mail towards the target generation.</param>
    /// <param name="vectorIndex">Removes the approximate index of a generation nothing reads any more.</param>
    /// <param name="concurrencyRetryPolicy">Commits a transition or a removal batch, retrying a conflict with a competing writer.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public EmbeddingGenerationUpkeep(
        IEmbeddingGenerationStore generationStore,
        IStoredEmailEmbeddingBackfillStore backfillStore,
        StoredEmailEmbeddingBackfill backfill,
        IEmbeddingProfileVectorIndex vectorIndex,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy)
    {
        ArgumentNullException.ThrowIfNull(generationStore);
        ArgumentNullException.ThrowIfNull(backfillStore);
        ArgumentNullException.ThrowIfNull(backfill);
        ArgumentNullException.ThrowIfNull(vectorIndex);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);

        this.generationStore = generationStore;
        this.backfillStore = backfillStore;
        this.backfill = backfill;
        this.vectorIndex = vectorIndex;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
    }

    /// <summary>Runs one bounded pass.</summary>
    /// <param name="cancellationToken">Cancels the pass between its steps and inside each of them.</param>
    /// <returns>What the pass produced, and whether running again shortly would reach more.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when a competing writer wins a race the bounded retries could not resolve.</exception>
    /// <exception cref="EmbeddingVectorIndexFailedException">Thrown when the database refused to remove a superseded generation's index.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels. Everything already committed stays durable.</exception>
    public async Task<EmbeddingGenerationUpkeepResult> RunAsync(CancellationToken cancellationToken)
    {
        var generations = await this.generationStore.ReadGenerationsAsync(cancellationToken);

        // The walk is what an instance with no generation has nothing to do; the removal is not. A reindex cancelled
        // on an instance that had never served one leaves a superseded generation and no sibling, and its partial
        // vectors are personal data that has to go whether or not anything is being embedded now.
        var sweep = generations.Target is { } target
            ? await this.backfill.RunAsync(target, cancellationToken)
            : StoredEmailEmbeddingBackfillResult.NoActiveProfile;

        var transition = await this.CompleteBuiltGenerationAsync(generations, sweep, cancellationToken);
        var removedVectorCount = await this.RemoveSupersededVectorsAsync(cancellationToken);

        return new EmbeddingGenerationUpkeepResult(sweep, transition, removedVectorCount);
    }

    /// <summary>Switches to the generation being built once nothing is outstanding for it.</summary>
    /// <remarks>
    /// A completed sweep is not the same statement as a complete generation, which is why the count is asked for rather
    /// than inferred: the walk ends its sweep when nothing is outstanding <em>in front of</em> its position, and a
    /// message a provider refused earlier stays outstanding behind it. Asking is one indexed count, and only on the pass
    /// that finished a sweep towards a generation that is being built.
    /// </remarks>
    private async Task<EmbeddingGenerationTransition> CompleteBuiltGenerationAsync(
        EmbeddingGenerations generations,
        StoredEmailEmbeddingBackfillResult sweep,
        CancellationToken cancellationToken)
    {
        if (generations.Building is not { } building
            || sweep.Outcome != StoredEmailEmbeddingBackfillOutcome.SweepCompleted)
        {
            return EmbeddingGenerationTransition.None;
        }

        var outstandingEmailCount = await this.backfillStore.CountEmailsAwaitingEmbeddingAsync(
            building.Id,
            cancellationToken);
        if (outstandingEmailCount > 0)
        {
            return EmbeddingGenerationTransition.None;
        }

        var switched = await this.concurrencyRetryPolicy.CommitAsync(
            (persistenceSession, attemptCancellationToken) => this.generationStore.SwitchToAsync(
                persistenceSession,
                building.Id,
                attemptCancellationToken),
            cancellationToken);

        // A generation somebody cancelled while this pass was finishing it is not switched to, and the store is what
        // says so: the count above was taken against a row that has since stopped being built.
        if (!switched)
        {
            return EmbeddingGenerationTransition.None;
        }

        // Dropped after the switch rather than with it, and outside its transaction: an index is not something the
        // switch can roll back, and every batched delete below would otherwise maintain an index nothing will read.
        if (generations.Serving is { } superseded)
        {
            await this.vectorIndex.RemoveAsync(superseded.Id, cancellationToken);
        }

        return EmbeddingGenerationTransition.Switched;
    }

    /// <summary>Removes one bounded batch of a superseded generation's vectors.</summary>
    /// <remarks>
    /// One generation per pass, and the index goes when the last batch of it does. A batch that came back short is the
    /// generation being empty, which is also the moment an index left behind by a process that stopped mid-removal is
    /// cleared — the call is idempotent, so paying it once per emptied generation costs nothing.
    /// </remarks>
    private async Task<int> RemoveSupersededVectorsAsync(CancellationToken cancellationToken)
    {
        if (await this.generationStore.FindSupersededProfileHoldingVectorsAsync(cancellationToken)
            is not { } supersededProfileId)
        {
            return 0;
        }

        var removedVectorCount = await this.concurrencyRetryPolicy.CommitAsync(
            (persistenceSession, attemptCancellationToken) => this.generationStore.RemoveVectorsAsync(
                persistenceSession,
                supersededProfileId,
                SupersededVectorRemovalBatchSize,
                attemptCancellationToken),
            cancellationToken);

        if (removedVectorCount < SupersededVectorRemovalBatchSize)
        {
            await this.vectorIndex.RemoveAsync(supersededProfileId, cancellationToken);
        }

        return removedVectorCount;
    }
}
