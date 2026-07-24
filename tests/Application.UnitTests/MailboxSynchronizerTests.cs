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
        var sessionScopeFactory = Substitute.For<ISessionFactory>();
        var persistenceSession = Substitute.For<ISession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = Substitute.For<TimeProvider>();
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
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
        await contentStore.Received(1).SaveContentAsync(persistenceSession, content, CancellationToken.None);
        await metadataRepository.Received(1).UpsertMetadataAsync(persistenceSession, metadata, CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folderName, Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == uid), CancellationToken.None);
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
        var sessionScopeFactory = Substitute.For<ISessionFactory>();
        var persistenceSession = Substitute.For<ISession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = Substitute.For<TimeProvider>();
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
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
        await contentStore.DidNotReceive().SaveContentAsync(Arg.Any<ISession>(), Arg.Any<RemoteMessageContent>(), CancellationToken.None);
        await metadataRepository.DidNotReceive().UpsertMetadataAsync(Arg.Any<ISession>(), Arg.Any<RemoteMessageMetadata>(), CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folderName, Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == uid), CancellationToken.None);
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
        var sessionScopeFactory = Substitute.For<ISessionFactory>();
        var persistenceSession = Substitute.For<ISession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = Substitute.For<TimeProvider>();
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024, MaxUidWindowsPerRun = 2 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
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
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folderName, Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == firstCursor), CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folderName, Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == secondCursor), CancellationToken.None);
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
        var sessionScopeFactory = Substitute.For<ISessionFactory>();
        var persistenceSession = Substitute.For<ISession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = Substitute.For<TimeProvider>();
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024, MaxUidWindowsPerRun = 1 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
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
        await contentStore.DidNotReceive().SaveContentAsync(Arg.Any<ISession>(), Arg.Any<RemoteMessageContent>(), CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folderName, Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == uid), CancellationToken.None);
    }
    [Fact]
    public async Task SynchronizeAsync_NewMessage_FetchesRemoteContentBeforeOpeningPersistenceSession()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = MessageOccurrenceId.Create(accountId, folderName, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IMessageMetadataRepository>();
        var sessionScopeFactory = Substitute.For<ISessionFactory>();
        var persistenceSession = Substitute.For<ISession>();
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = Substitute.For<TimeProvider>();
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = new MailboxSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        var metadata = new RemoteMessageMetadata(occurrence, "message-1@example.test", "Subject", DateTimeOffset.UtcNow, 128);
        var content = new RemoteMessageContent(occurrence, new ReadOnlyMemory<byte>([1, 2, 3]));
        var contentFetched = false;
        clock.GetUtcNow().Returns(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetMessageBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteMessageMetadataBatch([metadata], uid, HasMore: false));
        session.FetchMessageContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(_ =>
        {
            contentFetched = true;
            return content;
        });
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(_ =>
        {
            Assert.True(contentFetched);
            return persistenceSession;
        });

        // Act
        await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        await sessionScopeFactory.Received(1).BeginSessionAsync(CancellationToken.None);
        await contentStore.Received(1).SaveContentAsync(persistenceSession, content, CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_EmptyUnassignedMailboxWindow_DoesNotPersistSpeculativeCheckpoint()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(5);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IMessageMetadataRepository>();
        var sessionScopeFactory = Substitute.For<ISessionFactory>();
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = Substitute.For<TimeProvider>();
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = new MailboxSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetMessageBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteMessageMetadataBatch([], null, HasMore: false));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Null(result.Checkpoint.LastSeenUid);
        await sessionScopeFactory.DidNotReceive().BeginSessionAsync(CancellationToken.None);
        await checkpointStore.DidNotReceive().SaveCheckpointAsync(Arg.Any<ISession>(), accountId, folderName, Arg.Any<SynchronizationCheckpoint>(), CancellationToken.None);
    }

}
