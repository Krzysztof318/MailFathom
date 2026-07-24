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

    /// <summary>Initializes a new mailbox synchronizer.</summary>
    public MailboxSynchronizer(IImapMailboxSessionFactory sessionFactory, ISynchronizationCheckpointStore checkpointStore, IMessageMetadataRepository metadataRepository, IMessageContentStore contentStore, TimeProvider timeProvider)
    {
        this.sessionFactory = sessionFactory;
        this.checkpointStore = checkpointStore;
        this.metadataRepository = metadataRepository;
        this.contentStore = contentStore;
        this.timeProvider = timeProvider;
    }

    /// <summary>Synchronizes one account folder without mutating remote IMAP flags.</summary>
    public async Task<MailboxSynchronizationResult> SynchronizeAsync(MailAccountId accountId, MailFolderName folderName, CancellationToken cancellationToken)
    {
        var checkpoint = await checkpointStore.GetCheckpointAsync(accountId, folderName, cancellationToken);
        await using var session = await sessionFactory.OpenReadOnlyAsync(accountId, folderName, cancellationToken);
        var uidValidity = await session.GetUidValidityAsync(cancellationToken);
        checkpoint = checkpoint?.UidValidity == uidValidity ? checkpoint : SynchronizationCheckpoint.None(uidValidity);
        var messages = await session.GetMessagesAfterAsync(checkpoint.LastSeenUid, cancellationToken);
        var storedCount = 0;

        foreach (var metadata in messages.OrderBy(message => message.OccurrenceId.Uid.Value))
        {
            var content = await session.FetchMessageContentWithoutSettingSeenAsync(metadata.OccurrenceId, cancellationToken);
            await contentStore.SaveContentAsync(content, cancellationToken);
            await metadataRepository.UpsertMetadataAsync(metadata, cancellationToken);
            checkpoint = checkpoint.AdvanceTo(metadata.OccurrenceId.Uid, timeProvider.GetUtcNow());
            await checkpointStore.SaveCheckpointAsync(accountId, folderName, checkpoint, cancellationToken);
            storedCount++;
        }

        return new MailboxSynchronizationResult(storedCount, checkpoint);
    }
}

/// <summary>Summarizes one mailbox synchronization run.</summary>
public sealed record MailboxSynchronizationResult(int StoredMessageCount, SynchronizationCheckpoint Checkpoint);
