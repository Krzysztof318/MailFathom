// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.EmailContent;
using MailMcp.Application.Persistence;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Synchronization;

namespace MailMcp.Application.Synchronization;

/// <summary>Coordinates read-only mailbox folder synchronization into local persistence.</summary>
public sealed class MailboxSynchronizer
{
    private readonly IMailboxSessionFactory mailboxSessionFactory;
    private readonly ISynchronizationCheckpointStore checkpointStore;
    private readonly IPersistenceSessionFactory persistenceSessionFactory;
    private readonly IEmailMetadataRepository metadataRepository;
    private readonly IEmailContentStore contentStore;
    private readonly TimeProvider timeProvider;
    private readonly MailboxSynchronizationOptions options;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;

    /// <summary>Initializes a new mailbox synchronizer.</summary>
    public MailboxSynchronizer(
        IMailboxSessionFactory mailboxSessionFactory,
        ISynchronizationCheckpointStore checkpointStore,
        IPersistenceSessionFactory persistenceSessionFactory,
        IEmailMetadataRepository metadataRepository,
        IEmailContentStore contentStore,
        TimeProvider timeProvider,
        MailboxSynchronizationOptions options)
    {
        this.mailboxSessionFactory = mailboxSessionFactory;
        this.checkpointStore = checkpointStore;
        this.persistenceSessionFactory = persistenceSessionFactory;
        this.metadataRepository = metadataRepository;
        this.contentStore = contentStore;
        this.timeProvider = timeProvider;
        this.options = options;
        this.concurrencyRetryPolicy = new OptimisticConcurrencyRetryPolicy(
            persistenceSessionFactory,
            options.MaxPersistenceConcurrencyAttempts,
            timeProvider);
    }

    /// <summary>Synchronizes one account folder without mutating remote mailbox flags.</summary>
    public async Task<MailboxSynchronizationResult> SynchronizeAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        CancellationToken cancellationToken)
    {
        var persistedCheckpoint =
            await this.checkpointStore.GetCheckpointAsync(accountId, folderName, cancellationToken);

        await using var mailboxSession = await this.mailboxSessionFactory.OpenReadOnlyAsync(accountId, folderName, cancellationToken);

        var uidValidity = await mailboxSession.GetUidValidityAsync(cancellationToken);
        var checkpoint = persistedCheckpoint?.UidValidity == uidValidity
            ? persistedCheckpoint
            : SynchronizationCheckpoint.None(uidValidity);

        var storedCount = 0;
        var skippedOversizedCount = 0;
        var hasMore = true;
        var inspectedBatchCount = 0;

        while (hasMore && inspectedBatchCount < this.options.MaxMetadataBatchesPerRun)
        {
            inspectedBatchCount++;

            var batch = await mailboxSession.GetEmailBatchAfterAsync(checkpoint.LastSeenUid, this.options.MaxMetadataBatchSize, cancellationToken);
            foreach (var metadata in batch.Emails.OrderBy(email => email.OccurrenceId.Uid.Value))
            {
                var outcome = await this.StoreOccurrenceAsync(mailboxSession, metadata, cancellationToken);
                if (outcome == OccurrenceOutcome.Stored)
                {
                    storedCount++;
                }
                else if (outcome == OccurrenceOutcome.SkippedOversized)
                {
                    skippedOversizedCount++;
                }
                else
                {
                    return new MailboxSynchronizationResult(
                        storedCount,
                        skippedOversizedCount,
                        HasMoreEmails: true,
                        checkpoint,
                        MailboxSynchronizationOutcome.ConcurrencyConflict);
                }
            }

            if (batch.InspectedThroughUid is { } inspectedThroughUid)
            {
                var advancedCheckpoint = checkpoint.AdvanceTo(inspectedThroughUid, this.timeProvider.GetUtcNow());
                var checkpointCommitResult = await this.SaveCheckpointAsync(
                    accountId,
                    folderName,
                    persistedCheckpoint,
                    advancedCheckpoint,
                    cancellationToken);

                if (checkpointCommitResult == PersistenceCommitResult.ConcurrencyConflict)
                {
                    return new MailboxSynchronizationResult(
                        storedCount,
                        skippedOversizedCount,
                        HasMoreEmails: true,
                        checkpoint,
                        MailboxSynchronizationOutcome.ConcurrencyConflict);
                }

                checkpoint = advancedCheckpoint;
                persistedCheckpoint = advancedCheckpoint;
            }

            hasMore = batch.HasMore;
        }

        return new MailboxSynchronizationResult(
            storedCount,
            skippedOversizedCount,
            hasMore,
            checkpoint,
            MailboxSynchronizationOutcome.Completed);
    }

    private async Task<OccurrenceOutcome> StoreOccurrenceAsync(
        IMailboxSession mailboxSession,
        RemoteEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (metadata.SizeOctets > this.options.MaxRawMimeBytes)
        {
            var result = await this.RecordOversizedOccurrenceAsync(metadata, cancellationToken);

            return result == PersistenceCommitResult.Committed
                ? OccurrenceOutcome.SkippedOversized
                : OccurrenceOutcome.ConcurrencyConflict;
        }

        RemoteEmailContent content;
        try
        {
            content = await mailboxSession.FetchEmailContentWithoutSettingSeenAsync(metadata.OccurrenceId, this.options.MaxRawMimeBytes, cancellationToken);
        }
        catch (EmailContentTooLargeException)
        {
            // The advertised size understated the payload, so the occurrence is recorded without content instead of
            // being silently skipped past by the checkpoint.
            var result = await this.RecordOversizedOccurrenceAsync(metadata, cancellationToken);

            return result == PersistenceCommitResult.Committed
                ? OccurrenceOutcome.SkippedOversized
                : OccurrenceOutcome.ConcurrencyConflict;
        }

        var commitResult = await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                var storedEmailId = await this.metadataRepository.UpsertMetadataAsync(
                    persistenceSession,
                    metadata,
                    StoredEmailContentAvailability.Available,
                    attemptCancellationToken);
                await this.contentStore.SaveContentAsync(
                    persistenceSession,
                    storedEmailId,
                    content,
                    attemptCancellationToken);
            },
            cancellationToken);

        return commitResult == PersistenceCommitResult.Committed
            ? OccurrenceOutcome.Stored
            : OccurrenceOutcome.ConcurrencyConflict;
    }

    private Task<PersistenceCommitResult> RecordOversizedOccurrenceAsync(
        RemoteEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        return this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                await this.metadataRepository.UpsertMetadataAsync(
                    persistenceSession,
                    metadata,
                    StoredEmailContentAvailability.ExceededSizeLimit,
                    attemptCancellationToken);
            },
            cancellationToken);
    }

    private async Task<PersistenceCommitResult> SaveCheckpointAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        SynchronizationCheckpoint? expectedCheckpoint,
        SynchronizationCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await using var persistenceSession =
            await this.persistenceSessionFactory.BeginSessionAsync(cancellationToken);

        var saveResult = await this.checkpointStore.SaveCheckpointAsync(
            persistenceSession,
            accountId,
            folderName,
            expectedCheckpoint,
            checkpoint,
            cancellationToken);

        if (saveResult == SynchronizationCheckpointSaveResult.ConcurrencyConflict)
        {
            return PersistenceCommitResult.ConcurrencyConflict;
        }

        return await persistenceSession.CommitAsync(cancellationToken);
    }

    private enum OccurrenceOutcome
    {
        Stored,
        SkippedOversized,
        ConcurrencyConflict,
    }
}

/// <summary>Describes whether a mailbox synchronization run completed or stopped after persistence conflicts.</summary>
public enum MailboxSynchronizationOutcome
{
    /// <summary>The run completed its bounded amount of work without an unresolved persistence conflict.</summary>
    Completed,

    /// <summary>The run stopped after an optimistic concurrency conflict remained unresolved.</summary>
    ConcurrencyConflict,
}

/// <summary>Summarizes one mailbox synchronization run.</summary>
public sealed record MailboxSynchronizationResult(
    int StoredEmailCount,
    int SkippedOversizedEmailCount,
    bool HasMoreEmails,
    SynchronizationCheckpoint Checkpoint,
    MailboxSynchronizationOutcome Outcome);
