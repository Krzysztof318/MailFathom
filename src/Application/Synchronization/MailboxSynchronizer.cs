// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.EmailContent;
using MailMcp.Application.Mail;
using MailMcp.Application.Persistence;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Synchronization;
using MailMcp.Domain.Transport;

namespace MailMcp.Application.Synchronization;

/// <summary>Coordinates read-only mailbox folder synchronization into local persistence.</summary>
public sealed class MailboxSynchronizer
{
    private readonly IMailboxSessionFactory mailboxSessionFactory;
    private readonly IMailTransportSecurityPolicyReader transportSecurityPolicyReader;
    private readonly ISynchronizationCheckpointStore checkpointStore;
    private readonly IPersistenceSessionFactory persistenceSessionFactory;
    private readonly IEmailMetadataRepository metadataRepository;
    private readonly IEmailContentStore contentStore;
    private readonly OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy;
    private readonly TimeProvider timeProvider;
    private readonly MailboxSynchronizationOptions options;

    /// <summary>Initializes a new mailbox synchronizer.</summary>
    public MailboxSynchronizer(
        IMailboxSessionFactory mailboxSessionFactory,
        IMailTransportSecurityPolicyReader transportSecurityPolicyReader,
        ISynchronizationCheckpointStore checkpointStore,
        IPersistenceSessionFactory persistenceSessionFactory,
        IEmailMetadataRepository metadataRepository,
        IEmailContentStore contentStore,
        OptimisticConcurrencyRetryPolicy concurrencyRetryPolicy,
        TimeProvider timeProvider,
        MailboxSynchronizationOptions options)
    {
        this.mailboxSessionFactory = mailboxSessionFactory;
        this.transportSecurityPolicyReader = transportSecurityPolicyReader;
        this.checkpointStore = checkpointStore;
        this.persistenceSessionFactory = persistenceSessionFactory;
        this.metadataRepository = metadataRepository;
        this.contentStore = contentStore;
        this.concurrencyRetryPolicy = concurrencyRetryPolicy;
        this.timeProvider = timeProvider;
        this.options = options;
    }

    /// <summary>Synchronizes one account folder without mutating remote mailbox flags.</summary>
    /// <param name="accountId">The account to synchronize.</param>
    /// <param name="folderName">The folder to synchronize.</param>
    /// <param name="cancellationToken">Cancels the run between remote reads and local writes.</param>
    /// <returns>The bounded progress this run committed.</returns>
    /// <exception cref="MailTransportSecurityPolicyViolationException">
    /// Thrown when the account's configured transport security policy is unsafe, before any connection is attempted.
    /// </exception>
    /// <exception cref="PersistenceConcurrencyConflictException">
    /// Thrown when a competing writer wins a race that the bounded local retries could not resolve. Progress already
    /// committed by this run stays durable, and the next run rereads the committed checkpoint before deciding again.
    /// </exception>
    public async Task<MailboxSynchronizationResult> SynchronizeAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        CancellationToken cancellationToken)
    {
        var persistedCheckpoint =
            await this.checkpointStore.GetCheckpointAsync(accountId, folderName, cancellationToken);

        var transportSecurityPolicy = this.transportSecurityPolicyReader.GetPolicy(accountId);

        await using var mailboxSession = await this.mailboxSessionFactory.OpenReadOnlyAsync(
            accountId,
            folderName,
            transportSecurityPolicy,
            cancellationToken);

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
                var availability = await this.StoreOccurrenceAsync(mailboxSession, metadata, cancellationToken);
                if (availability == StoredEmailContentAvailability.Available)
                {
                    storedCount++;
                }
                else
                {
                    skippedOversizedCount++;
                }
            }

            if (batch.InspectedThroughUid is { } inspectedThroughUid)
            {
                var advancedCheckpoint = checkpoint.AdvanceTo(inspectedThroughUid, this.timeProvider.GetUtcNow());
                await this.CommitCheckpointAsync(
                    accountId,
                    folderName,
                    persistedCheckpoint,
                    advancedCheckpoint,
                    cancellationToken);

                checkpoint = advancedCheckpoint;
                persistedCheckpoint = advancedCheckpoint;
            }

            hasMore = batch.HasMore;
        }

        return new MailboxSynchronizationResult(
            storedCount,
            skippedOversizedCount,
            hasMore,
            checkpoint);
    }

    private async Task<StoredEmailContentAvailability> StoreOccurrenceAsync(
        IMailboxSession mailboxSession,
        RemoteEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (metadata.SizeOctets > this.options.MaxRawMimeBytes)
        {
            return await this.RecordOversizedOccurrenceAsync(metadata, cancellationToken);
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
            return await this.RecordOversizedOccurrenceAsync(metadata, cancellationToken);
        }

        await this.concurrencyRetryPolicy.CommitAsync(
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

        return StoredEmailContentAvailability.Available;
    }

    private async Task<StoredEmailContentAvailability> RecordOversizedOccurrenceAsync(
        RemoteEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        await this.concurrencyRetryPolicy.CommitAsync(
            async (persistenceSession, attemptCancellationToken) =>
            {
                await this.metadataRepository.UpsertMetadataAsync(
                    persistenceSession,
                    metadata,
                    StoredEmailContentAvailability.ExceededSizeLimit,
                    attemptCancellationToken);
            },
            cancellationToken);

        return StoredEmailContentAvailability.ExceededSizeLimit;
    }

    // A checkpoint advance is attempted once rather than retried: the intended progress was derived from the state read
    // at the start of the run, so a competing advance invalidates the decision itself instead of only the write.
    private async Task CommitCheckpointAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        SynchronizationCheckpoint? expectedCheckpoint,
        SynchronizationCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await using var persistenceSession =
            await this.persistenceSessionFactory.BeginSessionAsync(cancellationToken);

        await this.checkpointStore.SaveCheckpointAsync(
            persistenceSession,
            accountId,
            folderName,
            expectedCheckpoint,
            checkpoint,
            cancellationToken);

        if (await persistenceSession.CommitAsync(cancellationToken) == PersistenceCommitResult.ConcurrencyConflict)
        {
            throw new PersistenceConcurrencyConflictException(
                $"Synchronization progress for folder {folderName.Value} was changed by another writer before this run committed its advance.");
        }
    }
}

/// <summary>Summarizes one mailbox synchronization run.</summary>
public sealed record MailboxSynchronizationResult(
    int StoredEmailCount,
    int SkippedOversizedEmailCount,
    bool HasMoreEmails,
    SynchronizationCheckpoint Checkpoint);
