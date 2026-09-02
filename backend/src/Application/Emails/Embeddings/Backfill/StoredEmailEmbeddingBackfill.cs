// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Embeddings.Backfill;

/// <summary>Walks the stored mail, giving one generation the passages and the vectors it does not yet have.</summary>
/// <remarks>
/// <para>
/// The work is bounded, idempotent, and restartable, and it is deliberately the same shape as the extraction backfill:
/// a run processes at most a configured number of batches and then reports whether more remain, and the position each
/// message's turn commits is what the next run resumes from. What is outstanding is decided by the absence of a vector
/// rather than remembered anywhere, so a run interrupted between two provider calls re-embeds nothing it already paid
/// for.
/// </para>
/// <para>
/// Which generation it walks towards is the caller's decision and never this type's. The same walk fills the mail a
/// live path never reached under the generation now serving, and fills a new generation from nothing while the old one
/// goes on answering searches — one mechanism, because the two are the same question asked of two profiles.
/// </para>
/// <para>
/// Two things are outstanding here rather than one, and the order between them is the whole point: a message stored
/// before chunking existed has extracted text and no passages, and nothing can be embedded until those passages are
/// cut. Cutting them costs a database write and no provider call, and it reads text an earlier extraction already
/// stored, so this reaches no mail server and cannot touch a remote <c>\Seen</c> flag however long it runs.
/// </para>
/// <para>
/// The walk is one walk over the deployment's mail and its resume position is one cursor, deliberately, on a system
/// that bounds embedding spend per owner. A cursor per owner would be the shape to reach for if a sweep served one
/// owner at a time; it serves all of them at once, in identifier order, so a per-owner cursor would record the same
/// walk several times over and the run would still have to visit every message to decide which cursor to move. What an
/// owner's ceiling costs is that owner's messages being stepped over for the rest of the period, which the walk above
/// already does without remembering anything. The embedding profile is deployment-wide for the same kind of reason and
/// a different one:
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0006-embedding-profile-identity-lifecycle-and-activation-cost.md">ADR 0006</see>
/// makes a profile the meaning of a stored vector, and two owners' vectors sharing an index have to mean the same
/// thing. Neither is an omission left for a later change to fill in.
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
    private readonly IStoredEmailEmbeddingBackfillStore backfillStore;
    private readonly StoredEmailEmbeddingGenerator embeddingGenerator;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly IDerivedWorkGateTelemetry gateTelemetry;
    private readonly StoredEmailEmbeddingBackfillOptions options;

    /// <summary>Initializes a new embedding backfill.</summary>
    /// <param name="backfillStore">Reads what remains and writes both the passages and the position a run produced.</param>
    /// <param name="embeddingGenerator">Brings one message up to date, which is the same unit of work the live worker performs.</param>
    /// <param name="concurrencyRetryPolicy">Commits a write, retrying a conflict with a competing writer.</param>
    /// <param name="gateTelemetry">Records why a message the classification gate was holding is being derived from now.</param>
    /// <param name="options">Bounds one run.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public StoredEmailEmbeddingBackfill(
        IStoredEmailEmbeddingBackfillStore backfillStore,
        StoredEmailEmbeddingGenerator embeddingGenerator,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        IDerivedWorkGateTelemetry gateTelemetry,
        StoredEmailEmbeddingBackfillOptions options)
    {
        ArgumentNullException.ThrowIfNull(backfillStore);
        ArgumentNullException.ThrowIfNull(embeddingGenerator);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(gateTelemetry);
        ArgumentNullException.ThrowIfNull(options);

        this.backfillStore = backfillStore;
        this.embeddingGenerator = embeddingGenerator;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.gateTelemetry = gateTelemetry;
        this.options = options;
    }

    /// <summary>Runs one bounded pass of the backfill towards one generation.</summary>
    /// <param name="target">The generation whose missing vectors this run produces.</param>
    /// <param name="cancellationToken">Cancels the run between messages, between batches, and inside a message's turn.</param>
    /// <returns>What this run produced, and why it ended.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="target" /> is <see langword="null" />.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race that the bounded retries could not resolve. Passages and vectors
    /// already committed stay durable, and the next run resumes from the committed position.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels. Committed passages, vectors, and positions stay durable.</exception>
    /// <remarks>
    /// The target is taken once for the whole walk rather than per message, because it is what the outstanding-work
    /// query is expressed against: a run that changed generation half way through would leave two prefixes of two
    /// generations and a position that describes neither.
    /// </remarks>
    public async Task<StoredEmailEmbeddingBackfillResult> RunAsync(
        RegisteredEmbeddingProfile target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var position = await this.backfillStore.FindResumePositionAsync(cancellationToken);
        var outstandingAtSweepStart = position is null
            ? await this.backfillStore.CountEmailsAwaitingEmbeddingAsync(target.Id, cancellationToken)
            : (int?)null;

        var progress = RunProgress.Empty;

        for (var batchNumber = 1; batchNumber <= this.options.MaxBatchesPerRun; batchNumber++)
        {
            var batch = await this.backfillStore.GetEmailsAwaitingEmbeddingAsync(
                position,
                target.Id,
                this.options.BatchSize,
                cancellationToken);

            if (batch.Count == 0)
            {
                await this.CommitPositionAsync(position: null, cancellationToken);

                return Ended(StoredEmailEmbeddingBackfillOutcome.SweepCompleted, progress, outstandingAtSweepStart);
            }

            foreach (var email in batch)
            {
                var turn = await this.BringUpToDateAsync(email, target, cancellationToken);
                progress = progress.Add(email, turn);

                // This says nothing about the message, so stepping past it would skip one for a reason that has nothing
                // to do with it. The position stays where it was and an operator settles it.
                if (turn.Outcome is StoredEmailEmbeddingOutcome.GeneratorDisagreesWithProfile)
                {
                    return Ended(
                        StoredEmailEmbeddingBackfillOutcome.GeneratorDisagreesWithProfile,
                        progress,
                        outstandingAtSweepStart);
                }

                // The deployment's ceiling stops the run before the position steps past this message, because unlike a
                // refused provider call it says nothing about the message and the passages it did not reach are the
                // ones the rolled-over period should pay for first.
                if (turn.ReachedSpendBound is EmbeddingSpendBound.Deployment)
                {
                    return Ended(
                        StoredEmailEmbeddingBackfillOutcome.SpendCeilingReached,
                        progress,
                        outstandingAtSweepStart,
                        spendPeriodEndsAt: turn.SpendPeriodEndsAt,
                        reachedSpendBound: turn.ReachedSpendBound);
                }

                // One owner's ceiling stops that owner and nobody else, so the walk steps past the message rather than
                // ending: the identifier order interleaves owners, so ending here would leave every other owner's mail
                // unembedded until the period rolled over — which is the harm a per-owner ceiling exists to prevent.
                // The message keeps its outstanding passages, which is what the next sweep selects on.
                if (turn.ReachedSpendBound is EmbeddingSpendBound.Owner)
                {
                    position = email.StoredEmailId;
                    await this.CommitPositionAsync(position, cancellationToken);

                    continue;
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

                // A turn that spent every call it is allowed carries on to the next message rather than ending the run,
                // because it says something about that one message's length and nothing about the provider. It is
                // counted rather than passed over: the walk steps past such a message, so without a number of its own a
                // mailbox needing several sweeps to finish one message would look exactly like one finishing them.
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
        RegisteredEmbeddingProfile target,
        CancellationToken cancellationToken)
    {
        if (email.RequiresChunking)
        {
            // Cutting a message's first passages is where a message the classification gate held is let through, and the
            // only place the release is decidable one message at a time — the sweeps below it are narrowed by a
            // set-based predicate that answers about a batch rather than about a message. A message with passages
            // already cut is ordinary outstanding work and says nothing about the gate, so it is not counted here.
            this.gateTelemetry.RecordAdmission(email.Admission);

            await this.concurrencyRetryPolicy.CommitAsync(
                (persistenceSession, attemptCancellationToken) => this.backfillStore.DeriveChunksAsync(
                    persistenceSession,
                    email.StoredEmailId,
                    attemptCancellationToken),
                cancellationToken);
        }

        return await this.embeddingGenerator.EmbedAsync(email.StoredEmailId, target, cancellationToken);
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
        EmbeddingGenerationFailure? failure = null,
        DateTimeOffset? spendPeriodEndsAt = null,
        EmbeddingSpendBound reachedSpendBound = EmbeddingSpendBound.None) =>
        new(
            outcome,
            progress.ChunkedEmailCount,
            progress.EmbeddedEmailCount,
            progress.EmbeddedChunkCount,
            progress.CallBudgetExhaustedEmailCount,
            progress.OwnerSpendCeilingEmailCount,
            progress.OwnerSpendPeriodEndsAt,
            outstandingAtSweepStart,
            failure,
            spendPeriodEndsAt,
            reachedSpendBound);

    /// <summary>What a run has produced so far, in counts alone.</summary>
    /// <remarks>
    /// A message counts as embedded only when its turn reported the message whole. One that spent every call a turn is
    /// allowed keeps the passages it did get — which the passage count carries — and is deliberately not counted as a
    /// message brought up to date, because a later sweep still has to reach it. The same holds for one the walk stepped
    /// past because its owner had spent their period: it is counted where an operator can see it and nowhere else, and
    /// the period it was refused in is carried beside the count so the worker can report it once rather than per pass.
    /// </remarks>
    private sealed record RunProgress(
        int ChunkedEmailCount,
        int EmbeddedEmailCount,
        int EmbeddedChunkCount,
        int CallBudgetExhaustedEmailCount,
        int OwnerSpendCeilingEmailCount,
        DateTimeOffset? OwnerSpendPeriodEndsAt)
    {
        public static RunProgress Empty { get; } = new(
            ChunkedEmailCount: 0,
            EmbeddedEmailCount: 0,
            EmbeddedChunkCount: 0,
            CallBudgetExhaustedEmailCount: 0,
            OwnerSpendCeilingEmailCount: 0,
            OwnerSpendPeriodEndsAt: null);

        public RunProgress Add(StoredEmailAwaitingEmbedding email, StoredEmailEmbeddingRun turn) => this with
        {
            ChunkedEmailCount = this.ChunkedEmailCount + (email.RequiresChunking ? 1 : 0),
            EmbeddedEmailCount = this.EmbeddedEmailCount
                + (turn.Outcome == StoredEmailEmbeddingOutcome.Embedded ? 1 : 0),
            EmbeddedChunkCount = this.EmbeddedChunkCount + turn.EmbeddedChunkCount,
            CallBudgetExhaustedEmailCount = this.CallBudgetExhaustedEmailCount
                + (turn.Outcome == StoredEmailEmbeddingOutcome.CallBudgetExhausted ? 1 : 0),
            OwnerSpendCeilingEmailCount = this.OwnerSpendCeilingEmailCount
                + (turn.ReachedSpendBound == EmbeddingSpendBound.Owner ? 1 : 0),

            // The latest one wins rather than the first, because a sweep can outlive a rollover: two owners refused on
            // either side of it are one count, and the period a reader acts on is the one still in force.
            OwnerSpendPeriodEndsAt = turn.ReachedSpendBound == EmbeddingSpendBound.Owner
                ? turn.SpendPeriodEndsAt
                : this.OwnerSpendPeriodEndsAt,
        };
    }
}
