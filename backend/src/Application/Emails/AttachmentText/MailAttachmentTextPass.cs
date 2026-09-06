// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Emails.AttachmentText;

/// <summary>Reads the attachments of the account's mail the cut has already been through, and offers each message for embedding again.</summary>
/// <remarks>
/// <para>
/// The fifth stage of the arrival pipeline and the last of the run's local passes, which is where it belongs rather
/// than where it happened to fit. It runs behind the cut for one reason: reading an attachment is the only stage that
/// parses octets a stranger composed and the only one that may call a provider, so a message reaches lexical and
/// semantic retrieval on its own words first, and gains its attachments' words when they arrive. A body passage cut by
/// the stage in front of this one is untouched by anything here — its digest, its ordinal, and its vector all stay
/// exactly as they were, which is what stops attachments from re-billing a mailbox that was already embedded.
/// </para>
/// <para>
/// Nothing is derived inside a transaction. Each message is read whole first — content store, parsers, and where a
/// picture is involved a chat provider — and only then is one statement committed carrying the readings, the passages
/// cut from them, and the stamp that takes the message out of the selection. A crash between the two leaves the message
/// exactly as it was, and the next run repeats the reading rather than half of it.
/// </para>
/// <para>
/// It needs no cursor for the reason the cut needs none: a message leaves the selection by being read. What one pass's
/// budget leaves behind is the next run's.
/// </para>
/// </remarks>
public sealed class MailAttachmentTextPass
{
    /// <summary>How many messages one batch reads.</summary>
    /// <remarks>
    /// Far smaller than the cut's batch, because every message here costs a read of stored raw MIME and a parse of each
    /// attachment on it. What bounds the pass in the end is the run's octet budget rather than this number; the batch
    /// only decides how often the walk goes back to the database for more.
    /// </remarks>
    private const int BatchSize = 25;

    /// <summary>How many batches one pass may commit before it leaves the rest to the next run.</summary>
    private const int MaxBatchesPerPass = 20;

    private readonly IStoredEmailAttachmentTextStore attachmentTextStore;
    private readonly EmailAttachmentTextDeriver deriver;
    private readonly EmailAttachmentTextBounds bounds;
    private readonly IEmailEmbeddingBacklog embeddingBacklog;
    private readonly IDerivedWorkGateTelemetry gateTelemetry;
    private readonly OptimisticConcurrencyRetryPolicy commitPolicy;

    /// <summary>Initializes the pass from the state it walks, the derivation it runs, and the backlog it hands messages to.</summary>
    /// <param name="attachmentTextStore">Reads what is awaiting a reading and stores what one produced.</param>
    /// <param name="deriver">Reads one message's attachments, outside any transaction.</param>
    /// <param name="bounds">The ceilings one message and one run are read under.</param>
    /// <param name="embeddingBacklog">Takes each message whose attachments yielded passages on to the embedding worker.</param>
    /// <param name="gateTelemetry">Reports which of the classification gate's answers let each message through.</param>
    /// <param name="commitPolicy">Commits one message's readings, retrying a conflict with a competing writer.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailAttachmentTextPass(
        IStoredEmailAttachmentTextStore attachmentTextStore,
        EmailAttachmentTextDeriver deriver,
        EmailAttachmentTextBounds bounds,
        IEmailEmbeddingBacklog embeddingBacklog,
        IDerivedWorkGateTelemetry gateTelemetry,
        OptimisticConcurrencyRetryPolicy commitPolicy)
    {
        ArgumentNullException.ThrowIfNull(attachmentTextStore);
        ArgumentNullException.ThrowIfNull(deriver);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(embeddingBacklog);
        ArgumentNullException.ThrowIfNull(gateTelemetry);
        ArgumentNullException.ThrowIfNull(commitPolicy);

        this.attachmentTextStore = attachmentTextStore;
        this.deriver = deriver;
        this.bounds = bounds;
        this.embeddingBacklog = embeddingBacklog;
        this.gateTelemetry = gateTelemetry;
        this.commitPolicy = commitPolicy;
    }

    /// <summary>Takes one bounded pass over the account's mail awaiting a reading of its attachments.</summary>
    /// <param name="account">The account whose mail is read.</param>
    /// <param name="cancellationToken">Cancels the pass between messages and between batches; committed readings stay durable.</param>
    /// <returns>How many messages this pass read, and what it left behind.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">Thrown when a competing writer wins a race the bounded retries could not resolve.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels. Committed readings stay durable.</exception>
    public async Task<MailAttachmentTextPassReport> RunAsync(
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        // The switch is honoured here rather than at composition, so a deployment that has not turned attachment
        // reading on runs a pass that answers in one comparison and issues no query at all — and there is one place
        // rather than several where the answer to "does this instance read attachments" is given.
        if (!this.bounds.IsEnabled)
        {
            return new MailAttachmentTextPassReport(
                ReadEmailCount: 0,
                RefusedOfferCount: 0,
                RunBudgetExhausted: false,
                EmailsRemain: false);
        }

        var runBudget = new EmailAttachmentTextRunBudget(this.bounds.MaxInputOctetsPerAccountRun);
        var readCount = 0;
        var refusedCount = 0;
        var emailsRemain = false;

        for (var batchNumber = 1; batchNumber <= MaxBatchesPerPass && !runBudget.IsExhausted; batchNumber++)
        {
            var batch = await this.attachmentTextStore.GetEmailsAwaitingAttachmentTextAsync(
                account,
                BatchSize,
                cancellationToken);

            if (batch.Count == 0)
            {
                emailsRemain = false;

                break;
            }

            foreach (var email in batch)
            {
                var derived = await this.deriver.DeriveAsync(email, runBudget, cancellationToken);

                // The run ran out of octets while this message was in hand. Nothing about it has been decided, so
                // nothing is written: it stays outstanding and the next run, which starts with a full budget, reaches
                // it first.
                if (derived is null)
                {
                    return new MailAttachmentTextPassReport(
                        readCount,
                        refusedCount,
                        RunBudgetExhausted: true,
                        EmailsRemain: true);
                }

                await this.commitPolicy.CommitAsync(
                    (session, attemptCancellationToken) => this.attachmentTextStore.SaveAttachmentTextAsync(
                        session,
                        email.Id,
                        derived,
                        attemptCancellationToken),
                    cancellationToken);

                // Recorded here for the reason the cut records it where it does: this is where the gate's decision
                // becomes an act, a message it was holding having been released by having its attachments read.
                this.gateTelemetry.RecordAdmission(email.Admission);

                readCount++;

                // Offered only where words were actually stored. A message every attachment of which was refused has
                // gained no passage, so waking the embedding worker for it would be a queue slot spent on nothing.
                if (derived.YieldedText && !this.embeddingBacklog.TryEnqueue(email.Id))
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

        return new MailAttachmentTextPassReport(
            readCount,
            refusedCount,
            RunBudgetExhausted: false,
            emailsRemain);
    }
}
