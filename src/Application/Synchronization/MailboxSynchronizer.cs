// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.MessageContent;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Synchronization;

namespace MailMcp.Application.Synchronization;

/// <summary>Coordinates read-only IMAP folder synchronization into local persistence.</summary>
public sealed class MailboxSynchronizer
{
    private readonly IImapMailboxSessionFactory sessionFactory;
    private readonly ISynchronizationCheckpointStore checkpointStore;
    private readonly IMessageMetadataRepository metadataRepository;
    private readonly IMessageContentStore contentStore;
    private readonly TimeProvider timeProvider;
    private readonly MailboxSynchronizationOptions options;

    /// <summary>Initializes a new mailbox synchronizer.</summary>
    public MailboxSynchronizer(IImapMailboxSessionFactory sessionFactory, ISynchronizationCheckpointStore checkpointStore, IMessageMetadataRepository metadataRepository, IMessageContentStore contentStore, TimeProvider timeProvider, MailboxSynchronizationOptions options)
    {
        this.sessionFactory = sessionFactory;
        this.checkpointStore = checkpointStore;
        this.metadataRepository = metadataRepository;
        this.contentStore = contentStore;
        this.timeProvider = timeProvider;
        this.options = options;
    }

    /// <summary>Synchronizes one account folder without mutating remote IMAP flags.</summary>
    public async Task<MailboxSynchronizationResult> SynchronizeAsync(MailAccountId accountId, MailFolderName folderName, CancellationToken cancellationToken)
    {
        var checkpoint = await checkpointStore.GetCheckpointAsync(accountId, folderName, cancellationToken);
        await using var session = await sessionFactory.OpenReadOnlyAsync(accountId, folderName, cancellationToken);
        var uidValidity = await session.GetUidValidityAsync(cancellationToken);
        checkpoint = checkpoint?.UidValidity == uidValidity ? checkpoint : SynchronizationCheckpoint.None(uidValidity);
        var storedCount = 0;
        var skippedOversizedCount = 0;
        var hasMore = true;
        var inspectedWindowCount = 0;

        while (hasMore && inspectedWindowCount < options.MaxUidWindowsPerRun)
        {
            inspectedWindowCount++;
            var batch = await session.GetMessageBatchAfterAsync(checkpoint.LastSeenUid, options.MaxMetadataBatchSize, cancellationToken);
            foreach (var metadata in batch.Messages.OrderBy(message => message.OccurrenceId.Uid.Value))
            {
                if (metadata.SizeOctets > options.MaxRawMimeBytes)
                {
                    skippedOversizedCount++;
                    continue;
                }

                try
                {
                    var content = await session.FetchMessageContentWithoutSettingSeenAsync(metadata.OccurrenceId, options.MaxRawMimeBytes, cancellationToken);
                    await contentStore.SaveContentAsync(content, cancellationToken);
                    await metadataRepository.UpsertMetadataAsync(metadata, cancellationToken);
                    storedCount++;
                }
                catch (MessageContentTooLargeException)
                {
                    skippedOversizedCount++;
                }
            }

            checkpoint = checkpoint.AdvanceTo(batch.InspectedThroughUid, timeProvider.GetUtcNow());
            await checkpointStore.SaveCheckpointAsync(accountId, folderName, checkpoint, cancellationToken);
            hasMore = batch.HasMore;
        }

        return new MailboxSynchronizationResult(storedCount, skippedOversizedCount, hasMore, checkpoint);
    }
}

/// <summary>Summarizes one mailbox synchronization run.</summary>
public sealed record MailboxSynchronizationResult(int StoredMessageCount, int SkippedOversizedMessageCount, bool HasMoreMessages, SynchronizationCheckpoint Checkpoint);
