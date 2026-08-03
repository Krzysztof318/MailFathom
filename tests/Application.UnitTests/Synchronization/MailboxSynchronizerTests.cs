// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Domain.Transport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Synchronization;

public sealed class MailboxSynchronizerTests
{
    private static readonly MailFolderAlias InboxAlias = MailFolderAlias.Create("inbox");

    private static readonly RemoteFolderPath InboxRemotePath = RemoteFolderPath.Create("INBOX", '/');

    private static readonly MailFolderMapping InboxMapping =
        MailFolderMapping.ToSpecialUse(InboxAlias, MailFolderSpecialUse.Inbox);

    private static readonly MailFolderResolution InboxFolder =
        MailFolderResolution.FirstBindingOf(InboxAlias, InboxRemotePath);

    private static readonly MailTransportSecurityPolicy RequiredTlsPolicy = MailTransportSecurityPolicy.Create(
        MailConnectionSecurity.TlsOnConnect,
        MailAuthenticationPolicy.Create(
            [MailAuthenticationMechanism.Plain],
            allowInsecureConnection: false,
            allowClearTextAuthenticationOverUnencryptedConnection: false),
        MailServerCertificateTrust.SystemTrustStore,
        trustedCertificateAuthorityReference: null);

    [Fact]
    public async Task SynchronizeAsync_NewMessage_UsesStoredEmailIdForContentBeforeAdvancingCheckpoint()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, uid);
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
        var synchronizer = CreateSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
        var metadata = new RemoteEmailMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 128);
        var content = new RemoteEmailContent(occurrence, new ReadOnlyMemory<byte>([1, 2, 3]));
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var metadataStored = false;
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        session.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(RemoteEmailContentFetchResult.Retrieved(content));
        metadataRepository.UpsertMetadataAsync(persistenceSession, metadata, Arg.Any<ExtractedEmailMetadata?>(), StoredEmailContentAvailability.Available, CancellationToken.None).Returns(_ =>
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
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.StoredEmailCount);
        Assert.Equal(0, result.SkippedOversizedEmailCount);
        await session.Received(1).GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None);
        await session.Received(1).FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None);
        await metadataRepository.Received(1).UpsertMetadataAsync(persistenceSession, metadata, Arg.Any<ExtractedEmailMetadata?>(), StoredEmailContentAvailability.Available, CancellationToken.None);
        await contentStore.Received(1).SaveContentAsync(persistenceSession, storedEmailId, content, CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folder.Id, Arg.Any<SynchronizationCheckpoint?>(), Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == uid), CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_OversizedMessage_RecordsOccurrenceWithoutContentAndAdvancesCheckpoint()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, uid);
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
        var synchronizer = CreateSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
        var metadata = new RemoteEmailMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 2048);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.StoredEmailCount);
        Assert.Equal(1, result.SkippedOversizedEmailCount);
        await session.DidNotReceive().FetchEmailContentWithoutSettingSeenAsync(Arg.Any<EmailOccurrenceId>(), Arg.Any<long>(), CancellationToken.None);
        await contentStore.DidNotReceive().SaveContentAsync(Arg.Any<IPersistenceSession>(), Arg.Any<StoredEmailId>(), Arg.Any<RemoteEmailContent>(), CancellationToken.None);
        await metadataRepository.Received(1).UpsertMetadataAsync(persistenceSession, metadata, null, StoredEmailContentAvailability.ExceededSizeLimit, CancellationToken.None);
        await persistenceSession.Received(2).CommitAsync(CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folder.Id, Arg.Any<SynchronizationCheckpoint?>(), Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == uid), CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_HasMoreAfterBatchLimit_ReturnsRemainingWorkWithoutUnboundedLoop()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
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
        var synchronizer = CreateSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
        var firstCursor = ImapUid.Create(25);
        var secondCursor = ImapUid.Create(50);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([], firstCursor, HasMore: true));
        session.GetEmailBatchAfterAsync(firstCursor, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([], secondCursor, HasMore: true));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.True(result.HasMoreEmails);
        await session.Received(2).GetEmailBatchAfterAsync(Arg.Any<ImapUid?>(), 25, MailSynchronizationWindow.Unbounded, CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folder.Id, Arg.Any<SynchronizationCheckpoint?>(), Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == firstCursor), CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folder.Id, Arg.Any<SynchronizationCheckpoint?>(), Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == secondCursor), CancellationToken.None);
    }

    /// <summary>The configured bound must reach the server, because filtering after a fetch would defeat the point of it.</summary>
    [Fact]
    public async Task SynchronizeAsync_AccountBoundsHowFarBackToReach_RequestsEveryBatchUnderThatBound()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, uid);
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
        var window = MailSynchronizationWindow.EmailsReceivedSince(new DateOnly(2024, 1, 1));
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options,
            synchronizationWindow: window);
        var metadata = new RemoteEmailMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 128);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, window, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        session.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(
            RemoteEmailContentFetchResult.Retrieved(new RemoteEmailContent(occurrence, new ReadOnlyMemory<byte>([1, 2, 3]))));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.StoredEmailCount);
        await session.Received(1).GetEmailBatchAfterAsync(null, 25, window, CancellationToken.None);
        await session.DidNotReceive().GetEmailBatchAfterAsync(Arg.Any<ImapUid?>(), Arg.Any<int>(), MailSynchronizationWindow.Unbounded, CancellationToken.None);
    }

    /// <summary>An excluded range must be stepped over once; rescanning it every interval is what a bound is supposed to avoid.</summary>
    [Fact]
    public async Task SynchronizeAsync_BoundExcludesEveryEmailInTheRange_AdvancesTheCheckpointPastItAndEndsTheRun()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var inspectedThroughUid = ImapUid.Create(4000);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var contentStore = Substitute.For<IEmailContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024, MaxMetadataBatchesPerRun = 5 };
        var window = MailSynchronizationWindow.EmailsReceivedSince(new DateOnly(2026, 1, 1));
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options,
            synchronizationWindow: window);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, window, CancellationToken.None).Returns(
            new RemoteEmailMetadataBatch([], inspectedThroughUid, HasMore: false));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.StoredEmailCount);
        Assert.False(result.HasMoreEmails);
        Assert.Equal(inspectedThroughUid, result.Checkpoint!.LastSeenUid);
        await session.Received(1).GetEmailBatchAfterAsync(Arg.Any<ImapUid?>(), 25, window, CancellationToken.None);
        await session.DidNotReceive().FetchEmailContentWithoutSettingSeenAsync(Arg.Any<EmailOccurrenceId>(), Arg.Any<long>(), CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folder.Id, Arg.Any<SynchronizationCheckpoint?>(), Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == inspectedThroughUid), CancellationToken.None);
    }

    /// <summary>A batch that straddles the bound checkpoints through what the search inspected, not through its last email.</summary>
    [Fact]
    public async Task SynchronizeAsync_BatchStraddlesTheBound_StoresTheIncludedEmailAndCheckpointsPastTheExcludedOnes()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var includedUid = ImapUid.Create(90);
        var inspectedThroughUid = ImapUid.Create(120);
        var includedOccurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, includedUid);
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
        var window = MailSynchronizationWindow.EmailsReceivedSince(new DateOnly(2026, 7, 1));
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options,
            synchronizationWindow: window);
        var includedMetadata = new RemoteEmailMetadata(includedOccurrence, "message-90@example.test", "Subject", new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero), 128);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, window, CancellationToken.None).Returns(
            new RemoteEmailMetadataBatch([includedMetadata], inspectedThroughUid, HasMore: false));
        session.FetchEmailContentWithoutSettingSeenAsync(includedOccurrence, 1024, CancellationToken.None).Returns(
            RemoteEmailContentFetchResult.Retrieved(new RemoteEmailContent(includedOccurrence, new ReadOnlyMemory<byte>([1, 2, 3]))));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.StoredEmailCount);
        Assert.Equal(inspectedThroughUid, result.Checkpoint!.LastSeenUid);
        await session.Received(1).FetchEmailContentWithoutSettingSeenAsync(includedOccurrence, 1024, CancellationToken.None);
        await session.Received(1).FetchEmailContentWithoutSettingSeenAsync(Arg.Any<EmailOccurrenceId>(), Arg.Any<long>(), CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folder.Id, Arg.Any<SynchronizationCheckpoint?>(), Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == inspectedThroughUid), CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_ContentStreamExceedsLimit_RecordsOccurrenceWithoutContentAndAdvancesCheckpoint()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, uid);
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
        var synchronizer = CreateSynchronizer(sessionFactory, checkpointStore, sessionScopeFactory, metadataRepository, contentStore, clock, options);
        var metadata = new RemoteEmailMetadata(occurrence, "message-1@example.test", "Subject", new DateTimeOffset(2026, 7, 24, 8, 0, 0, TimeSpan.Zero), 0);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        session.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(RemoteEmailContentFetchResult.ExceededSizeLimit());

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.Equal(0, result.StoredEmailCount);
        Assert.Equal(1, result.SkippedOversizedEmailCount);
        await contentStore.DidNotReceive().SaveContentAsync(Arg.Any<IPersistenceSession>(), Arg.Any<StoredEmailId>(), Arg.Any<RemoteEmailContent>(), CancellationToken.None);
        await metadataRepository.Received(1).UpsertMetadataAsync(persistenceSession, metadata, null, StoredEmailContentAvailability.ExceededSizeLimit, CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(persistenceSession, accountId, folder.Id, Arg.Any<SynchronizationCheckpoint?>(), Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == uid), CancellationToken.None);
    }
    [Fact]
    public async Task SynchronizeAsync_NewMessage_FetchesRemoteContentBeforeOpeningPersistenceSession()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        var contentStore = Substitute.For<IEmailContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        var metadata = new RemoteEmailMetadata(occurrence, "message-1@example.test", "Subject", clock.GetUtcNow(), 128);
        var content = new RemoteEmailContent(occurrence, new ReadOnlyMemory<byte>([1, 2, 3]));
        var contentFetched = false;
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        session.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(_ =>
        {
            contentFetched = true;
            return RemoteEmailContentFetchResult.Retrieved(content);
        });
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(_ =>
        {
            Assert.True(contentFetched);
            return persistenceSession;
        });

        // Act
        await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        await sessionScopeFactory.Received(2).BeginSessionAsync(CancellationToken.None);
        await contentStore.Received(1).SaveContentAsync(persistenceSession, Arg.Any<StoredEmailId>(), content, CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_MultipleMessages_CommitsAndDisposesEachMessageBeforeFetchingTheNext()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var firstUid = ImapUid.Create(10);
        var secondUid = ImapUid.Create(11);
        var firstOccurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, firstUid);
        var secondOccurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, secondUid);
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
        var synchronizer = CreateSynchronizer(
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
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(mailboxSession);
        mailboxSession.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        mailboxSession.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(
            new RemoteEmailMetadataBatch([firstMetadata, secondMetadata], secondUid, HasMore: false));
        mailboxSession.FetchEmailContentWithoutSettingSeenAsync(firstOccurrence, 1024, CancellationToken.None).Returns(RemoteEmailContentFetchResult.Retrieved(firstContent));
        mailboxSession.FetchEmailContentWithoutSettingSeenAsync(secondOccurrence, 1024, CancellationToken.None).Returns(_ =>
        {
            Assert.True(firstMessageSession.IsCommitted);
            Assert.True(firstMessageSession.IsDisposed);
            return RemoteEmailContentFetchResult.Retrieved(secondContent);
        });

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

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
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var firstAttemptSession = Substitute.For<IPersistenceSession>();
        var secondAttemptSession = Substitute.For<IPersistenceSession>();
        var checkpointSession = Substitute.For<IPersistenceSession>();
        var firstConflictObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None)
            .Returns(firstAttemptSession, secondAttemptSession, checkpointSession);
        firstAttemptSession.CommitAsync(CancellationToken.None).Returns(_ =>
        {
            firstConflictObserved.SetResult();
            return PersistenceCommitResult.ConcurrencyConflict;
        });
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
        };
        var synchronizer = CreateSynchronizer(
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
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None)
            .Returns(SynchronizationCheckpoint.None(uidValidity));
        mailboxSessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(mailboxSession);
        mailboxSession.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        mailboxSession.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None)
            .Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        mailboxSession.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(RemoteEmailContentFetchResult.Retrieved(content));
        metadataRepository.UpsertMetadataAsync(
                Arg.Any<IPersistenceSession>(),
                metadata,
                Arg.Any<ExtractedEmailMetadata?>(),
                StoredEmailContentAvailability.Available,
                CancellationToken.None)
            .Returns(storedEmailId);

        // Act
        var synchronizationTask = synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);
        await firstConflictObserved.Task;
        clock.Advance(TimeSpan.FromSeconds(1));
        var result = await synchronizationTask;

        // Assert
        Assert.Equal(1, result.StoredEmailCount);
        await mailboxSession.Received(1)
            .FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None);
        await metadataRepository.Received(1).UpsertMetadataAsync(
            firstAttemptSession,
            metadata,
            Arg.Any<ExtractedEmailMetadata?>(),
            StoredEmailContentAvailability.Available,
            CancellationToken.None);
        await metadataRepository.Received(1).UpsertMetadataAsync(
            secondAttemptSession,
            metadata,
            Arg.Any<ExtractedEmailMetadata?>(),
            StoredEmailContentAvailability.Available,
            CancellationToken.None);
        await contentStore.Received(1)
            .SaveContentAsync(firstAttemptSession, storedEmailId, content, CancellationToken.None);
        await contentStore.Received(1)
            .SaveContentAsync(secondAttemptSession, storedEmailId, content, CancellationToken.None);
        await sessionScopeFactory.Received(3).BeginSessionAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_PersistenceConflictsExhausted_ThrowsWithoutAdvancingCheckpoint()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var attemptSessions = Enumerable.Range(0, 2)
            .Select(_ => Substitute.For<IPersistenceSession>())
            .ToArray();
        var firstConflictObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None)
            .Returns(attemptSessions[0], attemptSessions[1]);
        attemptSessions[0].CommitAsync(CancellationToken.None).Returns(_ =>
        {
            firstConflictObserved.SetResult();
            return PersistenceCommitResult.ConcurrencyConflict;
        });
        attemptSessions[1].CommitAsync(CancellationToken.None)
            .Returns(PersistenceCommitResult.ConcurrencyConflict);

        var contentStore = Substitute.For<IEmailContentStore>();
        var mailboxSessionFactory = Substitute.For<IMailboxSessionFactory>();
        var mailboxSession = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions
        {
            MaxMetadataBatchSize = 25,
            MaxRawMimeBytes = 1024,
        };
        var synchronizer = CreateSynchronizer(
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
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None)
            .Returns(initialCheckpoint);
        mailboxSessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(mailboxSession);
        mailboxSession.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        mailboxSession.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None)
            .Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        mailboxSession.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(RemoteEmailContentFetchResult.Retrieved(content));
        metadataRepository.UpsertMetadataAsync(
                Arg.Any<IPersistenceSession>(),
                metadata,
                Arg.Any<ExtractedEmailMetadata?>(),
                StoredEmailContentAvailability.Available,
                CancellationToken.None)
            .Returns(storedEmailId);

        // Act
        var conflictAssertion = Assert.ThrowsAsync<PersistenceConcurrencyConflictException>(
            () => synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None));
        await firstConflictObserved.Task;
        clock.Advance(TimeSpan.FromSeconds(1));
        await conflictAssertion;

        // Assert
        await mailboxSession.Received(1)
            .FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None);
        await sessionScopeFactory.Received(2).BeginSessionAsync(CancellationToken.None);
        await checkpointStore.DidNotReceive().SaveCheckpointAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderResolutionId>(),
            Arg.Any<SynchronizationCheckpoint?>(),
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
        var folder = InboxFolder;
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
        };
        var synchronizer = CreateSynchronizer(
            mailboxSessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None)
            .Returns(initialCheckpoint);
        mailboxSessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None)
            .Returns(mailboxSession);
        mailboxSession.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        mailboxSession.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None)
            .Returns(new RemoteEmailMetadataBatch([], inspectedThroughUid, HasMore: false));

        // Act
        await Assert.ThrowsAsync<PersistenceConcurrencyConflictException>(
            () => synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None));

        // Assert
        await sessionScopeFactory.Received(1).BeginSessionAsync(CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(
            persistenceSession,
            accountId,
            folder.Id,
            initialCheckpoint,
            Arg.Is<SynchronizationCheckpoint>(
                checkpoint => checkpoint!.LastSeenUid == inspectedThroughUid),
            CancellationToken.None);
        await persistenceSession.Received(1).CommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_CheckpointStateChangedBeforeWrite_PropagatesConflictWithoutCommit()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var persistedUid = ImapUid.Create(5);
        var inspectedThroughUid = ImapUid.Create(10);
        var initialCheckpoint = new SynchronizationCheckpoint(
            uidValidity,
            persistedUid,
            new DateTimeOffset(2026, 7, 24, 11, 0, 0, TimeSpan.Zero));
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        sessionScopeFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var contentStore = Substitute.For<IEmailContentStore>();
        var mailboxSessionFactory = Substitute.For<IMailboxSessionFactory>();
        var mailboxSession = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions
        {
            MaxMetadataBatchSize = 25,
            MaxRawMimeBytes = 1024,
        };
        var synchronizer = CreateSynchronizer(
            mailboxSessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None)
            .Returns(initialCheckpoint);
        checkpointStore.SaveCheckpointAsync(
                persistenceSession,
                accountId,
                folder.Id,
                initialCheckpoint,
                Arg.Any<SynchronizationCheckpoint>(),
                CancellationToken.None)
            .Returns(_ => throw new PersistenceConcurrencyConflictException("progress moved"));
        mailboxSessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None)
            .Returns(mailboxSession);
        mailboxSession.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        mailboxSession.GetEmailBatchAfterAsync(persistedUid, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None)
            .Returns(new RemoteEmailMetadataBatch([], inspectedThroughUid, HasMore: false));

        // Act
        await Assert.ThrowsAsync<PersistenceConcurrencyConflictException>(
            () => synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None));

        // Assert
        await checkpointStore.Received(1).SaveCheckpointAsync(
            persistenceSession,
            accountId,
            folder.Id,
            initialCheckpoint,
            Arg.Is<SynchronizationCheckpoint>(
                checkpoint => checkpoint!.LastSeenUid == inspectedThroughUid),
            CancellationToken.None);
        await persistenceSession.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SynchronizeAsync_EmptyUnassignedMailboxWindow_DoesNotPersistSpeculativeCheckpoint()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var contentStore = Substitute.For<IEmailContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([], null, HasMore: false));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.Null(result.Checkpoint!.LastSeenUid);
        await sessionScopeFactory.DidNotReceive().BeginSessionAsync(CancellationToken.None);
        await checkpointStore.DidNotReceive().SaveCheckpointAsync(Arg.Any<IPersistenceSession>(), accountId, folder.Id, Arg.Any<SynchronizationCheckpoint?>(), Arg.Any<SynchronizationCheckpoint>(), CancellationToken.None);
    }


    [Fact]
    public async Task SynchronizeAsync_UidValidityChanged_DiscardsStaleCheckpointAndRestartsFromFirstUid()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
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
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        var reassignedUid = ImapUid.Create(1);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(staleCheckpoint);
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(currentUidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([], reassignedUid, HasMore: false));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.Equal(currentUidValidity, result.Checkpoint!.UidValidity);
        await session.Received(1).GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None);
        await session.DidNotReceive().GetEmailBatchAfterAsync(staleCheckpoint.LastSeenUid, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(
            persistenceSession,
            accountId,
            folder.Id,
            staleCheckpoint,
            Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.UidValidity == currentUidValidity && checkpoint.LastSeenUid == reassignedUid),
            CancellationToken.None);
    }

    [Fact]
    public async Task SynchronizeAsync_CancellationRequestedDuringBatch_PropagatesCancellationWithoutWritingProgress()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
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
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, cancellation.Token).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), cancellation.Token).Returns(session);
        session.GetUidValidityAsync(cancellation.Token).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, cancellation.Token).Returns<RemoteEmailMetadataBatch>(_ =>
        {
            cancellation.Cancel();
            throw new OperationCanceledException(cancellation.Token);
        });

        // Act
        await Assert.ThrowsAsync<OperationCanceledException>(() => synchronizer.SynchronizeAsync(accountId, InboxMapping, cancellation.Token));

        // Assert
        await sessionScopeFactory.DidNotReceive().BeginSessionAsync(Arg.Any<CancellationToken>());
        await checkpointStore.DidNotReceive().SaveCheckpointAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<MailAccountId>(),
            Arg.Any<MailFolderResolutionId>(),
            Arg.Any<SynchronizationCheckpoint?>(),
            Arg.Any<SynchronizationCheckpoint>(),
            Arg.Any<CancellationToken>());
        await session.Received(1).DisposeAsync();
    }

    [Fact]
    public async Task SynchronizeAsync_ConfiguredAccount_OpensTheSessionWithTheAccountTransportSecurityPolicy()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var contentStore = Substitute.For<IEmailContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 24, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var accountPolicy = MailTransportSecurityPolicy.Create(
            MailConnectionSecurity.StartTlsRequired,
            MailAuthenticationPolicy.Create(
                [MailAuthenticationMechanism.ScramSha256],
                allowInsecureConnection: false,
                allowClearTextAuthenticationOverUnencryptedConnection: false),
            MailServerCertificateTrust.SystemTrustStore,
            trustedCertificateAuthorityReference: null);
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository,
            contentStore,
            clock,
            options,
            CreateTransportSecurityPolicyReader(accountPolicy));
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([], null, HasMore: false));

        // Act
        await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        await sessionFactory.Received(1).OpenReadOnlyAsync(accountId, folder, accountPolicy, CancellationToken.None);
    }

    /// <summary>Each reason an alias resolves to no folder must reach the caller as its own outcome, and none may open a mailbox.</summary>
    [Theory]
    [InlineData(0, MailboxSynchronizationOutcome.FolderAliasUnresolved)]
    [InlineData(2, MailboxSynchronizationOutcome.FolderAliasAmbiguous)]
    public async Task SynchronizeAsync_AliasResolvesToNoSingleFolder_ReportsWhyAndOpensNoSession(
        int foldersCarryingTheInboxRole,
        MailboxSynchronizationOutcome expectedOutcome)
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var persistenceSessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            Substitute.For<ISynchronizationCheckpointStore>(),
            persistenceSessionFactory,
            Substitute.For<IEmailMetadataRepository>(),
            Substitute.For<IEmailContentStore>(),
            clock,
            new MailboxSynchronizationOptions(),
            folderResolver: CreateFolderResolverOverAdvertisedInboxes(
                foldersCarryingTheInboxRole,
                persistenceSessionFactory,
                clock));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.Equal(expectedOutcome, result.Outcome);
        Assert.Null(result.Checkpoint);
        await sessionFactory.DidNotReceiveWithAnyArgs().OpenReadOnlyAsync(default!, default!, default!, CancellationToken.None);
    }

    /// <summary>Enrichment must read the payload the run already fetched, never a second copy from the mail server.</summary>
    [Fact]
    public async Task SynchronizeAsync_StoredMessage_EnrichesTheFetchedContentWithoutASecondRemoteRead()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var persistenceSessionFactory = Substitute.For<IPersistenceSessionFactory>();
        persistenceSessionFactory.BeginSessionAsync(CancellationToken.None).Returns(Substitute.For<IPersistenceSession>());
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var mimeReader = CreateMimeReaderThatExtractsEverything();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            persistenceSessionFactory,
            Substitute.For<IEmailMetadataRepository>(),
            Substitute.For<IEmailContentStore>(),
            clock,
            options,
            mimeReader: mimeReader);
        var metadata = new RemoteEmailMetadata(occurrence, "message-1@example.test", "Subject", null, 128);
        var content = new RemoteEmailContent(occurrence, new ReadOnlyMemory<byte>([1, 2, 3]));
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        session.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(RemoteEmailContentFetchResult.Retrieved(content));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.StoredEmailCount);
        Assert.Equal(0, result.UnreadableMimeEmailCount);
        await mimeReader.Received(1).ReadMetadataAsync(content, CancellationToken.None);
        await session.Received(1).FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None);
    }

    /// <summary>The row must describe the payload that was stored, so what the reader extracted has to reach persistence.</summary>
    [Fact]
    public async Task SynchronizeAsync_StoredMessage_HandsTheExtractedMetadataToPersistence()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var persistenceSessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        persistenceSessionFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var content = new RemoteEmailContent(occurrence, new ReadOnlyMemory<byte>([1, 2, 3]));
        var extracted = CreateExtractedMetadata(occurrence);
        var mimeReader = Substitute.For<IEmailMimeReader>();
        mimeReader
            .ReadMetadataAsync(content, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailMimeExtractionResult.Extracted(extracted)));
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            persistenceSessionFactory,
            metadataRepository,
            Substitute.For<IEmailContentStore>(),
            clock,
            options,
            mimeReader: mimeReader);
        var metadata = new RemoteEmailMetadata(occurrence, "message-1@example.test", "Subject", null, 128);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        session.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(RemoteEmailContentFetchResult.Retrieved(content));

        // Act
        await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        await metadataRepository.Received(1).UpsertMetadataAsync(
            persistenceSession,
            metadata,
            extracted,
            StoredEmailContentAvailability.Available,
            CancellationToken.None);
    }

    /// <summary>Nothing was read from the payload, so the row records only what the server's envelope reported.</summary>
    [Fact]
    public async Task SynchronizeAsync_MessageWhoseMimeCannotBeRead_PersistsTheOccurrenceWithoutExtractedMetadata()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var persistenceSessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        persistenceSessionFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var content = new RemoteEmailContent(occurrence, new ReadOnlyMemory<byte>([1, 2, 3]));
        var mimeReader = Substitute.For<IEmailMimeReader>();
        mimeReader
            .ReadMetadataAsync(content, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(EmailMimeExtractionResult.MalformedContent()));
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            persistenceSessionFactory,
            metadataRepository,
            Substitute.For<IEmailContentStore>(),
            clock,
            options,
            mimeReader: mimeReader);
        var metadata = new RemoteEmailMetadata(occurrence, "message-1@example.test", "Subject", null, 128);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch([metadata], uid, HasMore: false));
        session.FetchEmailContentWithoutSettingSeenAsync(occurrence, 1024, CancellationToken.None).Returns(RemoteEmailContentFetchResult.Retrieved(content));

        // Act
        await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        await metadataRepository.Received(1).UpsertMetadataAsync(
            persistenceSession,
            metadata,
            null,
            StoredEmailContentAvailability.Available,
            CancellationToken.None);
    }

    /// <summary>A message nobody can parse is counted and stepped over: it stops neither the batch nor the checkpoint.</summary>
    [Theory]
    [InlineData(EmailMimeExtractionOutcome.MalformedContent)]
    [InlineData(EmailMimeExtractionOutcome.PartCountLimitExceeded)]
    [InlineData(EmailMimeExtractionOutcome.NestingDepthLimitExceeded)]
    public async Task SynchronizeAsync_MessageWhoseMimeCannotBeRead_CountsItAndKeepsTheBatchAndCheckpointGoing(
        EmailMimeExtractionOutcome unreadableOutcome)
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var unreadableOccurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, ImapUid.Create(10));
        var readableOccurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, ImapUid.Create(11));
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var metadataRepository = Substitute.For<IEmailMetadataRepository>();
        var persistenceSessionFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        persistenceSessionFactory.BeginSessionAsync(CancellationToken.None).Returns(persistenceSession);
        var contentStore = Substitute.For<IEmailContentStore>();
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var unreadableContent = new RemoteEmailContent(unreadableOccurrence, new ReadOnlyMemory<byte>([1]));
        var readableContent = new RemoteEmailContent(readableOccurrence, new ReadOnlyMemory<byte>([2]));
        var mimeReader = CreateMimeReaderThatExtractsEverything();
        mimeReader
            .ReadMetadataAsync(unreadableContent, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CreateFailedExtraction(unreadableOutcome)));
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            persistenceSessionFactory,
            metadataRepository,
            contentStore,
            clock,
            options,
            mimeReader: mimeReader);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch(
            [
                new RemoteEmailMetadata(unreadableOccurrence, "message-1@example.test", "Subject", null, 128),
                new RemoteEmailMetadata(readableOccurrence, "message-2@example.test", "Subject", null, 128),
            ],
            ImapUid.Create(11),
            HasMore: false));
        session.FetchEmailContentWithoutSettingSeenAsync(unreadableOccurrence, 1024, CancellationToken.None).Returns(RemoteEmailContentFetchResult.Retrieved(unreadableContent));
        session.FetchEmailContentWithoutSettingSeenAsync(readableOccurrence, 1024, CancellationToken.None).Returns(RemoteEmailContentFetchResult.Retrieved(readableContent));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.Equal(2, result.StoredEmailCount);
        Assert.Equal(1, result.UnreadableMimeEmailCount);
        await contentStore.Received(2).SaveContentAsync(persistenceSession, Arg.Any<StoredEmailId>(), Arg.Any<RemoteEmailContent>(), CancellationToken.None);
        await checkpointStore.Received(1).SaveCheckpointAsync(
            persistenceSession,
            accountId,
            folder.Id,
            Arg.Any<SynchronizationCheckpoint?>(),
            Arg.Is<SynchronizationCheckpoint>(checkpoint => checkpoint!.LastSeenUid == ImapUid.Create(11)),
            CancellationToken.None);
    }

    /// <summary>An occurrence stored without its content has no MIME to read, so nothing may be asked to read one.</summary>
    [Fact]
    public async Task SynchronizeAsync_OversizedMessage_ReadsNoMimeAndCountsNoneUnreadable()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var uid = ImapUid.Create(10);
        var occurrence = EmailOccurrenceId.Create(accountId, folder.Id, uidValidity, uid);
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var persistenceSessionFactory = Substitute.For<IPersistenceSessionFactory>();
        persistenceSessionFactory.BeginSessionAsync(CancellationToken.None).Returns(Substitute.For<IPersistenceSession>());
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var mimeReader = CreateMimeReaderThatExtractsEverything();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 28, 12, 0, 0, TimeSpan.Zero));
        var options = new MailboxSynchronizationOptions { MaxMetadataBatchSize = 25, MaxRawMimeBytes = 1024 };
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            persistenceSessionFactory,
            Substitute.For<IEmailMetadataRepository>(),
            Substitute.For<IEmailContentStore>(),
            clock,
            options,
            mimeReader: mimeReader);
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session.GetEmailBatchAfterAsync(null, 25, MailSynchronizationWindow.Unbounded, CancellationToken.None).Returns(new RemoteEmailMetadataBatch(
            [new RemoteEmailMetadata(occurrence, "message-1@example.test", "Subject", null, 2048)],
            uid,
            HasMore: false));

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.SkippedOversizedEmailCount);
        Assert.Equal(0, result.UnreadableMimeEmailCount);
        await mimeReader.DidNotReceiveWithAnyArgs().ReadMetadataAsync(default!, CancellationToken.None);
    }

    private static EmailMimeExtractionResult CreateFailedExtraction(EmailMimeExtractionOutcome outcome) => outcome switch
    {
        EmailMimeExtractionOutcome.MalformedContent => EmailMimeExtractionResult.MalformedContent(),
        EmailMimeExtractionOutcome.PartCountLimitExceeded => EmailMimeExtractionResult.PartCountLimitExceeded(),
        EmailMimeExtractionOutcome.NestingDepthLimitExceeded => EmailMimeExtractionResult.NestingDepthLimitExceeded(),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Only failures are built here."),
    };

    /// <summary>Builds a resolver over a server advertising a chosen number of folders that all carry the inbox role.</summary>
    private static MailFolderResolver CreateFolderResolverOverAdvertisedInboxes(
        int foldersCarryingTheInboxRole,
        IPersistenceSessionFactory persistenceSessionFactory,
        TimeProvider timeProvider)
    {
        var remoteFolderCatalog = Substitute.For<IRemoteFolderCatalog>();
        remoteFolderCatalog
            .ListFoldersAsync(Arg.Any<MailAccountId>(), Arg.Any<MailTransportSecurityPolicy>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RemoteFolder>>(
            [
                .. Enumerable.Range(0, foldersCarryingTheInboxRole).Select(index =>
                    new RemoteFolder(RemoteFolderPath.Create($"Inbox{index}", '/'), [MailFolderSpecialUse.Inbox])),
            ]));

        return new MailFolderResolver(
            remoteFolderCatalog,
            Substitute.For<IMailFolderResolutionStore>(),
            Substitute.For<IMailFolderMappingChangeAuditor>(),
            persistenceSessionFactory,
            timeProvider);
    }

    /// <summary>Builds a resolver whose alias is already bound to the folder the server advertises, so no run rebinds it.</summary>
    /// <remarks>
    /// These tests are about what synchronization does once a folder is known. Resolution has tests of its own, and
    /// leaving it unbound here would make every run write a binding and consume a persistence session the assertions
    /// about checkpoint commits are counting.
    /// </remarks>
    private static MailFolderResolver CreateFolderResolverBoundToInbox(
        IPersistenceSessionFactory persistenceSessionFactory,
        TimeProvider timeProvider)
    {
        var remoteFolderCatalog = Substitute.For<IRemoteFolderCatalog>();
        remoteFolderCatalog
            .ListFoldersAsync(Arg.Any<MailAccountId>(), Arg.Any<MailTransportSecurityPolicy>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RemoteFolder>>(
                [new RemoteFolder(InboxRemotePath, [MailFolderSpecialUse.Inbox])]));

        var resolutionStore = Substitute.For<IMailFolderResolutionStore>();
        resolutionStore
            .GetCurrentResolutionAsync(Arg.Any<MailAccountId>(), Arg.Any<MailFolderAlias>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<MailFolderResolution?>(InboxFolder));

        return new MailFolderResolver(
            remoteFolderCatalog,
            resolutionStore,
            Substitute.For<IMailFolderMappingChangeAuditor>(),
            persistenceSessionFactory,
            timeProvider);
    }

    /// <summary>
    /// The backward pass belongs to the run rather than to a worker of its own, so it runs over the session the forward
    /// pass opened and under the UIDVALIDITY that session reported.
    /// </summary>
    [Fact]
    public async Task SynchronizeAsync_FolderHoldsEmailsAwaitingReconciliation_ChecksThemOverTheSameSessionAndReportsWhatItFound()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var folder = InboxFolder;
        var uidValidity = ImapUidValidity.Create(5);
        var storedEmailId = StoredEmailId.Create(Guid.CreateVersion7());
        var checkpointStore = Substitute.For<ISynchronizationCheckpointStore>();
        var sessionScopeFactory = Substitute.For<IPersistenceSessionFactory>();
        var persistenceSession = Substitute.For<IPersistenceSession>();
        sessionScopeFactory.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(persistenceSession);
        persistenceSession.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        var sessionFactory = Substitute.For<IMailboxSessionFactory>();
        var session = Substitute.For<IMailboxSession>();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 7, 31, 12, 0, 0, TimeSpan.Zero));
        var reconciliationStore = Substitute.For<IStoredEmailReconciliationStore>();
        IReadOnlyList<StoredEmailAwaitingReconciliation> window =
            [new StoredEmailAwaitingReconciliation(storedEmailId, ImapUid.Create(10))];
        reconciliationStore
            .GetReconciliationWindowAsync(accountId, folder.Id, uidValidity, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(window));
        checkpointStore.GetCheckpointAsync(accountId, folder.Id, CancellationToken.None).Returns(SynchronizationCheckpoint.None(uidValidity));
        sessionFactory.OpenReadOnlyAsync(accountId, folder, Arg.Any<MailTransportSecurityPolicy>(), CancellationToken.None).Returns(session);
        session.GetUidValidityAsync(CancellationToken.None).Returns(uidValidity);
        session
            .GetEmailBatchAfterAsync(null, Arg.Any<int>(), MailSynchronizationWindow.Unbounded, CancellationToken.None)
            .Returns(new RemoteEmailMetadataBatch([], InspectedThroughUid: null, HasMore: false));
        session
            .GetRemoteFlagsWithoutSettingSeenAsync(Arg.Any<IReadOnlyList<ImapUid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<RemoteEmailFlagObservation>>([]));
        var synchronizer = CreateSynchronizer(
            sessionFactory,
            checkpointStore,
            sessionScopeFactory,
            metadataRepository: Substitute.For<IEmailMetadataRepository>(),
            contentStore: Substitute.For<IEmailContentStore>(),
            clock,
            new MailboxSynchronizationOptions(),
            reconciliationStore: reconciliationStore);

        // Act
        var result = await synchronizer.SynchronizeAsync(accountId, InboxMapping, CancellationToken.None);

        // Assert
        Assert.Equal(1, result.Reconciliation.RemotelyDeletedEmailCount);
        await reconciliationStore.Received(1).ApplyReconciliationOutcomeAsync(
            persistenceSession,
            Arg.Is<ReconciledFolderOutcome>(applied =>
                applied!.Disappeared.Single() == storedEmailId
                && applied.Disposition == RemotelyDeletedEmailDisposition.RetainTombstone),
            Arg.Any<CancellationToken>());
        await sessionFactory.Received(1).OpenReadOnlyAsync(
            accountId,
            folder,
            Arg.Any<MailTransportSecurityPolicy>(),
            CancellationToken.None);
    }

    private static IMailTransportSecurityPolicyReader CreateTransportSecurityPolicyReader(MailTransportSecurityPolicy policy)
    {
        var reader = Substitute.For<IMailTransportSecurityPolicyReader>();
        reader.GetPolicy(Arg.Any<MailAccountId>()).Returns(policy);

        return reader;
    }

    private static IMailSynchronizationWindowReader CreateSynchronizationWindowReader(MailSynchronizationWindow window)
    {
        var reader = Substitute.For<IMailSynchronizationWindowReader>();
        reader.GetWindow(Arg.Any<MailAccountId>()).Returns(window);

        return reader;
    }

    private static MailboxSynchronizer CreateSynchronizer(
        IMailboxSessionFactory mailboxSessionFactory,
        ISynchronizationCheckpointStore checkpointStore,
        IPersistenceSessionFactory persistenceSessionFactory,
        IEmailMetadataRepository metadataRepository,
        IEmailContentStore contentStore,
        TimeProvider timeProvider,
        MailboxSynchronizationOptions options,
        IMailTransportSecurityPolicyReader? transportSecurityPolicyReader = null,
        MailFolderResolver? folderResolver = null,
        IEmailMimeReader? mimeReader = null,
        MailSynchronizationWindow synchronizationWindow = default,
        IStoredEmailReconciliationStore? reconciliationStore = null)
    {
        var concurrencyRetryPolicy = new OptimisticConcurrencyRetryPolicy(
            persistenceSessionFactory,
            new PersistenceConcurrencyOptions(),
            timeProvider);

        return new MailboxSynchronizer(
            folderResolver ?? CreateFolderResolverBoundToInbox(persistenceSessionFactory, timeProvider),
            mailboxSessionFactory,
            transportSecurityPolicyReader ?? CreateTransportSecurityPolicyReader(RequiredTlsPolicy),
            CreateSynchronizationWindowReader(synchronizationWindow),
            checkpointStore,
            persistenceSessionFactory,
            metadataRepository,
            contentStore,
            mimeReader ?? CreateMimeReaderThatExtractsEverything(),
            new MailboxReconciler(
                reconciliationStore ?? CreateReconciliationStoreWithNothingToDo(),
                CreateDispositionReader(RemotelyDeletedEmailDisposition.RetainTombstone),
                concurrencyRetryPolicy,
                timeProvider,
                options),
            concurrencyRetryPolicy,
            timeProvider,
            options);
    }

    /// <summary>Builds a store whose folders hold nothing awaiting reconciliation, which is the case every forward-pass test is about.</summary>
    private static IStoredEmailReconciliationStore CreateReconciliationStoreWithNothingToDo()
    {
        var reconciliationStore = Substitute.For<IStoredEmailReconciliationStore>();
        reconciliationStore
            .GetReconciliationWindowAsync(
                Arg.Any<MailAccountId>(),
                Arg.Any<MailFolderResolutionId>(),
                Arg.Any<ImapUidValidity>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<StoredEmailAwaitingReconciliation>>([]));

        return reconciliationStore;
    }

    private static IRemotelyDeletedEmailDispositionReader CreateDispositionReader(
        RemotelyDeletedEmailDisposition disposition)
    {
        var reader = Substitute.For<IRemotelyDeletedEmailDispositionReader>();
        reader.GetDisposition(Arg.Any<MailAccountId>()).Returns(disposition);

        return reader;
    }

    /// <summary>Builds a reader whose messages all parse, which is the case every other behavior here is about.</summary>
    private static IEmailMimeReader CreateMimeReaderThatExtractsEverything()
    {
        var mimeReader = Substitute.For<IEmailMimeReader>();
        mimeReader
            .ReadMetadataAsync(Arg.Any<RemoteEmailContent>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(
                EmailMimeExtractionResult.Extracted(CreateExtractedMetadata(call.Arg<RemoteEmailContent>()!.OccurrenceId))));

        return mimeReader;
    }

    private static ExtractedEmailMetadata CreateExtractedMetadata(EmailOccurrenceId occurrenceId) => new(
        occurrenceId,
        Subject: "Subject",
        SentAt: null,
        ReceivedAt: null,
        Participants: [],
        EmailThreadReferences.None,
        EmailAttachmentSummary.None,
        ExtractedEmailText.NoTextualBody);

    private sealed class TrackingSession : IPersistenceSession
    {
        private static readonly Task<PersistenceCommitResult> committedResultTask =
            Task.FromResult(PersistenceCommitResult.Committed);

        public bool IsCommitted { get; private set; }

        public bool IsDisposed { get; private set; }

        public Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken)
        {
            this.IsCommitted = true;
            return committedResultTask;
        }

        public ValueTask DisposeAsync()
        {
            this.IsDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

}
