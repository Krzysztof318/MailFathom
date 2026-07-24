// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.MessageContent;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Messages;
using MailMcp.Domain.Synchronization;

namespace MailMcp.Application.Synchronization;

/// <summary>Coordinates read-only mailbox folder synchronization into local persistence.</summary>
public sealed class MailboxSynchronizer
{
    private readonly IMailboxSessionFactory sessionFactory;
    private readonly ISynchronizationCheckpointStore checkpointStore;
    private readonly ISessionFactory sessionScopeFactory;
    private readonly IMessageMetadataRepository metadataRepository;
    private readonly IMessageContentStore contentStore;
    private readonly TimeProvider timeProvider;
    private readonly MailboxSynchronizationOptions options;

    /// <summary>Initializes a new mailbox synchronizer.</summary>
    public MailboxSynchronizer(
        IMailboxSessionFactory sessionFactory,
        ISynchronizationCheckpointStore checkpointStore,
        ISessionFactory sessionScopeFactory,
        IMessageMetadataRepository metadataRepository,
        IMessageContentStore contentStore,
        TimeProvider timeProvider,
        MailboxSynchronizationOptions options)
    {
        this.sessionFactory = sessionFactory;
        this.checkpointStore = checkpointStore;
        this.sessionScopeFactory = sessionScopeFactory;
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
        await using var session = await this.sessionFactory.OpenReadOnlyAsync(accountId, folderName, cancellationToken);
        var uidValidity = await session.GetUidValidityAsync(cancellationToken);
        checkpoint = checkpoint?.UidValidity == uidValidity ? checkpoint : SynchronizationCheckpoint.None(uidValidity);
        var storedCount = 0;
        var skippedOversizedCount = 0;
        var hasMore = true;
        var inspectedBatchCount = 0;

        while (hasMore && inspectedBatchCount < this.options.MaxMetadataBatchesPerRun)
        {
            inspectedBatchCount++;
            var batch = await session.GetMessageBatchAfterAsync(checkpoint.LastSeenUid, this.options.MaxMetadataBatchSize, cancellationToken);
            foreach (var metadata in batch.Messages.OrderBy(message => message.OccurrenceId.Uid.Value))
            {
                if (metadata.SizeOctets > this.options.MaxRawMimeBytes)
                {
                    await this.RecordOversizedOccurrenceAsync(metadata, cancellationToken);
                    skippedOversizedCount++;
                    continue;
                }

                RemoteMessageContent content;
                try
                {
                    content = await session.FetchMessageContentWithoutSettingSeenAsync(metadata.OccurrenceId, this.options.MaxRawMimeBytes, cancellationToken);
                }
                catch (MessageContentTooLargeException)
                {
                    // The advertised size understated the payload, so the occurrence is recorded without content instead of
                    // being silently skipped past by the checkpoint.
                    await this.RecordOversizedOccurrenceAsync(metadata, cancellationToken);
                    skippedOversizedCount++;
                    continue;
                }

                await using var messagePersistenceSession = await this.sessionScopeFactory.BeginSessionAsync(cancellationToken);
                var storedEmailId = await this.metadataRepository.UpsertMetadataAsync(messagePersistenceSession, metadata, StoredEmailContentAvailability.Available, cancellationToken);
                await this.contentStore.SaveContentAsync(messagePersistenceSession, storedEmailId, content, cancellationToken);
                await messagePersistenceSession.CommitAsync(cancellationToken);
                storedCount++;
            }

            if (batch.InspectedThroughUid is { } inspectedThroughUid)
            {
                await using var checkpointPersistenceSession = await this.sessionScopeFactory.BeginSessionAsync(cancellationToken);
                checkpoint = checkpoint.AdvanceTo(inspectedThroughUid, this.timeProvider.GetUtcNow());
                await this.checkpointStore.SaveCheckpointAsync(checkpointPersistenceSession, accountId, folderName, checkpoint, cancellationToken);
                await checkpointPersistenceSession.CommitAsync(cancellationToken);
            }

            hasMore = batch.HasMore;
        }

        return new MailboxSynchronizationResult(storedCount, skippedOversizedCount, hasMore, checkpoint);
    }

    private async Task RecordOversizedOccurrenceAsync(
        RemoteMessageMetadata metadata,
        CancellationToken cancellationToken)
    {
        await using var persistenceSession = await this.sessionScopeFactory.BeginSessionAsync(cancellationToken);
        await this.metadataRepository.UpsertMetadataAsync(persistenceSession, metadata, StoredEmailContentAvailability.ExceededSizeLimit, cancellationToken);
        await persistenceSession.CommitAsync(cancellationToken);
    }
}

/// <summary>Summarizes one mailbox synchronization run.</summary>
public sealed record MailboxSynchronizationResult(
    int StoredMessageCount,
    int SkippedOversizedMessageCount,
    bool HasMoreMessages,
    SynchronizationCheckpoint Checkpoint);
