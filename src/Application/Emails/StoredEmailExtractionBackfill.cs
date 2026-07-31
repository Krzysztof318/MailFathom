// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Application.EmailContent;
using MailMcp.Application.Persistence;
using MailMcp.Domain.Emails;

namespace MailMcp.Application.Emails;

/// <summary>Re-derives normalized metadata and searchable text from the raw MIME of emails stored before extraction existed.</summary>
/// <remarks>
/// <para>
/// The work is bounded, idempotent, and restartable. A run processes at most a configured number of batches and then
/// reports whether more remain; a batch commits its extractions together with the position it reached, so an
/// interrupted run resumes at the next email rather than repeating or skipping one; and re-running over an email that
/// already has its extraction simply overwrites it with the same reading of the same immutable bytes.
/// </para>
/// <para>
/// Nothing here reaches a mail server. Every byte it reads was fetched and stored by an earlier synchronization run, so
/// a backfill cannot touch a remote <c>\Seen</c> flag however long it runs.
/// </para>
/// </remarks>
public sealed class StoredEmailExtractionBackfill
{
    /// <summary>How many characters of extracted text one batch may hold before it commits what it has.</summary>
    /// <remarks>
    /// Roughly sixteen megabytes of UTF-16 once both the trimmed and the untrimmed reading of each message are counted.
    /// It is a constant rather than a setting because it bounds this operation's memory rather than describing a
    /// deployment: an operator tuning the batch size is choosing how much progress an interrupted run loses, and should
    /// not have to know that the choice also multiplies with the text bound.
    /// </remarks>
    private const int MaximumRetainedTextCharactersPerBatch = 4_000_000;

    private readonly IStoredEmailExtractionBackfillStore backfillStore;
    private readonly IEmailContentStore contentStore;
    private readonly IEmailMimeReader mimeReader;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly StoredEmailExtractionBackfillOptions options;

    /// <summary>Initializes a new extraction backfill.</summary>
    /// <param name="backfillStore">Reads what remains and writes what one email's re-reading produced.</param>
    /// <param name="contentStore">Reads back the raw MIME an earlier run stored.</param>
    /// <param name="mimeReader">Turns that raw MIME into normalized metadata and text.</param>
    /// <param name="concurrencyRetryPolicy">Commits a batch, retrying a conflict with a competing writer.</param>
    /// <param name="options">Bounds one run.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public StoredEmailExtractionBackfill(
        IStoredEmailExtractionBackfillStore backfillStore,
        IEmailContentStore contentStore,
        IEmailMimeReader mimeReader,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        StoredEmailExtractionBackfillOptions options)
    {
        ArgumentNullException.ThrowIfNull(backfillStore);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(mimeReader);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(options);

        this.backfillStore = backfillStore;
        this.contentStore = contentStore;
        this.mimeReader = mimeReader;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.options = options;
    }

    /// <summary>Runs one bounded pass of the backfill.</summary>
    /// <param name="cancellationToken">Cancels the run between batches and between emails.</param>
    /// <returns>What this run extracted, and whether emails still await extraction.</returns>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race that the bounded retries could not resolve. Batches already committed
    /// stay durable, and the next run resumes from the committed position.
    /// </exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels. Committed batches stay durable.</exception>
    public async Task<StoredEmailExtractionBackfillResult> RunAsync(CancellationToken cancellationToken)
    {
        var position = await this.backfillStore.FindResumePositionAsync(cancellationToken);
        var extractedCount = 0;
        var unreadableCount = 0;
        var missingContentCount = 0;
        var emailsRemain = false;

        for (var batchNumber = 1; batchNumber <= this.options.MaxBatchesPerRun; batchNumber++)
        {
            var batch = await this.backfillStore.GetEmailsAwaitingExtractionAsync(
                position,
                this.options.BatchSize,
                cancellationToken);

            if (batch.Count == 0)
            {
                return new StoredEmailExtractionBackfillResult(
                    extractedCount,
                    unreadableCount,
                    missingContentCount,
                    EmailsRemain: false);
            }

            var batchOutcome = await this.ReadBatchAsync(batch, cancellationToken);

            await this.CommitBatchAsync(
                batchOutcome.Extractions,
                batchOutcome.LastProcessedEmailId,
                cancellationToken);

            position = batchOutcome.LastProcessedEmailId;
            extractedCount += batchOutcome.Extractions.Count;
            unreadableCount += batchOutcome.UnreadableEmailCount;
            missingContentCount += batchOutcome.MissingContentEmailCount;

            // A short batch means the query found no more work behind this position; a batch the text budget cut short
            // leaves the rest of its own emails behind. Either way the walk continues only while something is left.
            emailsRemain = batch.Count == this.options.BatchSize
                || batchOutcome.ProcessedEmailCount < batch.Count;
            if (!emailsRemain)
            {
                break;
            }
        }

        return new StoredEmailExtractionBackfillResult(
            extractedCount,
            unreadableCount,
            missingContentCount,
            emailsRemain);
    }

    /// <summary>Re-reads a batch's emails outside any transaction, stopping early once it is holding enough text.</summary>
    /// <remarks>
    /// The batch size bounds how many emails one commit covers; it does not bound how much text they hold, and the two
    /// ceilings multiply. A batch of the largest permitted size whose emails all carry the largest permitted body would
    /// otherwise be held in memory in full — twice over, because the untrimmed reading is retained beside the trimmed
    /// one — before anything was committed. The character budget bounds that product without making either setting
    /// depend on the other, and the emails it leaves behind are simply the next batch's.
    /// </remarks>
    private async Task<BatchReadOutcome> ReadBatchAsync(
        IReadOnlyList<StoredEmailAwaitingExtraction> batch,
        CancellationToken cancellationToken)
    {
        var extractions = new List<CompletedExtraction>(batch.Count);
        var missingContentCount = 0;
        var unreadableCount = 0;
        var retainedCharacterCount = 0;
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

            // The recorded length and digest are deliberately not checked here. A backfill re-reads what is stored to
            // fill in metadata it lacks, and a damaged payload simply fails to parse and is counted as unreadable; the
            // integrity check belongs to the read that serves content to a person, which can act on the difference.
            var extraction = await this.mimeReader.ReadMetadataAsync(
                new RemoteEmailContent(email.OccurrenceId, storedContent.RawMime),
                cancellationToken);

            // A message no reader can parse is stepped over exactly as it is during synchronization: it keeps whatever
            // the server's envelope reported, and the committed position moves past it so no later run stops on it.
            if (extraction.Metadata is { } metadata)
            {
                extractions.Add(new CompletedExtraction(email.StoredEmailId, metadata));
                retainedCharacterCount += RetainedCharacterCount(metadata);
            }
            else
            {
                unreadableCount++;
            }
        }

        return new BatchReadOutcome(
            extractions,
            unreadableCount,
            missingContentCount,
            processedCount,
            lastProcessedEmailId);
    }

    /// <summary>Counts the characters one completed extraction keeps alive until its batch commits.</summary>
    private static int RetainedCharacterCount(ExtractedEmailMetadata metadata) =>
        (metadata.Text.OriginalText?.Length ?? 0) + (metadata.Text.TrimmedText?.Length ?? 0);

    /// <summary>Commits one batch's extractions together with the position they reached.</summary>
    private Task CommitBatchAsync(
        IReadOnlyList<CompletedExtraction> extractions,
        StoredEmailId position,
        CancellationToken cancellationToken) =>
        this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                foreach (var extraction in extractions)
                {
                    await this.backfillStore.ApplyExtractionAsync(
                        persistenceSession,
                        extraction.StoredEmailId,
                        extraction.Metadata,
                        attemptCancellationToken);
                }

                await this.backfillStore.SaveResumePositionAsync(
                    persistenceSession,
                    position,
                    attemptCancellationToken);
            },
            cancellationToken);

    /// <summary>Pairs one email with what re-reading its MIME produced.</summary>
    private sealed record CompletedExtraction(StoredEmailId StoredEmailId, ExtractedEmailMetadata Metadata);

    /// <summary>What one batch's re-reading produced, before any of it was committed.</summary>
    /// <remarks>
    /// The two rejected counts stay apart because they ask the operator different questions: one is a message nobody
    /// can parse, the other a row whose raw MIME another operation removed while this run was walking towards it.
    /// The last processed identity is carried rather than taken from the end of the batch, because the text budget can
    /// stop a batch short and committing the batch's last identity would then step over emails nobody read.
    /// </remarks>
    private sealed record BatchReadOutcome(
        IReadOnlyList<CompletedExtraction> Extractions,
        int UnreadableEmailCount,
        int MissingContentEmailCount,
        int ProcessedEmailCount,
        StoredEmailId LastProcessedEmailId);
}

/// <summary>Summarizes one bounded run of the extraction backfill.</summary>
/// <param name="ExtractedEmailCount">How many stored emails were re-read and had their metadata and text written.</param>
/// <param name="UnreadableEmailCount">How many stored emails carried MIME no reader could parse, which the run stepped over.</param>
/// <param name="MissingContentEmailCount">How many stored emails no longer had raw MIME to re-read.</param>
/// <param name="EmailsRemain">Whether emails still await extraction after this run's batch budget was spent.</param>
/// <remarks>
/// Every field is a count. Nothing derived from a message — no subject, address, or fragment of body text — belongs in
/// a result a worker logs.
/// </remarks>
public sealed record StoredEmailExtractionBackfillResult(
    int ExtractedEmailCount,
    int UnreadableEmailCount,
    int MissingContentEmailCount,
    bool EmailsRemain);
