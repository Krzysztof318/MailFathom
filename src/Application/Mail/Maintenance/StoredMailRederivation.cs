// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Mail.Maintenance;

/// <summary>Re-reads the raw MIME a deployment already stores into the properties a newer release records from it.</summary>
/// <remarks>
/// <para>
/// The cheap half of filling in properties a newer release added, and the one that answers wherever the property is
/// already in the payload this deployment stored — the sender identity a receiving server authenticated is today's
/// example. No mailbox session is opened, nothing is fetched, and no stored content is rewritten: what the pass costs
/// is a read of local bytes, a parse, and an update of the row's own columns.
/// </para>
/// <para>
/// It says nothing about state that lives only on a server. Flags, keywords, and the internal date are the mailbox's
/// answers rather than the message's, so a property drawn from one of those needs
/// <see cref="MailSynchronizationRewind" /> and the fetch that comes with it.
/// </para>
/// <para>
/// One invocation is one bounded pass, and it reports whether the scope still holds mail it has not reached. A batch
/// commits its re-readings together with the position they reached, so an interrupted pass resumes at the next email
/// rather than repeating or stepping over one, and re-running over an email this pass already reached simply writes the
/// same reading of the same immutable bytes.
/// </para>
/// </remarks>
public sealed class StoredMailRederivation
{
    /// <summary>How many stored emails one batch re-reads before it commits.</summary>
    /// <remarks>
    /// A constant rather than a setting, because it bounds one request's memory and one interrupted batch's lost work
    /// rather than describing a deployment. The pass runs because an operator asked for it and ends when the command
    /// stops asking, so there is no interval, no queue depth, and no host health for a number here to be tuned against.
    /// </remarks>
    private const int BatchSize = 50;

    /// <summary>How many batches one pass commits before it answers that mail remains.</summary>
    /// <remarks>
    /// What keeps one request bounded, so an interrupted command loses at most a batch and the deployment answers often
    /// enough for the command to report progress between passes.
    /// </remarks>
    private const int MaxBatchesPerPass = 10;

    /// <summary>How many characters of extracted text one batch may hold before it commits what it has.</summary>
    /// <remarks>
    /// The batch size bounds how many emails one commit covers and not how much text they hold, and the two ceilings
    /// multiply — a batch whose emails all carry the largest permitted body would otherwise be held in memory in full,
    /// twice over, before anything was committed. The emails this budget leaves behind are simply the next batch's.
    /// </remarks>
    private const int MaximumRetainedTextCharactersPerBatch = 4_000_000;

    /// <summary>How many bytes of raw MIME one pass reads before it answers with what it has.</summary>
    /// <remarks>
    /// The batch count bounds how many messages a pass reads and says nothing about how large they are, and the two are
    /// not related: a scope of one-kilobyte notifications and a scope of messages carrying a video attachment differ by
    /// three orders of magnitude for the same five hundred rows. What the caller is waiting on is one HTTP request, so
    /// the pass ends on whichever ceiling it reaches first and the messages it left behind are the next pass's.
    /// </remarks>
    private const long MaximumRawBytesPerPass = 64L * 1024 * 1024;

    private readonly IStoredMailRederivationStore rederivationStore;
    private readonly IEmailContentStore contentStore;
    private readonly IEmailMimeReader mimeReader;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;

    /// <summary>Initializes the re-derivation.</summary>
    /// <param name="rederivationStore">Reads what the walk has left and writes what one email's re-reading produced.</param>
    /// <param name="contentStore">Reads back the raw MIME an earlier run stored.</param>
    /// <param name="mimeReader">Turns that raw MIME into normalized metadata.</param>
    /// <param name="concurrencyRetryPolicy">Commits a batch, retrying a conflict with a competing writer.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public StoredMailRederivation(
        IStoredMailRederivationStore rederivationStore,
        IEmailContentStore contentStore,
        IEmailMimeReader mimeReader,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy)
    {
        ArgumentNullException.ThrowIfNull(rederivationStore);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(mimeReader);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);

        this.rederivationStore = rederivationStore;
        this.contentStore = contentStore;
        this.mimeReader = mimeReader;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
    }

    /// <summary>Runs one bounded pass over the scope's stored mail.</summary>
    /// <param name="scope">The account, and the one folder of it, whose stored mail is re-read.</param>
    /// <param name="cancellationToken">Cancels the pass between batches and between emails.</param>
    /// <returns>What this pass re-derived, and whether the scope still holds mail a further pass would reach.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="scope" /> is <see langword="null" />.</exception>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race that the bounded retries could not resolve. Batches already committed
    /// stay durable, and the next pass resumes from the committed position.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels. Committed batches stay durable.</exception>
    public async Task<StoredMailRederivationPass> RunAsync(
        StoredMailScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var position = await this.rederivationStore.FindResumePositionAsync(scope, cancellationToken);
        var rederivedCount = 0;
        var unreadableCount = 0;
        var missingContentCount = 0;
        var readByteCount = 0L;

        for (var batchNumber = 1; batchNumber <= MaxBatchesPerPass; batchNumber++)
        {
            var batch = await this.rederivationStore.GetEmailsToRederiveAsync(
                scope,
                position,
                BatchSize,
                cancellationToken);

            if (batch.Count == 0)
            {
                await this.FinishAsync(scope, cancellationToken);

                return new StoredMailRederivationPass(
                    rederivedCount,
                    unreadableCount,
                    missingContentCount,
                    EmailsRemain: false);
            }

            var outcome = await this.ReadBatchAsync(batch, cancellationToken);

            await this.CommitBatchAsync(scope, outcome, cancellationToken);

            position = outcome.LastProcessedEmailId;
            rederivedCount += outcome.Rederivations.Count;
            unreadableCount += outcome.UnreadableEmailCount;
            missingContentCount += outcome.MissingContentEmailCount;
            readByteCount += outcome.ReadByteCount;

            // A short batch means the query found nothing more behind this position, and a batch the text budget cut
            // short leaves the rest of its own emails behind. Only the first of those ends the walk, and it ends it
            // here rather than on the next iteration so a scope whose last batch was exactly full is not reported as
            // finished before anything has looked behind it.
            if (batch.Count < BatchSize && outcome.ProcessedEmailCount == batch.Count)
            {
                await this.FinishAsync(scope, cancellationToken);

                return new StoredMailRederivationPass(
                    rederivedCount,
                    unreadableCount,
                    missingContentCount,
                    EmailsRemain: false);
            }

            if (readByteCount >= MaximumRawBytesPerPass)
            {
                break;
            }
        }

        return new StoredMailRederivationPass(
            rederivedCount,
            unreadableCount,
            missingContentCount,
            EmailsRemain: true);
    }

    /// <summary>Re-reads a batch's emails outside any transaction, stopping early once it is holding enough text.</summary>
    private async Task<BatchReadOutcome> ReadBatchAsync(
        IReadOnlyList<StoredMailAwaitingRederivation> batch,
        CancellationToken cancellationToken)
    {
        var rederivations = new List<CompletedRederivation>(batch.Count);
        var missingContentCount = 0;
        var unreadableCount = 0;
        var retainedCharacterCount = 0;
        var readByteCount = 0L;
        var processedCount = 0;
        var lastProcessedEmailId = batch[0].StoredEmailId;

        foreach (var email in batch)
        {
            // Checked before the read and never before the first one, so a single email larger than the whole budget
            // still makes progress instead of stalling the walk on itself forever.
            if (processedCount > 0 && retainedCharacterCount >= MaximumRetainedTextCharactersPerBatch)
            {
                break;
            }

            processedCount++;
            lastProcessedEmailId = email.StoredEmailId;

            var storedContent = await this.contentStore.FindStoredContentAsync(email.StoredEmailId, cancellationToken);
            if (storedContent is null)
            {
                missingContentCount++;

                continue;
            }

            readByteCount += storedContent.RawMime.Length;

            var extraction = await this.mimeReader.ReadMetadataAsync(
                new RemoteEmailContent(email.OccurrenceId, storedContent.RawMime),
                cancellationToken);

            // A message no reader can parse keeps what it already holds and the position moves past it, exactly as
            // synchronization and the extraction backfill both step over one. Writing an empty reading over a row a
            // previous release parsed would lose properties to a refresh that was asked to add one.
            if (extraction.Metadata is { } metadata)
            {
                rederivations.Add(new CompletedRederivation(email.StoredEmailId, metadata));
                retainedCharacterCount += RetainedCharacterCount(metadata);
            }
            else
            {
                unreadableCount++;
            }
        }

        return new BatchReadOutcome(
            rederivations,
            unreadableCount,
            missingContentCount,
            processedCount,
            readByteCount,
            lastProcessedEmailId);
    }

    /// <summary>Counts the characters one completed re-reading keeps alive until its batch commits.</summary>
    private static int RetainedCharacterCount(ExtractedEmailMetadata metadata) =>
        (metadata.Text.OriginalText?.Length ?? 0) + (metadata.Text.TrimmedText?.Length ?? 0);

    /// <summary>Commits one batch's re-readings together with the position they reached.</summary>
    private Task CommitBatchAsync(
        StoredMailScope scope,
        BatchReadOutcome outcome,
        CancellationToken cancellationToken) =>
        this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                foreach (var rederivation in outcome.Rederivations)
                {
                    await this.rederivationStore.ApplyRederivedMetadataAsync(
                        persistenceSession,
                        rederivation.StoredEmailId,
                        rederivation.Metadata,
                        attemptCancellationToken);
                }

                await this.rederivationStore.SaveResumePositionAsync(
                    persistenceSession,
                    scope,
                    outcome.LastProcessedEmailId,
                    attemptCancellationToken);
            },
            cancellationToken);

    /// <summary>Records that this scope's walk is over, so asking for it again starts at the beginning.</summary>
    private Task FinishAsync(StoredMailScope scope, CancellationToken cancellationToken) =>
        this.concurrencyRetryPolicy.CommitAsync(
            (persistenceSession, attemptCancellationToken) => this.rederivationStore.ClearResumePositionAsync(
                persistenceSession,
                scope,
                attemptCancellationToken),
            cancellationToken);

    /// <summary>Pairs one email with what re-reading its MIME produced.</summary>
    private sealed record CompletedRederivation(StoredEmailId StoredEmailId, ExtractedEmailMetadata Metadata);

    /// <summary>What one batch's re-reading produced, before any of it was committed.</summary>
    /// <remarks>
    /// The two rejected counts stay apart because they ask the operator different questions: one is a message nobody
    /// can parse, the other a row whose raw MIME another operation removed while this pass was walking towards it. The
    /// last processed identity is carried rather than taken from the end of the batch, because the text budget can stop
    /// a batch short and committing the batch's last identity would then step over emails nobody read. The bytes read
    /// are what the pass adds up against its own ceiling, and they count what was read rather than what parsed, because
    /// a message no reader could make sense of cost the same read as one that did.
    /// </remarks>
    private sealed record BatchReadOutcome(
        IReadOnlyList<CompletedRederivation> Rederivations,
        int UnreadableEmailCount,
        int MissingContentEmailCount,
        int ProcessedEmailCount,
        long ReadByteCount,
        StoredEmailId LastProcessedEmailId);
}
