// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Embeddings.Backfill;

/// <summary>Walks the mail an instance already had, giving it the passages and the vectors the live path never produced.</summary>
/// <remarks>
/// <para>
/// The work is bounded, idempotent, and restartable, and it is deliberately the same shape as the extraction backfill:
/// a run processes at most a configured number of batches and then reports whether more remain, and the position each
/// message's turn commits is what the next run resumes from. What is outstanding is decided by the absence of a vector
/// rather than remembered anywhere, so a run interrupted between two provider calls re-embeds nothing it already paid
/// for.
/// </para>
/// <para>
/// Two things are outstanding here rather than one, and the order between them is the whole point: a message stored
/// before chunking existed has extracted text and no passages, and nothing can be embedded until those passages are
/// cut. Cutting them costs a database write and no provider call, and it reads text an earlier extraction already
/// stored, so this reaches no mail server and cannot touch a remote <c>\Seen</c> flag however long it runs.
/// </para>
/// <para>
/// The walk repeats rather than finishing once. A message whose turn a provider refused keeps passages without vectors
/// and the position has already stepped past it, so when the walk reaches the end it ends the sweep instead of parking
/// there, and the next run starts again from the beginning. That is what makes the promise the live path relies on —
/// that whatever the bounded backlog turned away and whatever a failed turn did not reach is reached later — something
/// this actually keeps.
/// </para>
/// </remarks>
public sealed class StoredEmailEmbeddingBackfill
{
    private readonly IActiveEmbeddingProfileReader profileReader;
    private readonly IStoredEmailEmbeddingBackfillStore backfillStore;
    private readonly StoredEmailEmbeddingGenerator embeddingGenerator;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly StoredEmailEmbeddingBackfillOptions options;

    /// <summary>Initializes a new embedding backfill.</summary>
    /// <param name="profileReader">Answers whether this instance embeds at all, and into which vector space.</param>
    /// <param name="backfillStore">Reads what remains and writes both the passages and the position a run produced.</param>
    /// <param name="embeddingGenerator">Brings one message up to date, which is the same unit of work the live worker performs.</param>
    /// <param name="concurrencyRetryPolicy">Commits a write, retrying a conflict with a competing writer.</param>
    /// <param name="options">Bounds one run.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public StoredEmailEmbeddingBackfill(
        IActiveEmbeddingProfileReader profileReader,
        IStoredEmailEmbeddingBackfillStore backfillStore,
        StoredEmailEmbeddingGenerator embeddingGenerator,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        StoredEmailEmbeddingBackfillOptions options)
    {
        ArgumentNullException.ThrowIfNull(profileReader);
        ArgumentNullException.ThrowIfNull(backfillStore);
        ArgumentNullException.ThrowIfNull(embeddingGenerator);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(options);

        this.profileReader = profileReader;
        this.backfillStore = backfillStore;
        this.embeddingGenerator = embeddingGenerator;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.options = options;
    }

    /// <summary>Runs one bounded pass of the backfill.</summary>
    /// <param name="cancellationToken">Cancels the run between messages, between batches, and inside a message's turn.</param>
    /// <returns>What this run produced, and why it ended.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race that the bounded retries could not resolve. Passages and vectors
    /// already committed stay durable, and the next run resumes from the committed position.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels. Committed passages, vectors, and positions stay durable.</exception>
    public async Task<StoredEmailEmbeddingBackfillResult> RunAsync(CancellationToken cancellationToken)
    {
        // Read before the walk rather than per message, because the profile is what the outstanding-work query is
        // expressed against: with no active profile there is no vector space for a passage to be missing from, and the
        // question the walk asks would have no subject.
        var profile = await this.profileReader.FindActiveProfileAsync(cancellationToken);
        if (profile is null)
        {
            return Ended(StoredEmailEmbeddingBackfillOutcome.NoActiveProfile, RunProgress.Empty);
        }

        var position = await this.backfillStore.FindResumePositionAsync(cancellationToken);
        var outstandingAtSweepStart = position is null
            ? await this.backfillStore.CountEmailsAwaitingEmbeddingAsync(profile.Id, cancellationToken)
            : (int?)null;

        var progress = RunProgress.Empty;

        for (var batchNumber = 1; batchNumber <= this.options.MaxBatchesPerRun; batchNumber++)
        {
            var batch = await this.backfillStore.GetEmailsAwaitingEmbeddingAsync(
                position,
                profile.Id,
                this.options.BatchSize,
                cancellationToken);

            if (batch.Count == 0)
            {
                await this.CommitPositionAsync(position: null, cancellationToken);

                return Ended(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, progress, outstandingAtSweepStart);
            }

            foreach (var email in batch)
            {
                var turn = await this.BringUpToDateAsync(email, cancellationToken);
                progress = progress.Add(email, turn);

                // Neither of these says anything about this message, so stepping past it would skip a message for a
                // reason that has nothing to do with it. The position stays where it was and an operator settles it.
                if (turn.Outcome is StoredEmailEmbeddingOutcome.NoActiveProfile
                    or StoredEmailEmbeddingOutcome.GeneratorDisagreesWithProfile)
                {
                    return Ended(OutcomeOf(turn.Outcome), progress, outstandingAtSweepStart);
                }

                position = email.StoredEmailId;
                await this.CommitPositionAsync(position, cancellationToken);

                // The position moves past a refused message for the reason the extraction backfill moves past an
                // unreadable one: nothing may block the walk. What the turn did not reach stays without a vector, which
                // is the condition the next sweep selects on. The run itself ends, because a provider that has just
                // refused is not one to spend the rest of the batch against.
                if (turn.Outcome is StoredEmailEmbeddingOutcome.ProviderFailed)
                {
                    return Ended(
                        StoredEmailEmbeddingBackfillOutcome.ProviderFailed,
                        progress,
                        outstandingAtSweepStart,
                        turn.Failure);
                }
            }
        }

        return Ended(StoredEmailEmbeddingBackfillOutcome.BatchBudgetSpent, progress, outstandingAtSweepStart);
    }

    /// <summary>Gives one message its passages when it has none, then whatever vectors it is missing.</summary>
    /// <remarks>
    /// The two writes are separate transactions on purpose. Passages have to be committed before the generator can read
    /// them as outstanding, and the generator commits each provider call's vectors on its own so a crash leaves a whole
    /// page embedded or none of it — a single transaction around both would hold one open across every provider call
    /// this message needs.
    /// </remarks>
    private async Task<StoredEmailEmbeddingRun> BringUpToDateAsync(
        StoredEmailAwaitingEmbedding email,
        CancellationToken cancellationToken)
    {
        if (email.RequiresChunking)
        {
            await this.concurrencyRetryPolicy.CommitAsync(
                (persistenceSession, attemptCancellationToken) => this.backfillStore.DeriveChunksAsync(
                    persistenceSession,
                    email.StoredEmailId,
                    attemptCancellationToken),
                cancellationToken);
        }

        return await this.embeddingGenerator.EmbedAsync(email.StoredEmailId, cancellationToken);
    }

    /// <summary>Commits how far the sweep has come, or that it has ended.</summary>
    private Task CommitPositionAsync(StoredEmailId? position, CancellationToken cancellationToken) =>
        this.concurrencyRetryPolicy.CommitAsync(
            (persistenceSession, attemptCancellationToken) => this.backfillStore.SaveResumePositionAsync(
                persistenceSession,
                position,
                attemptCancellationToken),
            cancellationToken);

    private static StoredEmailEmbeddingBackfillResult Ended(
        StoredEmailEmbeddingBackfillOutcome outcome,
        RunProgress progress,
        int? outstandingAtSweepStart = null,
        EmbeddingGenerationFailure? failure = null) =>
        new(
            outcome,
            progress.ChunkedEmailCount,
            progress.EmbeddedEmailCount,
            progress.EmbeddedChunkCount,
            outstandingAtSweepStart,
            failure);

    /// <summary>Names the run's ending after the message turn that caused it.</summary>
    private static StoredEmailEmbeddingBackfillOutcome OutcomeOf(StoredEmailEmbeddingOutcome outcome) => outcome switch
    {
        StoredEmailEmbeddingOutcome.NoActiveProfile => StoredEmailEmbeddingBackfillOutcome.NoActiveProfile,
        _ => StoredEmailEmbeddingBackfillOutcome.GeneratorDisagreesWithProfile,
    };

    /// <summary>What a run has produced so far, in counts alone.</summary>
    /// <remarks>
    /// A message counts as embedded only when its turn reported the message whole. One that spent every call a turn is
    /// allowed keeps the passages it did get — which the passage count carries — and is deliberately not counted as a
    /// message brought up to date, because a later sweep still has to reach it.
    /// </remarks>
    private sealed record RunProgress(int ChunkedEmailCount, int EmbeddedEmailCount, int EmbeddedChunkCount)
    {
        public static RunProgress Empty { get; } = new(ChunkedEmailCount: 0, EmbeddedEmailCount: 0, EmbeddedChunkCount: 0);

        public RunProgress Add(StoredEmailAwaitingEmbedding email, StoredEmailEmbeddingRun turn) => this with
        {
            ChunkedEmailCount = this.ChunkedEmailCount + (email.RequiresChunking ? 1 : 0),
            EmbeddedEmailCount = this.EmbeddedEmailCount
                + (turn.Outcome == StoredEmailEmbeddingOutcome.Embedded ? 1 : 0),
            EmbeddedChunkCount = this.EmbeddedChunkCount + turn.EmbeddedChunkCount,
        };
    }
}
