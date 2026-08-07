// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Embeddings.Generation;

/// <summary>Gives every passage of one stored message a vector under the profile this instance embeds into.</summary>
/// <remarks>
/// <para>
/// The operation reads committed state and writes committed state, and the provider call between the two happens with
/// no transaction open. That ordering is the whole reason this is a separate unit of work from synchronization: an
/// embedding request that took a minute would otherwise hold a database transaction open for a minute, and a provider
/// outage would stall the mailbox fetch behind it.
/// </para>
/// <para>
/// It is idempotent at the granularity of a passage. What is outstanding is decided by the store rather than remembered
/// by the caller, so a message offered twice, a run interrupted mid-message, and a process that crashed after committing
/// half of one all resume by asking the same question and re-embedding only what is genuinely missing.
/// </para>
/// <para>
/// Nothing here retries a provider call. The adapter behind
/// <see cref="ITextEmbeddingGenerator" /> runs every call under a named resilience pipeline with its own bounded,
/// jittered attempts, and a second layer around it would multiply the two attempt counts against a provider that is
/// already refusing.
/// </para>
/// </remarks>
public sealed class StoredEmailEmbeddingGenerator
{
    /// <summary>How many provider calls one message's turn may make before the turn ends.</summary>
    /// <remarks>
    /// <para>
    /// It is here so that a store which reported passages as outstanding and then stored nothing for them ends the turn
    /// instead of spending against a provider in a loop. A constant rather than a setting, because it bounds a defect
    /// rather than describing a deployment.
    /// </para>
    /// <para>
    /// It is deliberately not claimed to be unreachable. How many passages one message yields is decided by the
    /// chunking rules and the extraction ceiling — a chunk may be as short as the rules' minimum, so a long message can
    /// yield far more passages than a round number here would suggest — and how many of them one call carries is a
    /// deployment's own <c>MaxPassagesPerRequest</c>. A small batch size against a long message therefore can reach
    /// this, which is why running out is reported as <see cref="StoredEmailEmbeddingOutcome.CallBudgetExhausted" />
    /// rather than folded into the outcome that says the message is whole.
    /// </para>
    /// </remarks>
    private const int MaximumProviderCallsPerEmail = 512;

    private readonly IActiveEmbeddingProfileReader profileReader;
    private readonly IEmailEmbeddingStore embeddingStore;
    private readonly ITextEmbeddingGenerator textEmbeddingGenerator;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;

    /// <summary>Initializes a new generator of one message's embeddings.</summary>
    /// <param name="profileReader">Answers which vector space this instance embeds into.</param>
    /// <param name="embeddingStore">Reads which passages lack a vector, and writes the vectors that answer.</param>
    /// <param name="textEmbeddingGenerator">Turns passages into points of that space.</param>
    /// <param name="concurrencyRetryPolicy">Commits one call's vectors, retrying a conflict with a competing writer.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public StoredEmailEmbeddingGenerator(
        IActiveEmbeddingProfileReader profileReader,
        IEmailEmbeddingStore embeddingStore,
        ITextEmbeddingGenerator textEmbeddingGenerator,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy)
    {
        ArgumentNullException.ThrowIfNull(profileReader);
        ArgumentNullException.ThrowIfNull(embeddingStore);
        ArgumentNullException.ThrowIfNull(textEmbeddingGenerator);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);

        this.profileReader = profileReader;
        this.embeddingStore = embeddingStore;
        this.textEmbeddingGenerator = textEmbeddingGenerator;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
    }

    /// <summary>Embeds whatever of one message is not yet embedded under the active profile.</summary>
    /// <param name="storedEmailId">The message to bring up to date.</param>
    /// <param name="cancellationToken">Cancels the turn between calls and between commits.</param>
    /// <returns>How the turn ended, and how many passages it committed vectors for.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race the bounded retries could not resolve. Vectors already committed stay
    /// durable and the passages they cover are no longer outstanding.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels or the host is shutting down. Committed vectors stay durable.</exception>
    public async Task<StoredEmailEmbeddingRun> EmbedAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        var profile = await this.profileReader.FindActiveProfileAsync(cancellationToken);
        if (profile is null)
        {
            return StoredEmailEmbeddingRun.NoActiveProfile();
        }

        // Compared through the fingerprint rather than property by property, because that digest is what the profile
        // table is unique on: agreeing here is the same statement as resolving to this row at activation.
        if (EmbeddingProfileFingerprint.Compute(this.textEmbeddingGenerator.Identity)
            != EmbeddingProfileFingerprint.Compute(profile.Identity))
        {
            return StoredEmailEmbeddingRun.GeneratorDisagreesWithProfile();
        }

        var embeddedChunkCount = 0;

        for (var call = 0; call < MaximumProviderCallsPerEmail; call++)
        {
            var passages = await this.embeddingStore.GetChunksAwaitingEmbeddingAsync(
                storedEmailId,
                profile.Id,
                this.textEmbeddingGenerator.MaximumPassagesPerCall,
                cancellationToken);

            // The one way out that means the message is whole: the store has nothing left to report. Falling out of the
            // loop instead means the budget ran out with passages still outstanding, which is a different answer.
            if (passages.Count == 0)
            {
                return StoredEmailEmbeddingRun.Embedded(embeddedChunkCount);
            }

            IReadOnlyList<EmbeddingVector> vectors;
            try
            {
                vectors = await this.textEmbeddingGenerator.GenerateAsync(
                    [.. passages.Select(passage => passage.Text)],
                    cancellationToken);
            }
            catch (EmbeddingGenerationFailedException generationFailure)
            {
                return StoredEmailEmbeddingRun.ProviderFailed(embeddedChunkCount, generationFailure.Failure);
            }

            await this.CommitVectorsAsync(profile, passages, vectors, cancellationToken);

            embeddedChunkCount += passages.Count;
        }

        // The budget is spent, and that alone does not say the message is unfinished: the last call may have taken the
        // final passages, leaving the loop with nowhere to go rather than with work left. One more read settles it, and
        // it is paid for only on the turn that reached the ceiling. Claiming either answer without asking would be
        // wrong in one direction or the other, and reporting a whole message as truncated is a false warning exactly as
        // reporting a truncated one as whole is a false success.
        var outstanding = await this.embeddingStore.GetChunksAwaitingEmbeddingAsync(
            storedEmailId,
            profile.Id,
            maxCount: 1,
            cancellationToken);

        return outstanding.Count == 0
            ? StoredEmailEmbeddingRun.Embedded(embeddedChunkCount)
            : StoredEmailEmbeddingRun.CallBudgetExhausted(embeddedChunkCount);
    }

    /// <summary>Commits one call's vectors, so a crash leaves a whole page of passages embedded or none of it.</summary>
    private Task CommitVectorsAsync(
        ActiveEmbeddingProfile profile,
        IReadOnlyList<EmailChunkAwaitingEmbedding> passages,
        IReadOnlyList<EmbeddingVector> vectors,
        CancellationToken cancellationToken)
    {
        GeneratedChunkEmbedding[] embeddings =
            [.. passages.Zip(vectors, (passage, vector) => new GeneratedChunkEmbedding(passage.Id, vector))];

        return this.concurrencyRetryPolicy.CommitAsync(
            (persistenceSession, attemptCancellationToken) => this.embeddingStore.SaveEmbeddingsAsync(
                persistenceSession,
                profile,
                embeddings,
                attemptCancellationToken),
            cancellationToken);
    }
}
