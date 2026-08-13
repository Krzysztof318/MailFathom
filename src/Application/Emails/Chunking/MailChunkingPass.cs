// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Emails.Chunking;

/// <summary>Cuts the passages of the account's mail the earlier stages have finished with, and offers each message for embedding.</summary>
/// <remarks>
/// <para>
/// This is the fourth stage of the arrival pipeline, and it is a stage of its own precisely because of what runs in
/// front of it. Classification decides whether a message is derived from at all, the owner's rules may move it into a
/// folder mapped differently from the one it arrived in, and both of those happen after the transaction that stored the
/// message committed. Cutting inside that transaction would therefore write passages from a message's placement before
/// the two stages allowed to change it had run — and passages are not undone by the message moving afterwards.
/// </para>
/// <para>
/// Running after the rule pass is not the whole of that, and the selection carries the rest: a rule declares a move
/// rather than performing one, and the account's next run is what carries it to the mail server, so a message this
/// pass meets may be one still on its way out of the folder it is sitting in. Such a message is passed over and cut
/// once it has arrived where the rule sent it. <c>StoredEmailChunkingStore.Selecting</c> states which records hold a
/// cut back and which have stopped mattering.
/// </para>
/// <para>
/// A step of the account's synchronization run rather than a schedule of its own, for the reason the classification and
/// rule passes are: that run already has per-account isolation, a slot count, a jittered backoff, and a failure path
/// that defers the account instead of the process, and only one pass per account is ever in flight because of it.
/// </para>
/// <para>
/// It needs no cursor. A message leaves the selection by being cut, so an interrupted pass repeats nothing and skips
/// nothing, and what one pass's batch budget leaves behind is the next run's — or the
/// <see cref="Embeddings.Backfill.StoredEmailEmbeddingBackfill" /> sweep's, which selects on the same condition and is
/// what reaches mail the account run never gets to.
/// </para>
/// </remarks>
public sealed class MailChunkingPass
{
    /// <summary>How many messages one batch cuts.</summary>
    /// <remarks>
    /// A constant rather than a setting, because it bounds this operation's memory and transaction size rather than
    /// describing a deployment: what an operator tunes about arriving mail is the synchronization interval and the
    /// embedding bounds, and neither is made better by choosing how many messages one local cut commits together.
    /// </remarks>
    private const int BatchSize = 200;

    /// <summary>How many batches one pass may commit before it leaves the rest to the next run.</summary>
    /// <remarks>
    /// The bound exists so that an account whose whole mailbox is awaiting the cut — a first synchronization, or a
    /// mapping switched on over stored mail — does not hold its own run open indefinitely while every other account
    /// waits for a slot. What it leaves behind is outstanding work in exactly the sense the embedding sweep already
    /// selects on.
    /// </remarks>
    private const int MaxBatchesPerPass = 25;

    private readonly IStoredEmailChunkingStore chunkingStore;
    private readonly IEmailEmbeddingBacklog embeddingBacklog;
    private readonly IDerivedWorkGateTelemetry gateTelemetry;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;

    /// <summary>Initializes the pass from the state it walks and the backlog it hands messages to.</summary>
    /// <param name="chunkingStore">Reads what is awaiting the cut and performs it.</param>
    /// <param name="embeddingBacklog">Takes each cut message on to the embedding worker.</param>
    /// <param name="gateTelemetry">Reports which of the classification gate's answers let each message through.</param>
    /// <param name="commitPolicy">Commits one message's passages, retrying a conflict with a competing writer.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailChunkingPass(
        IStoredEmailChunkingStore chunkingStore,
        IEmailEmbeddingBacklog embeddingBacklog,
        IDerivedWorkGateTelemetry gateTelemetry,
        OptimisticConcurrencyRetryPolicy commitPolicy)
    {
        ArgumentNullException.ThrowIfNull(chunkingStore);
        ArgumentNullException.ThrowIfNull(embeddingBacklog);
        ArgumentNullException.ThrowIfNull(gateTelemetry);
        ArgumentNullException.ThrowIfNull(commitPolicy);

        this.chunkingStore = chunkingStore;
        this.embeddingBacklog = embeddingBacklog;
        this.gateTelemetry = gateTelemetry;
        this.commitPolicy = commitPolicy;
    }

    /// <summary>Takes one bounded pass over the account's mail awaiting the cut.</summary>
    /// <param name="accountId">The account whose mail is cut.</param>
    /// <param name="cancellationToken">Cancels the pass between messages and between batches; committed passages stay durable.</param>
    /// <returns>How many messages this pass cut and offered, and whether more remain.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race the bounded retries could not resolve. Messages already cut stay
    /// durable and the next run resumes by asking the same question.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels. Committed passages stay durable.</exception>
    public async Task<MailChunkingPassReport> RunAsync(MailAccountId accountId, CancellationToken cancellationToken)
    {
        var chunkedCount = 0;
        var refusedCount = 0;
        var emailsRemain = false;

        for (var batchNumber = 1; batchNumber <= MaxBatchesPerPass; batchNumber++)
        {
            var batch = await this.chunkingStore.GetEmailsAwaitingChunkingAsync(
                accountId,
                BatchSize,
                cancellationToken);

            if (batch.Count == 0)
            {
                emailsRemain = false;

                break;
            }

            foreach (var email in batch)
            {
                // Committed one message at a time rather than one batch at a time, because the offer that follows must
                // name a message whose passages are already durable: a batch commit would either offer messages before
                // the transaction they are in has ended, or hold every one of them back until the last of them was cut.
                await this.commitPolicy.CommitAsync(
                    (session, attemptCancellationToken) => this.chunkingStore.DeriveChunksAsync(
                        session,
                        email.StoredEmailId,
                        attemptCancellationToken),
                    cancellationToken);

                // Recorded here because this is where the gate's decision becomes an act. A message the gate was holding
                // is let through by being cut, and this pass is the one place that release is decidable per message on
                // the live path.
                this.gateTelemetry.RecordAdmission(email.Admission);

                chunkedCount++;

                if (!this.embeddingBacklog.TryEnqueue(email.StoredEmailId))
                {
                    refusedCount++;
                }
            }

            emailsRemain = batch.Count == BatchSize;

            if (!emailsRemain)
            {
                break;
            }
        }

        return new MailChunkingPassReport(chunkedCount, refusedCount, emailsRemain);
    }
}
