// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.EmailContent;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
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
        var occurrence = EmailOccurrenceId.Create(accountId, folderName, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var contentStore = Substitute.For<IEmailContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
        var metadata = new RemoteEmailMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 128);
        var content = new RemoteEmailContent(occurrence, new ReadOnlyMemory<byte>([1, 2, 3]));
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var metadataStored = false;
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        session.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(content);
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
        Assert.Equal(1, result.StoredEmailCount);
        Assert.Equal(0, result.SkippedOversizedEmailCount);
        await session.Received(1).GetEmailBatchAfterAsync(null, 25, CancellationToken.None);
        await session.Received(1).FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None);
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
        var occurrence = EmailOccurrenceId.Create(accountId, folderName, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var contentStore = Substitute.For<IEmailContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
        var metadata = new RemoteEmailMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 2048);
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.StoredEmailCount);
        Assert.Equal(1, result.SkippedOversizedEmailCount);
        await session.DidNotReceive().FetchEmailContentWithoutSettingSeenAsync(Arg.Any<EmailOccurrenceId>(), Arg.Any<long>(), CancellationToken.None);
        await contentStore.DidNotReceive().SaveContentAsync(Arg.Any<IPersistenceSession>(), Arg.Any<StoredEmailId>(), Arg.Any<RemoteEmailContent>(), CancellationToken.None);
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
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var contentStore = Substitute.For<IEmailContentStore>();
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
        session.GetEmailBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([], firstCursor, HasMore: true));
        session.GetEmailBatchAfterAsync(firstCursor, 25, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([], secondCursor, HasMore: true));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.True(result.HasMoreEmails);
        await session.Received(2).GetEmailBatchAfterAsync(Arg.Any<ImapUid?>(), 25, CancellationToken.None);
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
        var occurrence = EmailOccurrenceId.Create(accountId, folderName, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var contentStore = Substitute.For<IEmailContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024, MaxMetadataBatchesPerRun = 1 };
        var synchronizer = new MailboxSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
        var metadata = new RemoteEmailMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 0);
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        session.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns<Task<RemoteEmailContent>>(_ => throw new EmailContentTooLargeException(occurrence, 2048, 1024));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.StoredEmailCount);
        Assert.Equal(1, result.SkippedOversizedEmailCount);
        await contentStore.DidNotReceive().SaveContentAsync(Arg.Any<IPersistenceSession>(), Arg.Any<StoredEmailId>(), Arg.Any<RemoteEmailContent>(), CancellationToken.None);
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
        var occurrence = EmailOccurrenceId.Create(accountId, folderName, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        var contentStore = Substitute.For<IEmailContentStore>();
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
        var metadata = new RemoteEmailMetadata(occurrence, "message-1@example.test", "Subject", DateTimeOffset.UtcNow, 128);
        var content = new RemoteEmailContent(occurrence, new ReadOnlyMemory<byte>([1, 2, 3]));
        var contentFetched = false;
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        session.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(_ =>
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
        var firstOccurrence = EmailOccurrenceId.Create(accountId, folderName, uidValidity, firstUid);
        var secondOccurrence = EmailOccurrenceId.Create(accountId, folderName, uidValidity, secondUid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var contentStore = Substitute.For<IEmailContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var mailboxSession = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        await using var firstMessageSession = new TrackingSession();
        await using var secondMessageSession = new TrackingSession();
        await using var checkpointSession = new TrackingSession();
        var persistenceSessions = new Queue<IPersistenceSession>(
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
        var firstMetadata = new RemoteEmailMetadata(
            firstOccurrence,
            "message-1@example.test",
            "First",
            new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero),
            128);
        var secondMetadata = new RemoteEmailMetadata(
            secondOccurrence,
            "message-2@example.test",
            "Second",
            new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.Zero),
            128);
        var firstContent = new RemoteEmailContent(firstOccurrence, new ReadOnlyMemory<byte>([1]));
        var secondContent = new RemoteEmailContent(secondOccurrence, new ReadOnlyMemory<byte>([2]));
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(mailboxSession);
        mailboxSession.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        mailboxSession.GetEmailBatchAfterAsync(null, 25, CancellationToken.None).Returns(
            new RemoteEmailMetadataBatch([firstMetadata, secondMetadata], secondUid, HasMore: false));
        mailboxSession.FetchEmailContentWithoutSettingSeenAsync(firstOccurrence, 1024, CancellationToken.None).Returns(firstContent);
        mailboxSession.FetchEmailContentWithoutSettingSeenAsync(secondOccurrence, 1024, CancellationToken.None).Returns(_ =>
        {
            Assert.True(firstMessageSession.IsCommitted);
            Assert.True(firstMessageSession.IsDisposed);
            return secondContent;
        });

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.StoredEmailCount);
        Assert.Empty(persistenceSessions);
        Assert.True(checkpointSession.IsCommitted);
        Assert.True(checkpointSession.IsDisposed);
        await sessionScopeFactory.Received(3).BeginSessionAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_PersistenceConflictThenCommitted_RetriesLocalWriteWithoutRefetchingContent()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = EmailOccurrenceId.Create(accountId, folderName, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var firstAttemptSession = Substitute.For<IPersistenceSession>();
        var secondAttemptSession = Substitute.For<IPersistenceSession>();
        var checkpointSession = Substitute.For<IPersistenceSession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None)
            .Returns(firstAttemptSession, secondAttemptSession, checkpointSession);
        firstAttemptSession.CommitAsync(CancellationToken.None).Returns(PersistenceCommitResult.ConcurrencyConflict);
        secondAttemptSession.CommitAsync(CancellationToken.None).Returns(PersistenceCommitResult.Committed);
        checkpointSession.CommitAsync(CancellationToken.None).Returns(PersistenceCommitResult.Committed);
        var contentStore = Substitute.For<IEmailContentStore>();
        var mailboxSessionFactory = Substitute.For<IMailboxSessionFactory>();
        var mailboxSession = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions
        {
            MaxMetadataBatchSize = 25,
            MaxRawMimeBytes = 1024,
            MaxPersistenceConcurrencyAttempts = 3,
        };
        var synchronizer = new MailboxSynchronizer(
            mailboxSessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        var metadata = new RemoteEmailMetadata(
            occurrence,
            "message-1@example.test",
            "Subject",
            new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero),
            128);
        var content = new RemoteEmailContent(occurrence, new ReadOnlyMemory<byte>([1, 2, 3]));
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None)
            .Returns(SynchronizationCheckpoint.None(uidValidity));
        mailboxSessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(mailboxSession);
        mailboxSession.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        mailboxSession.GetEmailBatchAfterAsync(null, 25, CancellationToken.None)
            .Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        mailboxSession.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(content);
        metadataRepository.UpsertMetadataAsync(
                Arg.Any<IPersistenceSession>(),
                metadata,
                StoredEmailContentAvailability.Available,
                CancellationToken.None)
            .Returns(storedEmailId);

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(MailboxSynchronizationOutcome.Completed, result.Outcome);
        Assert.Equal(1, result.StoredEmailCount);
        await mailboxSession.Received(1)
            .FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None);
        await metadataRepository.Received(1).UpsertMetadataAsync(
            firstAttemptSession,
            metadata,
            StoredEmailContentAvailability.Available,
            CancellationToken.None);
        await metadataRepository.Received(1).UpsertMetadataAsync(
            secondAttemptSession,
            metadata,
            StoredEmailContentAvailability.Available,
            CancellationToken.None);
        await contentStore.Received(1)
            .SaveContentAsync(firstAttemptSession, storedEmailId, content, CancellationToken.None);
        await contentStore.Received(1)
            .SaveContentAsync(secondAttemptSession, storedEmailId, content, CancellationToken.None);
        await sessionScopeFactory.Received(3).BeginSessionAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_PersistenceConflictsExhausted_ReturnsConflictWithoutAdvancingCheckpoint()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = EmailOccurrenceId.Create(accountId, folderName, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var attemptSessions = Enumerable.Range(0, 3)
            .Select(_ => Substitute.For<IPersistenceSession>())
            .ToArray();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None)
            .Returns(attemptSessions[0], attemptSessions[1], attemptSessions[2]);
        foreach (var attemptSession in attemptSessions)
        {
            attemptSession.CommitAsync(CancellationToken.None)
                .Returns(PersistenceCommitResult.ConcurrencyConflict);
        }

        var contentStore = Substitute.For<IEmailContentStore>();
        var mailboxSessionFactory = Substitute.For<IMailboxSessionFactory>();
        var mailboxSession = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions
        {
            MaxMetadataBatchSize = 25,
            MaxRawMimeBytes = 1024,
            MaxPersistenceConcurrencyAttempts = 3,
        };
        var synchronizer = new MailboxSynchronizer(
            mailboxSessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        var metadata = new RemoteEmailMetadata(
            occurrence,
            "message-1@example.test",
            "Subject",
            new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero),
            128);
        var content = new RemoteEmailContent(occurrence, new ReadOnlyMemory<byte>([1, 2, 3]));
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var initialCheckpoint = SynchronizationCheckpoint.None(uidValidity);
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None)
            .Returns(initialCheckpoint);
        mailboxSessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None).Returns(mailboxSession);
        mailboxSession.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        mailboxSession.GetEmailBatchAfterAsync(null, 25, CancellationToken.None)
            .Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        mailboxSession.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(content);
        metadataRepository.UpsertMetadataAsync(
                Arg.Any<IPersistenceSession>(),
                metadata,
                StoredEmailContentAvailability.Available,
                CancellationToken.None)
            .Returns(storedEmailId);

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(MailboxSynchronizationOutcome.ConcurrencyConflict, result.Outcome);
        Assert.Equal(0, result.StoredEmailCount);
        Assert.True(result.HasMoreEmails);
        Assert.Same(initialCheckpoint, result.Checkpoint);
        await mailboxSession.Received(1)
            .FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None);
        await sessionScopeFactory.Received(3).BeginSessionAsync(CancellationToken.None);
        await checkpointStore.DidNotReceive().SaveCheckpointAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderName>(),
            Arg.Any<SynchronizationCheckpoint>(),
            Arg.Any<CancellationToken>());
        foreach (var attemptSession in attemptSessions)
        {
            await contentStore.Received(1)
                .SaveContentAsync(attemptSession, storedEmailId, content, CancellationToken.None);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_CheckpointCommitConflict_DoesNotRetryStaleCheckpoint()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(5);
        var inspectedThroughUid = ImapUid.Create(10);
        var initialCheckpoint = SynchronizationCheckpoint.None(uidValidity);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        persistenceSession.CommitAsync(CancellationToken.None)
            .Returns(PersistenceCommitResult.ConcurrencyConflict);
        var contentStore = Substitute.For<IEmailContentStore>();
        var mailboxSessionFactory = Substitute.For<IMailboxSessionFactory>();
        var mailboxSession = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions
        {
            MaxMetadataBatchSize = 25,
            MaxRawMimeBytes = 1024,
            MaxPersistenceConcurrencyAttempts = 3,
        };
        var synchronizer = new MailboxSynchronizer(
            mailboxSessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        checkpointStore.GetCheckpointAsync(accountId, folderName, CancellationToken.None)
            .Returns(initialCheckpoint);
        mailboxSessionFactory.OpenReadOnlyAsync(accountId, folderName, CancellationToken.None)
            .Returns(mailboxSession);
        mailboxSession.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        mailboxSession.GetEmailBatchAfterAsync(null, 25, CancellationToken.None)
            .Returns(new RemoteEmailMetadataBatch([], inspectedThroughUid, HasMore: false));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(MailboxSynchronizationOutcome.ConcurrencyConflict, result.Outcome);
        Assert.True(result.HasMoreEmails);
        Assert.Same(initialCheckpoint, result.Checkpoint);
        await sessionScopeFactory.Received(1).BeginSessionAsync(CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(
            persistenceSession,
            accountId,
            folderName,
            Arg.Is<SynchronizationCheckpoint>(
                checkpoint => checkpoint!.LastSeenUid == inspectedThroughUid),
            CancellationToken.None);
        await persistenceSession.Received(1).CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_EmptyUnassignedMailboxWindow_DoesNotPersistSpeculativeCheckpoint()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var uidValidity = ImapUidValidity.Create(5);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var contentStore = Substitute.For<IEmailContentStore>();
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
        session.GetEmailBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([], null, HasMore: false));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Null(result.Checkpoint.LastSeenUid);
        await sessionScopeFactory.DidNotReceive().BeginSessionAsync(CancellationToken.None);
        await checkpointStore.DidNotReceive().SaveCheckpointAsync(Arg.Any<IPersistenceSession>(), accountId, folderName, Arg.Any<SynchronizationCheckpoint>(), CancellationToken.None);
    }

    [Fact]
    public void EmailContentTooLargeException_Constructors_PreserveSafeFailureDetails()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folderName = MailFolderName.Create("INBOX");
        var occurrence = EmailOccurrenceId.Create(accountId, folderName, ImapUidValidity.Create(5), ImapUid.Create(10));
        var inner = new InvalidOperationException("inner");

        // Act
        var empty = new EmailContentTooLargeException();
        var withMessage = new EmailContentTooLargeException("safe message");
        var withInner = new EmailContentTooLargeException("safe wrapper", inner);
        var withOccurrence = new EmailContentTooLargeException(occurrence, 2048, 1024);

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
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var contentStore = Substitute.For<IEmailContentStore>();
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
        session.GetEmailBatchAfterAsync(null, 25, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([], reassignedUid, HasMore: false));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, folderName, CancellationToken.None);

        // Assert
        Assert.Equal(currentUidValidity, result.Checkpoint.UidValidity);
        await session.Received(1).GetEmailBatchAfterAsync(null, 25, CancellationToken.None);
        await session.DidNotReceive().GetEmailBatchAfterAsync(staleCheckpoint.LastSeenUid, 25, CancellationToken.None);
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
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var contentStore = Substitute.For<IEmailContentStore>();
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
        session.GetEmailBatchAfterAsync(null, 25, cancellation.Token).Returns<RemoteEmailMetadataBatch>(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        // Act
        await Assert.ThrowsAsync<OperationCanceledException>(() => synchronizer.SynchronizeAsync(accountId, folderName, cancellation.Token));

        // Assert
        await sessionScopeFactory.DidNotReceive().BeginSessionAsync(Arg.Any<CancellationToken>());
        await checkpointStore.DidNotReceive().SaveCheckpointAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderName>(),
            Arg.Any<SynchronizationCheckpoint>(),
            Arg.Any<CancellationToken>());
        await session.Received(1).DisposeAsync();
    }

    private sealed class TrackingSession : IPersistenceSession
    {
        public bool IsCommitted { get; private set; }

        public bool IsDisposed { get; private set; }

        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken)
        {
            this.IsCommitted = true;
            return Task.FromResult(PersistenceCommitResult.Committed);
        }

        public ValueTask DisposeAsync()
        {
            this.IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

}
