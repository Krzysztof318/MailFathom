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
    }

    /// <summary>Synchronizes one account folder without mutating remote mailbox flags.</summary>
    public async Task<MailboxSynchronizationResult> SynchronizeAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        CancellationToken cancellationToken)
    {
        var checkpoint = await this.checkpointStore.GetCheckpointAsync(accountId, folderName, cancellationToken);

        await using var mailboxSession = await this.mailboxSessionFactory.OpenReadOnlyAsync(accountId, folderName, cancellationToken);

        var uidValidity = await mailboxSession.GetUidValidityAsync(cancellationToken);
        checkpoint = checkpoint?.UidValidity == uidValidity ? checkpoint : SynchronizationCheckpoint.None(uidValidity);

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
                else
                {
                    skippedOversizedCount++;
                }
            }

            if (batch.InspectedThroughUid is { } inspectedThroughUid)
            {
                checkpoint = checkpoint.AdvanceTo(inspectedThroughUid, this.timeProvider.GetUtcNow());

                await this.SaveCheckpointAsync(accountId, folderName, checkpoint, cancellationToken);
            }

            hasMore = batch.HasMore;
        }

        return new MailboxSynchronizationResult(storedCount, skippedOversizedCount, hasMore, checkpoint);
    }

    private async Task<OccurrenceOutcome> StoreOccurrenceAsync(
        IMailboxSession mailboxSession,
        RemoteEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        if (metadata.SizeOctets > this.options.MaxRawMimeBytes)
        {
            await this.RecordOversizedOccurrenceAsync(metadata, cancellationToken);

            return OccurrenceOutcome.SkippedOversized;
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
            await this.RecordOversizedOccurrenceAsync(metadata, cancellationToken);

            return OccurrenceOutcome.SkippedOversized;
        }

        await using var persistenceSession = await this.persistenceSessionFactory.BeginSessionAsync(cancellationToken);

        var storedEmailId = await this.metadataRepository.UpsertMetadataAsync(persistenceSession, metadata, StoredEmailContentAvailability.Available, cancellationToken);
        await this.contentStore.SaveContentAsync(persistenceSession, storedEmailId, content, cancellationToken);
        await persistenceSession.CommitAsync(cancellationToken);

        return OccurrenceOutcome.Stored;
    }

    private async Task RecordOversizedOccurrenceAsync(
        RemoteEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var persistenceSession = await this.persistenceSessionFactory.BeginSessionAsync(cancellationToken);

        await this.metadataRepository.UpsertMetadataAsync(persistenceSession, metadata, StoredEmailContentAvailability.ExceededSizeLimit, cancellationToken);
        await persistenceSession.CommitAsync(cancellationToken);
    }

    private async Task SaveCheckpointAsync(
        MailAccountId accountId,
        MailFolderName folderName,
        SynchronizationCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        await using var persistenceSession = await this.persistenceSessionFactory.BeginSessionAsync(cancellationToken);

        await this.checkpointStore.SaveCheckpointAsync(persistenceSession, accountId, folderName, checkpoint, cancellationToken);
        await persistenceSession.CommitAsync(cancellationToken);
    }

    private enum OccurrenceOutcome
    {
        Stored,
        SkippedOversized,
    }
}

/// <summary>Summarizes one mailbox synchronization run.</summary>
public sealed record MailboxSynchronizationResult(
    int StoredEmailCount,
    int SkippedOversizedEmailCount,
    bool HasMoreEmails,
    SynchronizationCheckpoint Checkpoint);
