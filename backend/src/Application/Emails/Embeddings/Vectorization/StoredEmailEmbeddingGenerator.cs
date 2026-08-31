// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Domain.Access;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Embeddings.Vectorization;

/// <summary>Gives every passage of one stored message a vector under one of this instance's generations.</summary>
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

    private readonly IEmailEmbeddingStore embeddingStore;
    private readonly ITextEmbeddingGenerator textEmbeddingGenerator;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly EmbeddingSpendGate spendGate;
    private readonly EmbeddingRequestPacer requestPacer;
    private readonly IMailOwnership ownership;
    private readonly SensitiveContentEgressGuard egressGuard;

    /// <summary>Initializes a new generator of one message's embeddings.</summary>
    /// <param name="embeddingStore">Reads which passages lack a vector, and writes the vectors that answer.</param>
    /// <param name="textEmbeddingGenerator">Turns passages into points of that space.</param>
    /// <param name="concurrencyRetryPolicy">Commits one call's vectors, retrying a conflict with a competing writer.</param>
    /// <param name="spendGate">Says whether the period still admits a request, and is charged for the ones it does.</param>
    /// <param name="requestPacer">Holds a call back until this deployment is allowed to send its next one.</param>
    /// <param name="ownership">Names the owner whose mail this message is, so the spend is bounded and charged for them.</param>
    /// <param name="egressGuard">States whose mail the passages are, so the adapter that sends them scans under that owner's posture.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public StoredEmailEmbeddingGenerator(
        IEmailEmbeddingStore embeddingStore,
        ITextEmbeddingGenerator textEmbeddingGenerator,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        EmbeddingSpendGate spendGate,
        EmbeddingRequestPacer requestPacer,
        IMailOwnership ownership,
        SensitiveContentEgressGuard egressGuard)
    {
        ArgumentNullException.ThrowIfNull(embeddingStore);
        ArgumentNullException.ThrowIfNull(textEmbeddingGenerator);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(spendGate);
        ArgumentNullException.ThrowIfNull(requestPacer);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(egressGuard);

        this.embeddingStore = embeddingStore;
        this.textEmbeddingGenerator = textEmbeddingGenerator;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.spendGate = spendGate;
        this.requestPacer = requestPacer;
        this.ownership = ownership;
        this.egressGuard = egressGuard;
    }

    /// <summary>Embeds whatever of one message is not yet embedded under one generation.</summary>
    /// <param name="storedEmailId">The message to bring up to date.</param>
    /// <param name="profile">The generation the vectors belong to, which the caller decides and this never reads.</param>
    /// <param name="cancellationToken">Cancels the turn between calls and between commits.</param>
    /// <returns>How the turn ended, and how many passages it committed vectors for.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile" /> is <see langword="null" />.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race the bounded retries could not resolve. Vectors already committed stay
    /// durable and the passages they cover are no longer outstanding.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels or the host is shutting down. Committed vectors stay durable.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no message is stored under <paramref name="storedEmailId" />, so the owner whose spend this turn
    /// would be charged against cannot be established. The caller holds an identifier it read from this deployment, so
    /// what this reports is a message erased underneath the turn rather than an argument a caller can correct. The
    /// ordinary form of that race does not reach it: a message already gone has nothing outstanding, which ends the
    /// turn as whole before an owner is ever asked for, and only one erased between that answer and the ownership
    /// lookup behind it arrives here.
    /// </exception>
    /// <remarks>
    /// The generation is a parameter rather than something read here, because the live path and a reindex write into
    /// different ones at the same moment: mail arriving is embedded into the generation serving searches, and the sweep
    /// fills the one being built. A generator that resolved it itself could only ever serve one of the two.
    /// </remarks>
    public async Task<StoredEmailEmbeddingRun> EmbedAsync(
        StoredEmailId storedEmailId,
        RegisteredEmbeddingProfile profile,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);

        // Compared through the fingerprint rather than property by property, because that digest is what the profile
        // table is unique on: agreeing here is the same statement as resolving to this row at activation.
        if (EmbeddingProfileFingerprint.Compute(this.textEmbeddingGenerator.Identity)
            != EmbeddingProfileFingerprint.Compute(profile.Identity))
        {
            return StoredEmailEmbeddingRun.GeneratorDisagreesWithProfile();
        }

        var embeddedChunkCount = 0;
        var sentCharacterCount = 0;

        // Resolved once for the whole turn, because whose mail a stored message is cannot change while it is being
        // embedded, and resolved lazily rather than up front, because a message with nothing outstanding must not need
        // an owner at all: one erased underneath this turn is an ordinary race that the empty answer below settles,
        // and asking who owned it first would turn that into a refusal a caller would read as a defect.
        MailOwnerId? owner = null;

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
                return StoredEmailEmbeddingRun.Embedded(embeddedChunkCount, sentCharacterCount);
            }

            owner ??= await this.ownership.ReadStoredEmailOwnerAsync(storedEmailId, cancellationToken);

            // Asked before every call rather than once per message, because a long message spends across many calls and
            // a ceiling consulted only at the start would be one a single message could walk straight through.
            var period = await this.spendGate.ReadCurrentPeriodForAsync(owner.Value, cancellationToken);
            if (!period.AdmitsRequest)
            {
                return StoredEmailEmbeddingRun.SpendCeilingReached(
                    embeddedChunkCount,
                    sentCharacterCount,
                    period.EndsAt,
                    period.ReachedBound);
            }

            var billedCharacterCount = CountBilledCharacters(profile, passages);

            await this.requestPacer.WaitForSlotAsync(cancellationToken);

            // The passages are this owner's mail on its way to a provider, and the adapter that sends them guards each
            // one several layers below here. This is where the answer to whose mail it is exists, so it is stated here.
            using var actingFor = this.egressGuard.ActingFor(owner.Value);

            IReadOnlyList<EmbeddingVector> vectors;
            try
            {
                vectors = await this.textEmbeddingGenerator.GenerateAsync(
                    [.. passages.Select(passage => passage.Text)],
                    cancellationToken);
            }
            catch (EmbeddingGenerationFailedException generationFailure)
            {
                return StoredEmailEmbeddingRun.ProviderFailed(
                    embeddedChunkCount,
                    generationFailure.Failure,
                    sentCharacterCount);
            }

            await this.CommitVectorsAsync(profile, owner.Value, passages, vectors, billedCharacterCount, cancellationToken);

            embeddedChunkCount += passages.Count;
            sentCharacterCount += billedCharacterCount;
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
            ? StoredEmailEmbeddingRun.Embedded(embeddedChunkCount, sentCharacterCount)
            : StoredEmailEmbeddingRun.CallBudgetExhausted(embeddedChunkCount, sentCharacterCount);
    }

    /// <summary>Counts what one batch will cost, in the characters a provider is about to be sent.</summary>
    /// <remarks>
    /// Measured through the profile's own input preparation rather than from the passage lengths, because the
    /// preparation is what decides the text a provider actually receives: a passage beyond the model's input limit is
    /// cut before it is sent, and charging a budget for characters nobody was sent would make a long-passage deployment
    /// look like an expensive one.
    /// </remarks>
    private static int CountBilledCharacters(
        RegisteredEmbeddingProfile profile,
        IReadOnlyList<EmailChunkAwaitingEmbedding> passages) =>
        passages.Sum(passage => profile.Identity.InputPreparation.CountBilledCharacters(passage.Text));

    /// <summary>Commits one call's vectors and its cost together, so a crash leaves a whole page embedded or none of it.</summary>
    /// <remarks>
    /// The charge joins the same transaction as the vectors it paid for, which is what makes the ledger unable to
    /// disagree with what was stored. A call whose commit then fails outright is the one case the ledger under-counts,
    /// and it is left that way deliberately: the alternative is a charge in its own transaction, which would over-count
    /// every ordinary conflict the retry policy goes on to resolve.
    /// </remarks>
    private Task CommitVectorsAsync(
        RegisteredEmbeddingProfile profile,
        MailOwnerId owner,
        IReadOnlyList<EmailChunkAwaitingEmbedding> passages,
        IReadOnlyList<EmbeddingVector> vectors,
        int billedCharacterCount,
        CancellationToken cancellationToken)
    {
        GeneratedChunkEmbedding[] embeddings =
            [.. passages.Zip(vectors, (passage, vector) => new GeneratedChunkEmbedding(passage.Id, vector))];

        return this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                await this.embeddingStore.SaveEmbeddingsAsync(
                    persistenceSession,
                    profile,
                    embeddings,
                    attemptCancellationToken);

                await this.spendGate.RecordSpendAsync(
                    persistenceSession,
                    owner,
                    billedCharacterCount,
                    attemptCancellationToken);
            },
            cancellationToken);
    }
}
