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
    private readonly IMailSynchronizationUnitOfWorkFactory unitOfWorkFactory;
    private readonly IMessageMetadataRepository metadataRepository;
    private readonly IMessageContentStore contentStore;
    private readonly TimeProvider timeProvider;
    private readonly MailboxSynchronizationOptions options;

    /// <summary>Initializes a new mailbox synchronizer.</summary>
    public MailboxSynchronizer(IImapMailboxSessionFactory sessionFactory, ISynchronizationCheckpointStore checkpointStore, IMailSynchronizationUnitOfWorkFactory unitOfWorkFactory, IMessageMetadataRepository metadataRepository, IMessageContentStore contentStore, TimeProvider timeProvider, MailboxSynchronizationOptions options)
    {
        this.sessionFactory = sessionFactory;
        this.checkpointStore = checkpointStore;
        this.unitOfWorkFactory = unitOfWorkFactory;
        this.metadataRepository = metadataRepository;
        this.contentStore = contentStore;
        this.timeProvider = timeProvider;
        this.options = options;
    }

    /// <summary>Synchronizes one account folder without mutating remote IMAP flags.</summary>
    public async Task<MailboxSynchronizationResult> SynchronizeAsync(MailAccountId accountId, MailFolderName folderName, CancellationToken cancellationToken)
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
            await using var unitOfWork = await this.unitOfWorkFactory.BeginSynchronizationWriteAsync(cancellationToken);
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
                    await this.contentStore.SaveContentAsync(unitOfWork, content, cancellationToken);
                    await this.metadataRepository.UpsertMetadataAsync(unitOfWork, metadata, cancellationToken);
                    storedCount++;
                }
                catch (MessageContentTooLargeException)
                {
                    skippedOversizedCount++;
                }
            }

            if (batch.InspectedThroughUid is { } inspectedThroughUid)
            {
                checkpoint = checkpoint.AdvanceTo(inspectedThroughUid, this.timeProvider.GetUtcNow());
                await this.checkpointStore.SaveCheckpointAsync(unitOfWork, accountId, folderName, checkpoint, cancellationToken);
            }

            await unitOfWork.CommitAsync(cancellationToken);
            hasMore = batch.HasMore;
        }

        return new MailboxSynchronizationResult(storedCount, skippedOversizedCount, hasMore, checkpoint);
    }
}

/// <summary>Summarizes one mailbox synchronization run.</summary>
public sealed record MailboxSynchronizationResult(int StoredMessageCount, int SkippedOversizedMessageCount, bool HasMoreMessages, SynchronizationCheckpoint Checkpoint);
