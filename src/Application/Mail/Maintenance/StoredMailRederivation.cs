// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
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
/// same reading of the same immutable bytes. Repeating the pass until the scope is exhausted belongs to
/// <see cref="StoredMailRederivationHandler" />, which is the durable work an operator's request enqueues.
/// </para>
/// </remarks>
public sealed class StoredMailRederivation
{
    /// <summary>How many stored emails one batch re-reads before it commits.</summary>
    /// <remarks>
    /// A constant rather than a setting, because it bounds one pass's memory and one interrupted batch's lost work
    /// rather than describing a deployment. What decides how long the walk runs is the attempt carrying it, so there is
    /// no interval and no host health for a number here to be tuned against.
    /// </remarks>
    private const int BatchSize = 50;

    /// <summary>How many batches one pass commits before it answers that mail remains.</summary>
    /// <remarks>
    /// What keeps one pass bounded, so an interrupted pass loses at most a batch and the attempt carrying the walk
    /// records its progress and renews its lease often enough to hold the work it is doing.
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
    /// three orders of magnitude for the same five hundred rows. What a pass owes is a committed position often enough
    /// for the attempt to be stoppable, so it ends on whichever ceiling it reaches first and the messages it left behind
    /// are the next pass's. It is
    /// read against what the pass has read so far rather than against one batch, and checked before each email rather
    /// than between batches, because a batch is fifty messages and a ceiling only a batch boundary enforces is one a
    /// batch of large messages passes fifty times over before anything looks.
    /// </remarks>
    private const long MaximumRawBytesPerPass = 64L * 1024 * 1024;

    private readonly IStoredMailRederivationStore rederivationStore;
    private readonly IStoredMailRederivationRunStore runStore;
    private readonly IEmailContentStore contentStore;
    private readonly IEmailMimeReader mimeReader;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly TimeProvider timeProvider;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the re-derivation.</summary>
    /// <param name="rederivationStore">Reads what the walk has left and writes what one email's re-reading produced.</param>
    /// <param name="runStore">Holds the run this walk is advancing, which each batch adds what it read to.</param>
    /// <param name="contentStore">Reads back the raw MIME an earlier run stored.</param>
    /// <param name="mimeReader">Turns that raw MIME into normalized metadata.</param>
    /// <param name="concurrencyRetryPolicy">Commits a batch, retrying a conflict with a competing writer.</param>
    /// <param name="timeProvider">Stamps the instant the walk reached the end of its scope.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public StoredMailRederivation(
        IStoredMailRederivationStore rederivationStore,
        IStoredMailRederivationRunStore runStore,
        IEmailContentStore contentStore,
        IEmailMimeReader mimeReader,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        TimeProvider timeProvider,
        AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(rederivationStore);
        ArgumentNullException.ThrowIfNull(runStore);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(mimeReader);
        ArgumentNullException.ThrowIfNull(concurrencyRetryPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(authorization);

        this.rederivationStore = rederivationStore;
        this.runStore = runStore;
        this.contentStore = contentStore;
        this.mimeReader = mimeReader;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.timeProvider = timeProvider;
        this.authorization = authorization;
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
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when anything but this deployment's own process reached the use case.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancels. Committed batches stay durable.</exception>
    /// <remarks>
    /// It asks for no permission, deliberately, and requires the process itself instead. What reaches it is a job this
    /// deployment enqueued when an operator asked for the run, so there is no caller to hold a grant, and requiring an
    /// administrative one here would mean the walk ran under a credential nobody presented. The operator's grant is
    /// asked for where the operator is — <see cref="StoredMailRederivationRequests" /> — and requiring the process
    /// identity here is what makes a caller reaching this method from an entrypoint added later a refusal rather than a
    /// mailbox-wide pass under no grant at all.
    /// </remarks>
    public async Task<StoredMailRederivationPass> RunAsync(
        StoredMailScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        this.authorization.RequireProcessIdentity();

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

            var outcome = await this.ReadBatchAsync(batch, readByteCount, cancellationToken);

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

    /// <summary>Re-reads a batch's emails outside any transaction, stopping early once either ceiling is reached.</summary>
    /// <param name="batch">The emails the walk offered, in the order it visits them.</param>
    /// <param name="bytesAlreadyReadThisPass">What earlier batches of this pass read, which the byte ceiling is against.</param>
    /// <param name="cancellationToken">Cancels between emails.</param>
    private async Task<BatchReadOutcome> ReadBatchAsync(
        IReadOnlyList<StoredMailAwaitingRederivation> batch,
        long bytesAlreadyReadThisPass,
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
            // Both ceilings are checked before the read and never before the first one, so a single email larger than
            // either still makes progress instead of stalling the walk on itself forever. The bytes are counted across
            // the pass rather than within this batch, because a ceiling checked only where a batch ends is no ceiling
            // on what one batch of large messages reads.
            if (processedCount > 0
                && (retainedCharacterCount >= MaximumRetainedTextCharactersPerBatch
                    || bytesAlreadyReadThisPass + readByteCount >= MaximumRawBytesPerPass))
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

    /// <summary>Commits one batch's re-readings, the position they reached, and what the run has now re-read.</summary>
    /// <remarks>
    /// The three are one transaction because the position is what the next attempt resumes past: counts written
    /// afterwards would be lost by a process killed in between, and the mail behind them would never be walked again
    /// either, so the figure an operator reads would be permanently short of what was really done. An attempt is
    /// stopped mid-pass as a matter of course rather than exceptionally — the execution timeout is what ends most of
    /// them — so that gap would be the ordinary case rather than a rare one.
    /// <para>
    /// The run is re-read inside the commit and added to, never written back from a reading taken before the batch,
    /// because two attempts of one run can overlap for as long as it takes a lost lease to be noticed.
    /// </para>
    /// </remarks>
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

                if (await this.runStore.FindAsync(scope, attemptCancellationToken) is not { IsOutstanding: true } run)
                {
                    return;
                }

                await this.runStore.SaveAsync(
                    persistenceSession,
                    run with
                    {
                        RederivedEmailCount = run.RederivedEmailCount + outcome.Rederivations.Count,
                        UnreadableEmailCount = run.UnreadableEmailCount + outcome.UnreadableEmailCount,
                        MissingContentEmailCount = run.MissingContentEmailCount + outcome.MissingContentEmailCount,
                    },
                    attemptCancellationToken);
            },
            cancellationToken);

    /// <summary>Records that this scope's walk is over, so asking for it again starts at the beginning.</summary>
    /// <remarks>
    /// The cursor is removed and the run is ended in one transaction, for the reason the batch commits its counts with
    /// its position: a walk whose cursor is gone while its run is still outstanding reads as a run nothing is carrying,
    /// and one whose run ended while its cursor stands would have the next request resume behind where this one
    /// finished.
    /// </remarks>
    private Task FinishAsync(StoredMailScope scope, CancellationToken cancellationToken) =>
        this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                await this.rederivationStore.ClearResumePositionAsync(
                    persistenceSession,
                    scope,
                    attemptCancellationToken);

                if (await this.runStore.FindAsync(scope, attemptCancellationToken) is not { IsOutstanding: true } run)
                {
                    return;
                }

                await this.runStore.SaveAsync(
                    persistenceSession,
                    run with { EndedAt = this.timeProvider.GetUtcNow() },
                    attemptCancellationToken);
            },
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
