// Copyright © 2026 Krzysztof Kasprowicz

using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailMcp.Application.EmailContent;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Emails;
using NSubstitute;
using Xunit;
using static MailMcp.Infrastructure.UnitTests.MailKitImapSessionTestContext;

namespace MailMcp.Infrastructure.UnitTests;

/// <summary>Covers what the configured pipelines do to an IMAP session when a mail server misbehaves.</summary>
public sealed class MailKitImapSessionResilienceTests
{
    /// <summary>How far virtual time moves per step while a pipeline waits out its backoff.</summary>
    private static readonly TimeSpan BackoffAdvanceStep = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task GetEmailBatchAfterAsync_ServerDropsTheConnectionMidBatch_ReestablishesTheSessionAndCompletesTheBatch()
    {
        // Arrange
        using var resilience = OutboundResilienceTestHost.WithConfiguredSettings();
        var droppedClient = new FakeImapClient();
        var recoveredClient = new FakeImapClient();
        var droppedFolder = CreateSelectedFolder();
        var recoveredFolder = CreateSelectedFolder();
        var recoveredSummary = CreateSummary(new UniqueId(10));
        droppedFolder.UidNext.Returns(new UniqueId(11));
        recoveredFolder.UidNext.Returns(new UniqueId(11));
        droppedFolder.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<IList<UniqueId>>(_ => throw droppedClient.DropConnection(new IOException("the server closed the connection")));
        recoveredFolder.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([new UniqueId(10)]);
        recoveredFolder.FetchAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IFetchRequest>(),
            Arg.Any<CancellationToken>()).Returns([recoveredSummary]);
        await using var session = await OpenScriptedSessionAsync(resilience, droppedClient, droppedFolder, recoveredClient, recoveredFolder);

        // Act
        var batch = await resilience.CompleteOnVirtualTimeAsync(
            session.GetEmailBatchAfterAsync(null, 100, CancellationToken.None),
            BackoffAdvanceStep);

        // Assert
        Assert.Equal([10U], batch.Emails.Select(email => email.OccurrenceId.Uid.Value));
        Assert.Equal(1, recoveredClient.ConnectCount);
        await recoveredFolder.Received(1).OpenAsync(FolderAccess.ReadOnly, Arg.Any<CancellationToken>());
        await recoveredFolder.DidNotReceive().OpenAsync(FolderAccess.ReadWrite, Arg.Any<CancellationToken>());
    }

    /// <summary>The case draft section 11.1 makes non-negotiable: recovery must not become the path that marks mail as read.</summary>
    [Fact]
    public async Task FetchEmailContentWithoutSettingSeenAsync_RetriedAfterADroppedConnection_ReselectsReadOnlyAndNeverSetsSeen()
    {
        // Arrange
        using var resilience = OutboundResilienceTestHost.WithConfiguredSettings();
        var droppedClient = new FakeImapClient();
        var recoveredClient = new FakeImapClient();
        var droppedFolder = CreateSelectedFolder();
        var recoveredFolder = CreateSelectedFolder();
        var rawMime = "From: sender@example.test\r\nSubject: Subject\r\n\r\nBody"u8.ToArray();
        droppedFolder.GetStreamAsync(new UniqueId(10), Arg.Any<CancellationToken>())
            .Returns<Stream>(_ => throw droppedClient.DropConnection(new IOException("the server closed the connection")));
        recoveredFolder.GetStreamAsync(new UniqueId(10), Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream(rawMime));
        await using var session = await OpenScriptedSessionAsync(resilience, droppedClient, droppedFolder, recoveredClient, recoveredFolder);

        // Act
        var fetch = await resilience.CompleteOnVirtualTimeAsync(
            session.FetchEmailContentWithoutSettingSeenAsync(CreateOccurrenceId(10), 1024, CancellationToken.None),
            BackoffAdvanceStep);

        // Assert
        Assert.Equal(RemoteEmailContentFetchOutcome.Retrieved, fetch.Outcome);
        Assert.Equal(rawMime, fetch.Content!.RawMime.ToArray());
        await recoveredFolder.Received(1).OpenAsync(FolderAccess.ReadOnly, Arg.Any<CancellationToken>());
        await recoveredFolder.Received(1).GetStreamAsync(new UniqueId(10), Arg.Any<CancellationToken>());
        await recoveredFolder.DidNotReceive().OpenAsync(FolderAccess.ReadWrite, Arg.Any<CancellationToken>());
        await recoveredFolder.DidNotReceive().StoreAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IStoreFlagsRequest>(), Arg.Any<CancellationToken>());
        await droppedFolder.DidNotReceive().StoreAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IStoreFlagsRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A desynchronized command stream is not something a client reports: it keeps answering that its socket is open.
    /// Retrying on that connection would spend every attempt on the same unusable session.
    /// </summary>
    [Fact]
    public async Task GetEmailBatchAfterAsync_TransientFailureLeavesTheClientReportingConnected_StillRebuildsTheSessionBeforeRetrying()
    {
        // Arrange
        using var resilience = OutboundResilienceTestHost.WithConfiguredSettings();
        var desynchronizedClient = new FakeImapClient();
        var recoveredClient = new FakeImapClient();
        var desynchronizedFolder = CreateSelectedFolder();
        var recoveredFolder = CreateSelectedFolder();
        var recoveredSummary = CreateSummary(new UniqueId(10));
        desynchronizedFolder.UidNext.Returns(new UniqueId(11));
        recoveredFolder.UidNext.Returns(new UniqueId(11));

        // The client is deliberately left reporting a live connection, which is what MailKit's own state would do if it
        // ever stopped tearing the engine down on a protocol error.
        desynchronizedFolder.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<IList<UniqueId>>(_ => throw new ImapProtocolException("The server sent an unexpected token."));
        recoveredFolder.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([new UniqueId(10)]);
        recoveredFolder.FetchAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IFetchRequest>(),
            Arg.Any<CancellationToken>()).Returns([recoveredSummary]);
        await using var session = await OpenScriptedSessionAsync(resilience, desynchronizedClient, desynchronizedFolder, recoveredClient, recoveredFolder);

        // Act
        var batch = await resilience.CompleteOnVirtualTimeAsync(
            session.GetEmailBatchAfterAsync(null, 100, CancellationToken.None),
            BackoffAdvanceStep);

        // Assert
        Assert.Equal([10U], batch.Emails.Select(email => email.OccurrenceId.Uid.Value));
        Assert.Equal(1, recoveredClient.ConnectCount);
        await recoveredFolder.Received(1).OpenAsync(FolderAccess.ReadOnly, Arg.Any<CancellationToken>());
        await recoveredFolder.DidNotReceive().OpenAsync(FolderAccess.ReadWrite, Arg.Any<CancellationToken>());
    }

    /// <summary>A connection being replaced is unusable, so it is closed rather than asked for a logout that can block past the attempt budget.</summary>
    [Fact]
    public async Task GetEmailBatchAfterAsync_ConnectionReplacedAfterATransientFailure_ClosesItWithoutALogout()
    {
        // Arrange
        using var resilience = OutboundResilienceTestHost.WithConfiguredSettings();
        var desynchronizedClient = new FakeImapClient();
        var recoveredClient = new FakeImapClient();
        var desynchronizedFolder = CreateSelectedFolder();
        var recoveredFolder = CreateSelectedFolder();
        desynchronizedFolder.UidNext.Returns(new UniqueId(11));
        recoveredFolder.UidNext.Returns(new UniqueId(1));
        desynchronizedFolder.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<IList<UniqueId>>(_ => throw new ImapProtocolException("The server sent an unexpected token."));
        await using var session = await OpenScriptedSessionAsync(resilience, desynchronizedClient, desynchronizedFolder, recoveredClient, recoveredFolder);

        // Act
        await resilience.CompleteOnVirtualTimeAsync(
            session.GetEmailBatchAfterAsync(null, 100, CancellationToken.None),
            BackoffAdvanceStep);

        // Assert
        Assert.Equal(0, desynchronizedClient.DisconnectCount);
        Assert.Equal(1, desynchronizedClient.DisposeCount);
    }

    /// <summary>An exhausted retry budget rethrows the last transient failure, which must still reach the worker as mailbox unavailability.</summary>
    [Fact]
    public async Task GetEmailBatchAfterAsync_EveryAttemptFailsTransiently_ReportsTheMailboxUnavailableRatherThanTheMailLibraryFailure()
    {
        // Arrange
        using var resilience = OutboundResilienceTestHost.WithConfiguredSettings();
        var establishedClients = new List<FakeImapClient>();
        var factory = CreateFactory(
            resilience,
            () => CreateAlwaysFailingConnection(establishedClients),
            CreateSettingsProvider());
        await using var session = await factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None);

        // Act
        var failure = await Assert.ThrowsAsync<MailboxUnavailableException>(
            () => resilience.CompleteOnVirtualTimeAsync(
                session.GetEmailBatchAfterAsync(null, 100, CancellationToken.None),
                BackoffAdvanceStep));

        // Assert
        Assert.Equal(PrimaryAccount, failure.AccountId);
        Assert.IsType<IOException>(failure.InnerException);
        Assert.Equal(3, establishedClients.Count);
    }

    /// <summary>Repeating a rejected credential is how a mailbox account gets locked, so the pipeline must not do it.</summary>
    [Fact]
    public async Task OpenReadOnlyAsync_ServerRejectsTheCredential_FailsOnTheFirstAttemptWithoutResolvingTheSecretAgain()
    {
        // Arrange
        using var resilience = OutboundResilienceTestHost.WithConfiguredSettings();
        var client = new FakeImapClient();
        var settingsProvider = CreateSettingsProvider(out var resolvedMaterial);
        client.AuthenticationMechanisms.Add("PLAIN");
        client.Folder = CreateSelectedFolder();
        client.AuthenticateException = new MailKit.Security.AuthenticationException("the credential was rejected");
        var factory = CreateFactory(resilience, () => client.Client, settingsProvider);

        // Act
        await Assert.ThrowsAsync<MailKit.Security.AuthenticationException>(() => factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None));

        // Assert
        Assert.Equal(1, client.ConnectCount);
        Assert.Single(resolvedMaterial);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_ServerNeverAnswersTheConnect_SpendsEveryAttemptAndReportsTheMailboxUnavailable()
    {
        // Arrange
        // The total timeout stays beyond the pumping loop's reach, so the assertion is about the attempts a silent
        // server costs rather than about how fast the loop moved the clock.
        using var resilience = OutboundResilienceTestHost.WithConfiguredSettings(
            ("MailboxSessionEstablishment:AttemptTimeout", "00:00:05"),
            ("MailboxSessionEstablishment:BaseDelay", "00:00:01"),
            ("MailboxSessionEstablishment:MaxDelay", "00:00:02"),
            ("MailboxSessionEstablishment:TotalTimeout", "1.00:00:00"));
        var client = new FakeImapClient();
        client.ConnectBehavior = attemptToken => Task.Delay(Timeout.InfiniteTimeSpan, attemptToken);
        var factory = CreateFactory(resilience, () => client.Client, CreateSettingsProvider());

        // Act
        var execution = factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None);

        var failure = await Assert.ThrowsAsync<MailboxUnavailableException>(
            () => resilience.CompleteOnVirtualTimeAsync(execution, TimeSpan.FromSeconds(1)));

        // Assert
        Assert.Equal(PrimaryAccount, failure.AccountId);
        Assert.Equal(InboxFolder.Alias, failure.FolderAlias);
        Assert.Equal(3, client.ConnectCount);
    }

    /// <summary>A host shutting down and a mail server that stopped answering must not reach the worker as one failure.</summary>
    [Fact]
    public async Task OpenReadOnlyAsync_CallerCancelsWhileTheServerIsSilent_ReportsCancellationRatherThanUnavailability()
    {
        // Arrange
        using var resilience = OutboundResilienceTestHost.WithConfiguredSettings();
        var client = new FakeImapClient();
        using var callerCancellation = new CancellationTokenSource();
        client.ConnectBehavior = async attemptToken =>
        {
            await callerCancellation.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, attemptToken);
        };
        var factory = CreateFactory(resilience, () => client.Client, CreateSettingsProvider());

        // Act
        var failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy,
            callerCancellation.Token));

        // Assert
        Assert.IsNotType<MailboxUnavailableException>(failure);
        Assert.Equal(1, client.ConnectCount);
    }

    /// <summary>A recovered connection can land on a folder the server recreated, and its UIDs no longer mean what the run assumed.</summary>
    [Fact]
    public async Task GetEmailBatchAfterAsync_ReselectedFolderReportsANewUidValidity_StopsTheRunInsteadOfMixingIdentities()
    {
        // Arrange
        using var resilience = OutboundResilienceTestHost.WithConfiguredSettings();
        var droppedClient = new FakeImapClient();
        var recoveredClient = new FakeImapClient();
        var droppedFolder = CreateSelectedFolder();
        var recreatedFolder = CreateSelectedFolder(uidValidity: 9U);
        droppedFolder.UidNext.Returns(new UniqueId(11));
        droppedFolder.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<IList<UniqueId>>(_ => throw droppedClient.DropConnection(new IOException("the server closed the connection")));
        await using var session = await OpenScriptedSessionAsync(resilience, droppedClient, droppedFolder, recoveredClient, recreatedFolder);

        // Act
        var execution = session.GetEmailBatchAfterAsync(null, 100, CancellationToken.None);
        var failure = await Assert.ThrowsAsync<MailboxFolderRecreatedException>(
            () => resilience.CompleteOnVirtualTimeAsync(execution, BackoffAdvanceStep));

        // Assert
        Assert.Equal(ImapUidValidity.Create(7), failure.SessionUidValidity);
        Assert.Equal(ImapUidValidity.Create(9), failure.ReselectedUidValidity);
    }

    /// <summary>Builds a fresh connection whose folder always drops the read, and records it so a test can count the reconnections.</summary>
    private static IImapClient CreateAlwaysFailingConnection(List<FakeImapClient> establishedClients)
    {
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        folder.UidNext.Returns(new UniqueId(11));
        folder.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns<IList<UniqueId>>(_ => throw client.DropConnection(new IOException("the server closed the connection")));
        client.AuthenticationMechanisms.Add("PLAIN");
        client.Folder = folder;
        establishedClients.Add(client);

        return client.Client;
    }

    private static IMessageSummary CreateSummary(UniqueId uid)
    {
        var summary = Substitute.For<IMessageSummary>();
        summary.UniqueId.Returns(uid);
        summary.Envelope.Returns(new Envelope { Subject = $"Subject {uid.Id}" });
        summary.Size.Returns(128U);

        return summary;
    }

    /// <summary>Opens a session on the first scripted connection, leaving the second one for the reconnection under test.</summary>
    private static Task<IMailboxSession> OpenScriptedSessionAsync(
        OutboundResilienceTestHost resilience,
        FakeImapClient firstClient,
        IMailFolder firstFolder,
        FakeImapClient recoveredClient,
        IMailFolder recoveredFolder)
    {
        firstClient.AuthenticationMechanisms.Add("PLAIN");
        firstClient.Folder = firstFolder;
        recoveredClient.AuthenticationMechanisms.Add("PLAIN");
        recoveredClient.Folder = recoveredFolder;

        var factory = CreateFactory(
            resilience,
            ConnectionSequence(firstClient, recoveredClient),
            CreateSettingsProvider());

        return factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None);
    }
}
