// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.MessageContent;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Messages;
using MailMcp.Domain.Synchronization;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailMcp.Application.UnitTests;

public sealed class MailboxSynchronizerTests
{
    [Fact]
    public async Task SynchronizeAsync_NewMessage_UsesStoredEmailIdForContentBeforeAdvancingCheckpoint()
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
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
        var metadata = new RemoteMessageMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 128);
        var content = new RemoteMessageContent(occurrence, new ReadOnlyMemory<byte>([1, 2, 3]));
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var metadataStored = false;
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetMessageBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteMessageMetadataBatch([metadata], uid, HasMore: false));
        session.FetchMessageContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(content);
        metadataRepository.UpsertMetadataAsync(persistenceSession, metadata, StoredEmailContentAvailability.Available, CancellationToken.None).Returns(_ =>
        {
            metadataStored = true;
            return storedEmailId;
        });
        contentStore.SaveContentAsync(persistenceSession, storedEmailId, content, CancellationToken.None).Returns(_ =>
        {
            Assert.True(metadataStored);
            return Task.CompletedTask;
        });

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.StoredMessageCount);
        Assert.Equal(0, result.SkippedOversizedMessageCount);
        await session.Received(1).GetMessageBatchAfterAsync(null, 25, CancellationToken.None);
        await session.Received(1).FetchMessageContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None);
        await metadataRepository.Received(1).UpsertMetadataAsync(persistenceSession, metadata, StoredEmailContentAvailability.Available, CancellationToken.None);
        await contentStore.Received(1).SaveContentAsync(persistenceSession, storedEmailId, content, CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folderName, Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == uid), CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_OversizedMessage_RecordsOccurrenceWithoutContentAndAdvancesCheckpoint()
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
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
        var metadata = new RemoteMessageMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 2048);
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
        await contentStore.DidNotReceive().SaveContentAsync(Arg.Any<ISession>(), Arg.Any<StoredEmailId>(), Arg.Any<RemoteMessageContent>(), CancellationToken.None);
        await metadataRepository.Received(1).UpsertMetadataAsync(persistenceSession, metadata, StoredEmailContentAvailability.ExceededSizeLimit, CancellationToken.None);
        await persistenceSession.Received(2).CommitAsync(CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folderName, Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == uid), CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_HasMoreAfterBatchLimit_ReturnsRemainingWorkWithoutUnboundedLoop()
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
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024, MaxMetadataBatchesPerRun = 2 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
        var firstCursor = ImapUid.Create(25);
        var secondCursor = ImapUid.Create(50);
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
    public async Task SynchronizeAsync_ContentStreamExceedsLimit_RecordsOccurrenceWithoutContentAndAdvancesCheckpoint()
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
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024, MaxMetadataBatchesPerRun = 1 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
        var metadata = new RemoteMessageMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 0);
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
        await contentStore.DidNotReceive().SaveContentAsync(Arg.Any<ISession>(), Arg.Any<StoredEmailId>(), Arg.Any<RemoteMessageContent>(), CancellationToken.None);
        await metadataRepository.Received(1).UpsertMetadataAsync(persistenceSession, metadata, StoredEmailContentAvailability.ExceededSizeLimit, CancellationToken.None);
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
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
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
        await sessionScopeFactory.Received(2).BeginSessionAsync(CancellationToken.None);
        await contentStore.Received(1).SaveContentAsync(persistenceSession, Arg.Any<StoredEmailId>(), content, CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_MultipleMessages_CommitsAndDisposesEachMessageBeforeFetchingTheNext()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(5);
        var firstUid = ImapUid.Create(10);
        var secondUid = ImapUid.Create(11);
        var firstOccurrence = MessageOccurrenceId.Create(accountId, folderName, uidValidity, firstUid);
        var secondOccurrence = MessageOccurrenceId.Create(accountId, folderName, uidValidity, secondUid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IMessageMetadataRepository>();
        var sessionScopeFactory = Substitute.For<ISessionFactory>();
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var mailboxSession = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        await using var firstMessageSession = new TrackingSession();
        await using var secondMessageSession = new TrackingSession();
        await using var checkpointSession = new TrackingSession();
        var persistenceSessions = new Queue<ISession>(
        [
            firstMessageSession,
            secondMessageSession,
            checkpointSession,
        ]);
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(_ =>
        {
            var persistenceSession = persistenceSessions.Dequeue();
            if (ReferenceEquals(persistenceSession, checkpointSession))
            {
                Assert.True(secondMessageSession.IsCommitted);
                Assert.True(secondMessageSession.IsDisposed);
            }

            return persistenceSession;
        });
        var options = new MailboxSynchronizationOptions
        {
            MaxMetadataBatchSize = 25,
            MaxRawMimeBytes = 1024,
            MaxMetadataBatchesPerRun = 1,
        };
        var synchronizer = new MailboxSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        var firstMetadata = new RemoteMessageMetadata(
            firstOccurrence,
            "message-1@example.test",
            "First",
            new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero),
            128);
        var secondMetadata = new RemoteMessageMetadata(
            secondOccurrence,
            "message-2@example.test",
            "Second",
            new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.Zero),
            128);
        var firstContent = new RemoteMessageContent(firstOccurrence, new ReadOnlyMemory<byte>([1]));
        var secondContent = new RemoteMessageContent(secondOccurrence, new ReadOnlyMemory<byte>([2]));
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(mailboxSession);
        mailboxSession.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        mailboxSession.GetMessageBatchAfterAsync(null, 25, CancellationToken.None).Returns(
            new RemoteMessageMetadataBatch([firstMetadata, secondMetadata], secondUid, HasMore: false));
        mailboxSession.FetchMessageContentWithoutSettingSeenAsync(firstOccurrence, 1024, CancellationToken.None).Returns(firstContent);
        mailboxSession.FetchMessageContentWithoutSettingSeenAsync(secondOccurrence, 1024, CancellationToken.None).Returns(_ =>
        {
            Assert.True(firstMessageSession.IsCommitted);
            Assert.True(firstMessageSession.IsDisposed);
            return secondContent;
        });

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.StoredMessageCount);
        Assert.Empty(persistenceSessions);
        Assert.True(checkpointSession.IsCommitted);
        Assert.True(checkpointSession.IsDisposed);
        await sessionScopeFactory.Received(3).BeginSessionAsync(CancellationToken.None);
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
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
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

    [Fact]
    public void MessageContentTooLargeException_Constructors_PreserveSafeFailureDetails()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var occurrence = MessageOccurrenceId.Create(accountId, folderName, ImapUidValidity.Create(5), ImapUid.Create(10));
        var inner = new InvalidOperationException("inner");

        // Act
        var empty = new MessageContentTooLargeException();
        var withMessage = new MessageContentTooLargeException("safe message");
        var withInner = new MessageContentTooLargeException("safe wrapper", inner);
        var withOccurrence = new MessageContentTooLargeException(occurrence, 2048, 1024);

        // Assert
        Assert.Null(empty.OccurrenceId);
        Assert.Equal("safe message", withMessage.Message);
        Assert.Same(inner, withInner.InnerException);
        Assert.Equal(occurrence, withOccurrence.OccurrenceId);
        Assert.Equal(2048, withOccurrence.SizeOctets);
        Assert.Equal(1024, withOccurrence.MaxAllowedOctets);
        Assert.Contains("primary/INBOX/5/10", withOccurrence.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SynchronizeAsync_UidValidityChanged_DiscardsStaleCheckpointAndRestartsFromFirstUid()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var staleUidValidity = ImapUidValidity.Create(5);
        var currentUidValidity = ImapUidValidity.Create(6);
        var staleCheckpoint = new SynchronizationCheckpoint(
            staleUidValidity,
            ImapUid.Create(999),
            new DateTimeOffset(2026, 7, 23, 12, 0, 0, TimeSpan.Zero));
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IMessageMetadataRepository>();
        var sessionScopeFactory = Substitute.For<ISessionFactory>();
        var persistenceSession = Substitute.For<ISession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = new MailboxSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        var reassignedUid = ImapUid.Create(1);
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(staleCheckpoint);
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(currentUidValidity);
        session.GetMessageBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteMessageMetadataBatch([], reassignedUid, HasMore: false));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(currentUidValidity, result.Checkpoint.UidValidity);
        await session.Received(1).GetMessageBatchAfterAsync(null, 25, CancellationToken.None);
        await session.DidNotReceive().GetMessageBatchAfterAsync(staleCheckpoint.LastSeenUid, 25, CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(
            persistenceSession,
            accountId,
            folderName,
            Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.UidValidity == currentUidValidity && checkpoint.LastSeenUid == reassignedUid),
            CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_CancellationRequestedDuringBatch_PropagatesCancellationWithoutWritingProgress()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(5);
        using var cancellation = new CancellationTokenSource();
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IMessageMetadataRepository>();
        var sessionScopeFactory = Substitute.For<ISessionFactory>();
        var contentStore = Substitute.For<IMessageContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = new MailboxSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        checkpointStore.GetCheckpointAsync(accountId, folderName, cancellation.Token).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, cancellation.Token).Returns(session);
        session.GetUidValidityAsync(cancellation.Token).Returns(uidValidity);
        session.GetMessageBatchAfterAsync(null, 25, cancellation.Token).Returns<RemoteMessageMetadataBatch>(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        // Act
        await Assert.ThrowsAsync<OperationCanceledException>(() => synchronizer.SynchronizeAsync(accountId, folderName, cancellation.Token));

        // Assert
        await sessionScopeFactory.DidNotReceive().BeginSessionAsync(Arg.Any<CancellationToken>());
        await checkpointStore.DidNotReceive().SaveCheckpointAsync(
            Arg.Any<ISession>(),
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderName>(),
            Arg.Any<SynchronizationCheckpoint>(),
            Arg.Any<CancellationToken>());
        await session.Received(1).DisposeAsync();
    }

    private sealed class TrackingSession : ISession
    {
        public bool IsCommitted { get; private set; }

        public bool IsDisposed { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken)
        {
            this.IsCommitted = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            this.IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

}
