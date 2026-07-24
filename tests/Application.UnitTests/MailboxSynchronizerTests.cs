// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.MessageContent;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Messages;
using MailMcp.Domain.Synchronization;
using NSubstitute;

namespace MailMcp.Application.UnitTests;

public sealed class MailboxSynchronizerTests
{
    [Fact]
    public async Task SynchronizeAsync_NewMessage_StoresContentBeforeAdvancingCheckpoint()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = MessageOccurrenceId.Create(accountId, folderName, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IMessageMetadataRepository>();
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IImapMailboxSessionFactory>();
        var session = Substitute.For<IImapMailboxSession>();
        var clock = Substitute.For<TimeProvider>();
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, metadataRepository, contentStore, clock);
        var metadata = new RemoteMessageMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 128);
        var content = new RemoteMessageContent(occurrence, new byte[] { 1, 2, 3 });
        clock.GetUtcNow().Returns(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetMessagesAfterAsync(null, CancellationToken.None).Returns([metadata]);
        session.FetchMessageContentWithoutSettingSeenAsync(occurrence, CancellationToken.None).Returns(content);

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.StoredMessageCount);
        await session.Received(1).FetchMessageContentWithoutSettingSeenAsync(occurrence, CancellationToken.None);
        Received.InOrder(() =>
        {
            contentStore.SaveContentAsync(content, CancellationToken.None);
            metadataRepository.UpsertMetadataAsync(metadata, CancellationToken.None);
            checkpointStore.SaveCheckpointAsync(accountId, folderName, Arg.Is<SynchronizationCheckpoint>(c => c.LastSeenUid == uid), CancellationToken.None);
        });
    }
}
