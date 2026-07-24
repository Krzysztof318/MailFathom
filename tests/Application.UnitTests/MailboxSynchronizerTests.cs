// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.MessageContent;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Messages;
using MailMcp.Domain.Synchronization;
using NSubstitute;
using Xunit;

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
        var unitOfWorkFactory = Substitute.For<IMailSynchronizationUnitOfWorkFactory>();
        var unitOfWork = Substitute.For<IMailSynchronizationUnitOfWorkSession>();
        unitOfWorkFactory.BeginSynchronizationWriteAsync(CancellationToken.None).Returns(unitOfWork);
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IImapMailboxSessionFactory>();
        var session = Substitute.For<IImapMailboxSession>();
        var clock = Substitute.For<TimeProvider>();
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, unitOfWorkFactory, metadataRepository, contentStore, clock, options);
        var metadata = new RemoteMessageMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 128);
        var content = new RemoteMessageContent(occurrence, new ReadOnlyMemory<byte>([1, 2, 3]));
        clock.GetUtcNow().Returns(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetMessageBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteMessageMetadataBatch([metadata], uid, HasMore: false));
        session.FetchMessageContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(content);

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.StoredMessageCount);
        Assert.Equal(0, result.SkippedOversizedMessageCount);
        await session.Received(1).GetMessageBatchAfterAsync(null, 25, CancellationToken.None);
        await session.Received(1).FetchMessageContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None);
        await contentStore.Received(1).SaveContentAsync(unitOfWork, content, CancellationToken.None);
        await metadataRepository.Received(1).UpsertMetadataAsync(unitOfWork, metadata, CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(unitOfWork, accountId, folderName, Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == uid), CancellationToken.None);
        await unitOfWork.Received(1).CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_OversizedMessage_SkipsContentFetchAndAdvancesCheckpoint()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = MessageOccurrenceId.Create(accountId, folderName, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IMessageMetadataRepository>();
        var unitOfWorkFactory = Substitute.For<IMailSynchronizationUnitOfWorkFactory>();
        var unitOfWork = Substitute.For<IMailSynchronizationUnitOfWorkSession>();
        unitOfWorkFactory.BeginSynchronizationWriteAsync(CancellationToken.None).Returns(unitOfWork);
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IImapMailboxSessionFactory>();
        var session = Substitute.For<IImapMailboxSession>();
        var clock = Substitute.For<TimeProvider>();
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, unitOfWorkFactory, metadataRepository, contentStore, clock, options);
        var metadata = new RemoteMessageMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 2048);
        clock.GetUtcNow().Returns(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetMessageBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteMessageMetadataBatch([metadata], uid, HasMore: false));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.StoredMessageCount);
        Assert.Equal(1, result.SkippedOversizedMessageCount);
        await session.DidNotReceive().FetchMessageContentWithoutSettingSeenAsync(Arg.Any<MessageOccurrenceId>(), Arg.Any<long>(), CancellationToken.None);
        await contentStore.DidNotReceive().SaveContentAsync(Arg.Any<IMailSynchronizationUnitOfWorkSession>(), Arg.Any<RemoteMessageContent>(), CancellationToken.None);
        await metadataRepository.DidNotReceive().UpsertMetadataAsync(Arg.Any<IMailSynchronizationUnitOfWorkSession>(), Arg.Any<RemoteMessageMetadata>(), CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(unitOfWork, accountId, folderName, Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == uid), CancellationToken.None);
        await unitOfWork.Received(1).CommitAsync(CancellationToken.None);
    }


    [Fact]
    public async Task SynchronizeAsync_HasMoreAfterWindowLimit_ReturnsRemainingWorkWithoutUnboundedLoop()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(5);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IMessageMetadataRepository>();
        var unitOfWorkFactory = Substitute.For<IMailSynchronizationUnitOfWorkFactory>();
        var unitOfWork = Substitute.For<IMailSynchronizationUnitOfWorkSession>();
        unitOfWorkFactory.BeginSynchronizationWriteAsync(CancellationToken.None).Returns(unitOfWork);
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IImapMailboxSessionFactory>();
        var session = Substitute.For<IImapMailboxSession>();
        var clock = Substitute.For<TimeProvider>();
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024, MaxUidWindowsPerRun = 2 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, unitOfWorkFactory, metadataRepository, contentStore, clock, options);
        var firstCursor = ImapUid.Create(25);
        var secondCursor = ImapUid.Create(50);
        clock.GetUtcNow().Returns(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetMessageBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteMessageMetadataBatch([], firstCursor, HasMore: true));
        session.GetMessageBatchAfterAsync(firstCursor, 25, CancellationToken.None).Returns(new RemoteMessageMetadataBatch([], secondCursor, HasMore: true));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.True(result.HasMoreMessages);
        await session.Received(2).GetMessageBatchAfterAsync(Arg.Any<ImapUid?>(), 25, CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(unitOfWork, accountId, folderName, Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == firstCursor), CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(unitOfWork, accountId, folderName, Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == secondCursor), CancellationToken.None);
        await unitOfWork.Received(2).CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_ContentStreamExceedsLimit_SkipsMessageAndAdvancesCheckpoint()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = MessageOccurrenceId.Create(accountId, folderName, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IMessageMetadataRepository>();
        var unitOfWorkFactory = Substitute.For<IMailSynchronizationUnitOfWorkFactory>();
        var unitOfWork = Substitute.For<IMailSynchronizationUnitOfWorkSession>();
        unitOfWorkFactory.BeginSynchronizationWriteAsync(CancellationToken.None).Returns(unitOfWork);
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IImapMailboxSessionFactory>();
        var session = Substitute.For<IImapMailboxSession>();
        var clock = Substitute.For<TimeProvider>();
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024, MaxUidWindowsPerRun = 1 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, unitOfWorkFactory, metadataRepository, contentStore, clock, options);
        var metadata = new RemoteMessageMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 0);
        clock.GetUtcNow().Returns(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetMessageBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteMessageMetadataBatch([metadata], uid, HasMore: false));
        session.FetchMessageContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns<Task<RemoteMessageContent>>(_ => throw new MessageContentTooLargeException(occurrence, 2048, 1024));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.StoredMessageCount);
        Assert.Equal(1, result.SkippedOversizedMessageCount);
        await contentStore.DidNotReceive().SaveContentAsync(Arg.Any<IMailSynchronizationUnitOfWorkSession>(), Arg.Any<RemoteMessageContent>(), CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(unitOfWork, accountId, folderName, Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == uid), CancellationToken.None);
        await unitOfWork.Received(1).CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_EmptyFolderWithoutCheckpoint_DoesNotAdvanceCheckpointIntoFutureUidSpace()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(5);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IMessageMetadataRepository>();
        var unitOfWorkFactory = Substitute.For<IMailSynchronizationUnitOfWorkFactory>();
        var unitOfWork = Substitute.For<IMailSynchronizationUnitOfWorkSession>();
        unitOfWorkFactory.BeginSynchronizationWriteAsync(CancellationToken.None).Returns(unitOfWork);
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IImapMailboxSessionFactory>();
        var session = Substitute.For<IImapMailboxSession>();
        var clock = Substitute.For<TimeProvider>();
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024, MaxUidWindowsPerRun = 1 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, unitOfWorkFactory, metadataRepository, contentStore, clock, options);
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetMessageBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteMessageMetadataBatch([], null, HasMore: false));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Null(result.Checkpoint.LastSeenUid);
        await checkpointStore.DidNotReceive().SaveCheckpointAsync(Arg.Any<IMailSynchronizationUnitOfWorkSession>(), Arg.Any<MailAccountId>(), Arg.Any<MailFolderName>(), Arg.Any<SynchronizationCheckpoint>(), CancellationToken.None);
        await unitOfWork.Received(1).CommitAsync(CancellationToken.None);
    }

}
