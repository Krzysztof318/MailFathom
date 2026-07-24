// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.MessageContent;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
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
        var inspectedWindowCount = 0;

        while (hasMore && inspectedWindowCount < this.options.MaxUidWindowsPerRun)
        {
            inspectedWindowCount++;
            var batch = await session.GetMessageBatchAfterAsync(checkpoint.LastSeenUid, this.options.MaxMetadataBatchSize, cancellationToken);
            await using var persistenceSession = await this.sessionScopeFactory.BeginSessionAsync(cancellationToken);
            foreach (var metadata in batch.Messages.OrderBy(message => message.OccurrenceId.Uid.Value))
            {
                if (metadata.SizeOctets > this.options.MaxRawMimeBytes)
                {
                    skippedOversizedCount++;
                    continue;
                }

                try
                {
                    var content = await session.FetchMessageContentWithoutSettingSeenAsync(metadata.OccurrenceId, this.options.MaxRawMimeBytes, cancellationToken);
                    await this.contentStore.SaveContentAsync(persistenceSession, content, cancellationToken);
                    await this.metadataRepository.UpsertMetadataAsync(persistenceSession, metadata, cancellationToken);
                    storedCount++;
                }
                catch (MessageContentTooLargeException)
                {
                    skippedOversizedCount++;
                }
            }

            checkpoint = checkpoint.AdvanceTo(batch.InspectedThroughUid, this.timeProvider.GetUtcNow());
            await this.checkpointStore.SaveCheckpointAsync(persistenceSession, accountId, folderName, checkpoint, cancellationToken);
            await persistenceSession.CommitAsync(cancellationToken);
            hasMore = batch.HasMore;
        }

        return new MailboxSynchronizationResult(storedCount, skippedOversizedCount, hasMore, checkpoint);
    }
}

/// <summary>Summarizes one mailbox synchronization run.</summary>
public sealed record MailboxSynchronizationResult(
    int StoredMessageCount,
    int SkippedOversizedMessageCount,
    bool HasMoreMessages,
    SynchronizationCheckpoint Checkpoint);
