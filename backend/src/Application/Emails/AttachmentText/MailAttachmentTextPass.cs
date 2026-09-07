// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.AttachmentText.Limits;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Persistence;
using MailFathom.Application.Spam.Gating;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

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
/// Three ceilings bound it and they are asked in order of how much they cost to reach. The run's octet budget is a
/// counter in memory, so it is spent inside the derivation itself; the deployment's aggregate ceilings are durable rows
/// and are read once per message, before the message is opened; and the per-attachment and per-message ceilings are
/// applied by the derivation as it walks. Reaching any of them ends the pass with mail still outstanding rather than
/// failing the account run — the work waits for the next run or for the period to roll over, which is what an exhausted
/// embedding budget already does for message text.
/// </para>
/// <para>
/// It carries a resume position across its batches, which the cut does not need. Most messages leave the selection by
/// being read, but one whose stored copy needs fetching again, or whose picture a provider did not answer for, keeps no
/// stamp on purpose — so without a cursor the next batch would select exactly those messages again, re-read them, and
/// never reach anything behind them, calling the provider once per batch on the way. The position is dropped at the end
/// of the pass rather than persisted: what one pass's budget leaves behind is the next run's, and that run starts at
/// the head of a selection those messages are still in.
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
    private readonly AttachmentDerivationSpendGate spendGate;

    /// <summary>Initializes the pass from the state it walks, the derivation it runs, and the backlog it hands messages to.</summary>
    /// <param name="attachmentTextStore">Reads what is awaiting a reading and stores what one produced.</param>
    /// <param name="deriver">Reads one message's attachments, outside any transaction.</param>
    /// <param name="bounds">The ceilings one message and one run are read under.</param>
    /// <param name="embeddingBacklog">Takes each message whose attachments yielded passages on to the embedding worker.</param>
    /// <param name="gateTelemetry">Reports which of the classification gate's answers let each message through.</param>
    /// <param name="commitPolicy">Commits one message's readings, retrying a conflict with a competing writer.</param>
    /// <param name="spendGate">Says whether the period still admits reading and describing for this owner, and is charged for what it did.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailAttachmentTextPass(
        IStoredEmailAttachmentTextStore attachmentTextStore,
        EmailAttachmentTextDeriver deriver,
        EmailAttachmentTextBounds bounds,
        IEmailEmbeddingBacklog embeddingBacklog,
        IDerivedWorkGateTelemetry gateTelemetry,
        OptimisticConcurrencyRetryPolicy commitPolicy,
        AttachmentDerivationSpendGate spendGate)
    {
        ArgumentNullException.ThrowIfNull(attachmentTextStore);
        ArgumentNullException.ThrowIfNull(deriver);
        ArgumentNullException.ThrowIfNull(bounds);
        ArgumentNullException.ThrowIfNull(embeddingBacklog);
        ArgumentNullException.ThrowIfNull(gateTelemetry);
        ArgumentNullException.ThrowIfNull(commitPolicy);
        ArgumentNullException.ThrowIfNull(spendGate);

        this.attachmentTextStore = attachmentTextStore;
        this.deriver = deriver;
        this.bounds = bounds;
        this.embeddingBacklog = embeddingBacklog;
        this.gateTelemetry = gateTelemetry;
        this.commitPolicy = commitPolicy;
        this.spendGate = spendGate;
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
        StoredEmailId? resumeAfter = null;

        for (var batchNumber = 1; batchNumber <= MaxBatchesPerPass && !runBudget.IsExhausted; batchNumber++)
        {
            var batch = await this.attachmentTextStore.GetEmailsAwaitingAttachmentTextAsync(
                account,
                resumeAfter,
                BatchSize,
                cancellationToken);

            if (batch.Count == 0)
            {
                emailsRemain = false;

                break;
            }

            foreach (var email in batch)
            {
                // Read before the message is opened rather than after it has been paid for, and read per message
                // because whose mail it is decides which owner's ceiling applies. A period reached here stops the pass
                // with the message untouched, which is the degradation the ceilings promise: the work waits for the
                // roll-over rather than failing the account run it is part of. The overshoot that admitting a whole
                // message on any remaining room allows is bounded by what one message may cost, which is the ceiling
                // above this one.
                if (await this.FindReachedPeriodCeilingAsync(email, cancellationToken) is { } reachedCeiling)
                {
                    return new MailAttachmentTextPassReport(
                        readCount,
                        refusedCount,
                        RunBudgetExhausted: false,
                        EmailsRemain: true,
                        reachedCeiling.Step,
                        reachedCeiling.ReachedBound);
                }

                var derived = await this.deriver.DeriveAsync(email, runBudget, cancellationToken);

                await this.commitPolicy.CommitAsync(
                    (session, attemptCancellationToken) => this.CommitAsync(
                        session,
                        email,
                        derived,
                        attemptCancellationToken),
                    cancellationToken);

                // The run ran out of octets while this message was in hand. Nothing about the message has been decided,
                // so no reading is written: it stays outstanding and the next run, which starts with a full budget,
                // reaches it first. What the earlier attachments already spent was charged above, because the octets
                // were genuinely parsed and the calls were genuinely made.
                if (derived.RunBudgetExhausted)
                {
                    return new MailAttachmentTextPassReport(
                        readCount,
                        refusedCount,
                        RunBudgetExhausted: true,
                        EmailsRemain: true);
                }

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

                // Advanced per message rather than per batch, so an interruption inside a batch resumes behind the
                // message it committed rather than in front of the batch it was part of.
                resumeAfter = email.Id;
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

    /// <summary>Names the aggregate ceiling that refuses this owner's next message, or nothing where both admit it.</summary>
    /// <remarks>
    /// The deployment's ceiling is reported in preference to the owner's by the admission itself, and extraction is
    /// asked before description because a message is read before any picture on it is sent anywhere: an operator whose
    /// extraction period is spent is told about the ceiling that actually stopped the walk.
    /// </remarks>
    private async Task<AttachmentDerivationAdmission?> FindReachedPeriodCeilingAsync(
        EmailAwaitingAttachmentText email,
        CancellationToken cancellationToken)
    {
        var extraction = await this.spendGate.ReadCurrentPeriodForAsync(
            AttachmentDerivationStep.Extraction,
            email.Owner,
            cancellationToken);

        if (!extraction.AdmitsWork)
        {
            return extraction;
        }

        var description = await this.spendGate.ReadCurrentPeriodForAsync(
            AttachmentDerivationStep.Description,
            email.Owner,
            cancellationToken);

        return description.AdmitsWork ? null : description;
    }

    /// <summary>Commits one message's readings and the two charges the reading incurred, as one durable fact.</summary>
    /// <remarks>
    /// The charges join the statement that stores the readings rather than following it, so a crash between the two
    /// cannot leave a mailbox read that nothing was charged for, or a period charged for readings that were never
    /// stored. Each step is charged in its own unit and only where it consumed anything, which keeps a deployment that
    /// describes no pictures from writing a row saying it asked for none.
    /// A reading the run's octet budget ran out under stores nothing and is still charged: it decided nothing about the
    /// message, but the attachments it reached before the budget ran out were parsed and described for real, and a
    /// ledger that omitted them would report less consumed than the provider is about to bill.
    /// </remarks>
    private async Task CommitAsync(
        IPersistenceSession session,
        EmailAwaitingAttachmentText email,
        EmailAttachmentTextDerivation derived,
        CancellationToken cancellationToken)
    {
        if (!derived.RunBudgetExhausted)
        {
            await this.attachmentTextStore.SaveAttachmentTextAsync(session, email.Id, derived, cancellationToken);
        }

        await this.spendGate.RecordSpendAsync(
            session,
            AttachmentDerivationStep.Extraction,
            email.Owner,
            derived.ExtractedOctetCount,
            cancellationToken);

        await this.spendGate.RecordSpendAsync(
            session,
            AttachmentDerivationStep.Description,
            email.Owner,
            derived.ProviderDescriptionCount,
            cancellationToken);
    }
}
