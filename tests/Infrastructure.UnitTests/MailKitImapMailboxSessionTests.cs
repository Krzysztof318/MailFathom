// Copyright © 2026 Krzysztof Kasprowicz

using MailKit;
using MailKit.Search;
using MailKit.Security;
using MailMcp.Application.EmailContent;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Synchronization;
using MailMcp.Domain.Transport;
using MailMcp.Infrastructure.Mail;
using NSubstitute;
using Xunit;
using static MailMcp.Infrastructure.UnitTests.MailKitImapSessionTestContext;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class MailKitImapMailboxSessionTests
{
    [Theory]
    [InlineData(MailConnectionSecurity.Auto, SecureSocketOptions.Auto)]
    [InlineData(MailConnectionSecurity.TlsOnConnect, SecureSocketOptions.SslOnConnect)]
    [InlineData(MailConnectionSecurity.StartTlsRequired, SecureSocketOptions.StartTls)]
    [InlineData(MailConnectionSecurity.StartTlsWhenAvailable, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(MailConnectionSecurity.None, SecureSocketOptions.None)]
    public async Task OpenReadOnlyAsync_ConnectionSecurityMode_ConnectsWithTheMappedSocketOptions(
        MailConnectionSecurity connectionSecurity,
        SecureSocketOptions expectedSocketOptions)
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var factory = CreateFactory(resilience, client, CreateSelectedFolder());
        client.AuthenticationMechanisms.Add("SCRAM-SHA-256");

        // Act
        await using var session = await factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            CreatePolicy(connectionSecurity, MailAuthenticationMechanism.ScramSha256),
            CancellationToken.None);

        // Assert
        Assert.Equal(expectedSocketOptions, client.ConnectSocketOptions);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_ServerAdvertisesMechanismsOutsideThePolicy_RemovesThemBeforeAuthenticating()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var factory = CreateFactory(resilience, client, CreateSelectedFolder());
        client.AuthenticationMechanisms.Add("PLAIN");
        client.AuthenticationMechanisms.Add("LOGIN");
        client.AuthenticationMechanisms.Add("SCRAM-SHA-256");

        // Act
        await using var session = await factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            CreatePolicy(MailConnectionSecurity.TlsOnConnect, MailAuthenticationMechanism.ScramSha256),
            CancellationToken.None);

        // Assert
        Assert.Equal(["SCRAM-SHA-256"], client.MechanismsWhenAuthenticated);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_ServerAdvertisesNoPermittedMechanism_FailsWithoutAuthenticatingOrWideningTheSet()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var factory = CreateFactory(resilience, client, CreateSelectedFolder());
        client.AuthenticationMechanisms.Add("LOGIN");

        // Act
        var exception = await Assert.ThrowsAsync<MailAuthenticationMechanismUnavailableException>(() => factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            CreatePolicy(MailConnectionSecurity.TlsOnConnect, MailAuthenticationMechanism.ScramSha256),
            CancellationToken.None));

        // Assert
        Assert.Equal("primary", exception.AccountId);
        Assert.Equal(["SCRAM-SHA-256"], exception.PermittedMechanismNames);
        Assert.False(client.AuthenticateCalled);
        Assert.Empty(client.AuthenticationMechanisms);
        Assert.Equal(1, client.DisposeCount);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_ServerAdvertisesNoPermittedMechanismButClearTextIsPermitted_AuthenticatesWithAnEmptySet()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var factory = CreateFactory(resilience, client, CreateSelectedFolder());
        client.AuthenticationMechanisms.Add("XOAUTH2");

        // Act
        await using var session = await factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            CreatePolicy(MailConnectionSecurity.TlsOnConnect, MailAuthenticationMechanism.Login),
            CancellationToken.None);

        // Assert
        Assert.True(client.AuthenticateCalled);
        Assert.Empty(client.MechanismsWhenAuthenticated);
    }

    [Fact]
    public async Task GetEmailBatchAfterAsync_EmptyFolder_DoesNotCheckpointFutureUid()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        folder.UidNext.Returns(new UniqueId(1));
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var batch = await session.GetEmailBatchAfterAsync(null, 100, MailSynchronizationWindow.Unbounded, CancellationToken.None);

        // Assert
        Assert.Null(batch.InspectedThroughUid);
        Assert.False(batch.HasMore);
        await folder.DidNotReceive().SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEmailBatchAfterAsync_NonUtcEnvelopeDate_NormalizesSentAtToUtc()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        var summary = Substitute.For<IMessageSummary>();
        var uid = new UniqueId(10);
        summary.UniqueId.Returns(uid);
        summary.Envelope.Returns(new Envelope
        {
            Date = new DateTimeOffset(2026, 7, 24, 8, 30, 0, TimeSpan.FromHours(2)),
            MessageId = "message@example.test",
            Subject = "Subject",
        });
        summary.Size.Returns(123U);
        folder.UidNext.Returns(new UniqueId(11));
        folder.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([uid]);
        folder.FetchAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IFetchRequest>(),
            Arg.Any<CancellationToken>()).Returns([summary]);
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var batch = await session.GetEmailBatchAfterAsync(null, 100, MailSynchronizationWindow.Unbounded, CancellationToken.None);

        // Assert
        var metadata = Assert.Single(batch.Emails);
        Assert.Equal(TimeSpan.Zero, metadata.SentAt!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 7, 24, 6, 30, 0, TimeSpan.Zero), metadata.SentAt);
    }

    /// <summary>A half-established connection is unusable, so it is closed rather than asked for a graceful logout it may never answer.</summary>
    [Fact]
    public async Task OpenReadOnlyAsync_FolderOpenFails_AbandonsTheConnectionWithoutASecondCommand()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        var factory = CreateFactory(resilience, client, folder);
        client.AuthenticationMechanisms.Add("PLAIN");
        folder.OpenAsync(FolderAccess.ReadOnly, Arg.Any<CancellationToken>()).Returns<Task>(_ => throw new InvalidOperationException("missing folder"));

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None));

        // Assert
        Assert.Equal("missing folder", exception.Message);
        Assert.Equal(1, client.GetFolderAsyncCount);
        Assert.Equal(0, client.DisconnectCount);
        Assert.Equal(1, client.DisposeCount);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_FolderOpenAndCleanupFail_PreservesTheFolderOpenFailure()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        var factory = CreateFactory(resilience, client, folder);
        var folderOpenException = new InvalidOperationException("folder open failed");
        client.AuthenticationMechanisms.Add("PLAIN");
        client.DisposeException = new IOException("dispose failed");
        folder.OpenAsync(FolderAccess.ReadOnly, Arg.Any<CancellationToken>()).Returns<Task>(_ => throw folderOpenException);

        // Act
        var observedException = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None));

        // Assert
        Assert.Same(folderOpenException, observedException);
        Assert.Equal(1, client.DisposeCount);
        client.DisposeException = null;
    }

    [Fact]
    public async Task DisposeAsync_DisconnectFails_StillDisposesClientAndPreservesDisconnectFailure()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var session = await OpenSessionAsync(resilience, client, CreateSelectedFolder());
        client.DisconnectException = new IOException("disconnect failed");

        // Act
        var observedException = await Assert.ThrowsAsync<IOException>(() => session.DisposeAsync().AsTask());

        // Assert
        Assert.Equal("disconnect failed", observedException.Message);
        Assert.Equal(1, client.DisconnectCount);
        Assert.Equal(1, client.DisposeCount);
        client.DisconnectException = null;
    }

    /// <summary>A previous generation of the same alias is as foreign as another account, which is the whole point of the generation.</summary>
    [Theory]
    [InlineData("secondary", "inbox", 1, 7U)]
    [InlineData("primary", "archive", 1, 7U)]
    [InlineData("primary", "inbox", 2, 7U)]
    [InlineData("primary", "inbox", 1, 8U)]
    public async Task FetchEmailContentWithoutSettingSeenAsync_ForeignOccurrence_RejectsBeforeRemoteFetch(
        string occurrenceAccountId,
        string occurrenceFolderAlias,
        int occurrenceGeneration,
        uint occurrenceUidValidity)
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        await using var session = await OpenSessionAsync(resilience, client, folder);
        var foreignOccurrence = EmailOccurrenceId.Create(
            MailAccountId.Create(occurrenceAccountId),
            new MailFolderResolutionId(
                MailFolderAlias.Create(occurrenceFolderAlias),
                MailFolderResolutionGeneration.Create(occurrenceGeneration)),
            ImapUidValidity.Create(occurrenceUidValidity),
            ImapUid.Create(10));

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => session.FetchEmailContentWithoutSettingSeenAsync(
            foreignOccurrence,
            1024,
            CancellationToken.None));

        // Assert
        Assert.Equal("occurrenceId", exception.ParamName);
        await folder.DidNotReceive().GetStreamAsync(Arg.Any<UniqueId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadOnlyAsync_Always_SelectsFolderWithReadOnlyAccess()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();

        // Act
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Assert
        await folder.Received(1).OpenAsync(FolderAccess.ReadOnly, Arg.Any<CancellationToken>());
        await folder.DidNotReceive().OpenAsync(FolderAccess.ReadWrite, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadOnlyAsync_FolderOpened_ErasesTheResolvedPasswordMaterial()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var settingsProvider = CreateSettingsProvider(out var resolvedMaterial);
        client.AuthenticationMechanisms.Add("PLAIN");
        client.Folder = CreateSelectedFolder();
        var factory = CreateFactory(resilience, () => client.Client, settingsProvider);

        // Act
        await using var session = await factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None);

        // Assert
        var material = Assert.Single(resolvedMaterial);
        Assert.Throws<ObjectDisposedException>(() => material.Password.RevealAsString());
    }

    [Fact]
    public async Task OpenReadOnlyAsync_AccountTrustingAnAdditionalAuthority_InstallsTheTrustDecisionBeforeConnecting()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        using var authority = TestCertificates.CreateCertificateAuthority("MailMcp Test Root");
        using var anchor = TestCertificates.WithoutPrivateKey(authority);
        var settingsProvider = CreateSettingsProvider(out _, anchor);
        client.AuthenticationMechanisms.Add("PLAIN");
        client.Folder = CreateSelectedFolder();
        var factory = CreateFactory(resilience, () => client.Client, settingsProvider);

        // Act
        await using var session = await factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None);

        // Assert
        Assert.NotNull(client.ValidationCallbackWhenConnected);
    }

    /// <summary>Without a configured authority the client keeps its own validating default rather than being handed a callback.</summary>
    [Fact]
    public async Task OpenReadOnlyAsync_AccountTrustingTheSystemStore_LeavesTheClientValidationUntouched()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();

        // Act
        await using var session = await OpenSessionAsync(resilience, client, CreateSelectedFolder());

        // Assert
        Assert.Null(client.ValidationCallbackWhenConnected);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_ConnectionFails_StillErasesTheResolvedPasswordMaterial()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var settingsProvider = CreateSettingsProvider(out var resolvedMaterial);
        client.ConnectException = new IOException("connect failed");
        var factory = CreateFactory(resilience, () => client.Client, settingsProvider);

        // Act
        await Assert.ThrowsAsync<MailboxUnavailableException>(() => factory.OpenReadOnlyAsync(
            PrimaryAccount,
            InboxFolder,
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None));

        // Assert
        var material = Assert.Single(resolvedMaterial);
        Assert.Throws<ObjectDisposedException>(() => material.Password.RevealAsString());
    }

    [Fact]
    public async Task FetchEmailContentWithoutSettingSeenAsync_ValidOccurrence_ReturnsContentWithoutRequestingAnySeenSettingOperation()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        var rawMime = "From: sender@example.test\r\nSubject: Subject\r\n\r\nBody"u8.ToArray();
        folder.GetStreamAsync(new UniqueId(10), Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream(rawMime));
        await using var session = await OpenSessionAsync(resilience, client, folder);
        var occurrenceId = CreateOccurrenceId(10);

        // Act
        var fetch = await session.FetchEmailContentWithoutSettingSeenAsync(occurrenceId, 1024, CancellationToken.None);

        // Assert
        Assert.Equal(RemoteEmailContentFetchOutcome.Retrieved, fetch.Outcome);
        Assert.Equal(occurrenceId, fetch.Content!.OccurrenceId);
        Assert.Equal(rawMime, fetch.Content.RawMime.ToArray());

        // GetStreamAsync(uid) is MailKit's BODY.PEEK[] retrieval; StoreAsync is the only IMailFolder member able to change
        // flags, and a read-write reselection would let the server set \Seen implicitly.
        await folder.Received(1).GetStreamAsync(new UniqueId(10), Arg.Any<CancellationToken>());
        await folder.DidNotReceive().StoreAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IStoreFlagsRequest>(), Arg.Any<CancellationToken>());
        await folder.DidNotReceive().OpenAsync(FolderAccess.ReadWrite, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchEmailContentWithoutSettingSeenAsync_ContentStreamExceedsLimit_ReportsTheSizeLimitWithoutContent()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        folder.GetStreamAsync(new UniqueId(10), Arg.Any<CancellationToken>()).Returns(_ => new MemoryStream(new byte[2048]));
        await using var session = await OpenSessionAsync(resilience, client, folder);
        var occurrenceId = CreateOccurrenceId(10);

        // Act
        var fetch = await session.FetchEmailContentWithoutSettingSeenAsync(occurrenceId, 1024, CancellationToken.None);

        // Assert
        Assert.Equal(RemoteEmailContentFetchOutcome.ExceededSizeLimit, fetch.Outcome);
        Assert.Null(fetch.Content);
        await folder.DidNotReceive().StoreAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IStoreFlagsRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEmailBatchAfterAsync_CheckpointAtHighestPossibleUid_StopsWithoutSearchingBeyondTheUidSpace()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        folder.UidNext.Returns(new UniqueId(uint.MaxValue));
        await using var session = await OpenSessionAsync(resilience, client, folder);
        var exhaustedUid = ImapUid.Create(uint.MaxValue);

        // Act
        var batch = await session.GetEmailBatchAfterAsync(exhaustedUid, 100, MailSynchronizationWindow.Unbounded, CancellationToken.None);

        // Assert
        Assert.Empty(batch.Emails);
        Assert.False(batch.HasMore);
        Assert.Equal(exhaustedUid, batch.InspectedThroughUid);
        await folder.DidNotReceive().SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUidValidityAsync_OpenFolder_ReturnsSelectedFolderUidValidity()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        await using var session = await OpenSessionAsync(resilience, client, CreateSelectedFolder());

        // Act
        var uidValidity = await session.GetUidValidityAsync(CancellationToken.None);

        // Assert
        Assert.Equal(ImapUidValidity.Create(7), uidValidity);
    }

    [Fact]
    public async Task GetEmailBatchAfterAsync_SparseUidsExceedingBatchSize_BoundsBatchByMessageCountAndCheckpointsLastFetchedUid()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        folder.UidNext.Returns(new UniqueId(1001));
        folder.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([new UniqueId(100), new UniqueId(400), new UniqueId(900)]);
        folder.FetchAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IFetchRequest>(),
            Arg.Any<CancellationToken>()).Returns(callInfo => CreateSummaries(callInfo.Arg<IList<UniqueId>>() ?? []));
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var batch = await session.GetEmailBatchAfterAsync(null, 2, MailSynchronizationWindow.Unbounded, CancellationToken.None);

        // Assert
        Assert.Equal([100U, 400U], batch.Emails.Select(message => message.OccurrenceId.Uid.Value));
        Assert.True(batch.HasMore);
        Assert.Equal(400U, batch.InspectedThroughUid!.Value.Value);
    }

    [Fact]
    public async Task GetEmailBatchAfterAsync_FewerMatchesThanBatchSize_CheckpointsThroughHighestAssignedUid()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        folder.UidNext.Returns(new UniqueId(1001));
        folder.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([new UniqueId(900)]);
        folder.FetchAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IFetchRequest>(),
            Arg.Any<CancellationToken>()).Returns(callInfo => CreateSummaries(callInfo.Arg<IList<UniqueId>>() ?? []));
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var batch = await session.GetEmailBatchAfterAsync(ImapUid.Create(400), 2, MailSynchronizationWindow.Unbounded, CancellationToken.None);

        // Assert
        Assert.Equal([900U], batch.Emails.Select(message => message.OccurrenceId.Uid.Value));
        Assert.False(batch.HasMore);
        Assert.Equal(1000U, batch.InspectedThroughUid!.Value.Value);
    }

    /// <summary>The bound has to be part of the search the server answers, or an excluded email would still be fetched.</summary>
    [Fact]
    public async Task GetEmailBatchAfterAsync_BoundedWindow_SendsTheUidRangeAndTheArrivalDateInOneSearch()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        folder.UidNext.Returns(new UniqueId(1001));
        folder.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([new UniqueId(900)]);
        folder.FetchAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IFetchRequest>(),
            Arg.Any<CancellationToken>()).Returns(callInfo => CreateSummaries(callInfo.Arg<IList<UniqueId>>() ?? []));
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var batch = await session.GetEmailBatchAfterAsync(
            ImapUid.Create(400),
            100,
            MailSynchronizationWindow.EmailsReceivedSince(new DateOnly(2024, 1, 1)),
            CancellationToken.None);

        // Assert
        Assert.Equal([900U], batch.Emails.Select(email => email.OccurrenceId.Uid.Value));
        var searchQuery = Assert.IsType<BinarySearchQuery>(CaptureSearchQuery(folder));
        Assert.Equal(SearchTerm.And, searchQuery.Term);
        var searchedUids = Assert.IsType<UidSearchQuery>(searchQuery.Left).Uids;
        Assert.Equal(401U, searchedUids[0].Id);
        Assert.Equal(1000U, searchedUids[^1].Id);
        var dateQuery = Assert.IsType<DateSearchQuery>(searchQuery.Right);
        Assert.Equal(SearchTerm.DeliveredAfter, dateQuery.Term);
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), dateQuery.Date);
    }

    [Fact]
    public async Task GetEmailBatchAfterAsync_UnboundedWindow_SearchesTheUidRangeWithNoDateCondition()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        folder.UidNext.Returns(new UniqueId(1001));
        folder.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([]);
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        await session.GetEmailBatchAfterAsync(null, 100, MailSynchronizationWindow.Unbounded, CancellationToken.None);

        // Assert
        Assert.IsType<UidSearchQuery>(CaptureSearchQuery(folder));
    }

    /// <summary>A folder whose whole backlog is excluded must terminate, which means checkpointing through what the search covered.</summary>
    [Fact]
    public async Task GetEmailBatchAfterAsync_BoundExcludesEveryAssignedUid_ReportsTheWholeRangeInspectedWithoutFetching()
    {
        // Arrange
        using var resilience = CreateSingleAttemptResilience();
        var client = new FakeImapClient();
        var folder = CreateSelectedFolder();
        folder.UidNext.Returns(new UniqueId(1001));
        folder.SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>()).Returns([]);
        await using var session = await OpenSessionAsync(resilience, client, folder);

        // Act
        var batch = await session.GetEmailBatchAfterAsync(
            null,
            100,
            MailSynchronizationWindow.EmailsReceivedSince(new DateOnly(2026, 1, 1)),
            CancellationToken.None);

        // Assert
        Assert.Empty(batch.Emails);
        Assert.False(batch.HasMore);
        Assert.Equal(1000U, batch.InspectedThroughUid!.Value.Value);
        await folder.DidNotReceive().FetchAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IFetchRequest>(), Arg.Any<CancellationToken>());
        await folder.DidNotReceive().StoreAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IStoreFlagsRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Reads the one query the session issued, so a test asserts on what the server was actually asked.</summary>
    private static SearchQuery CaptureSearchQuery(IMailFolder folder) =>
        Assert.Single(folder.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(IMailFolder.SearchAsync))
            .Select(call => (SearchQuery)call.GetArguments()[0]!));

    // Building a substitute configures NSubstitute's ambient call context rather than only the returned object, so the
    // construction stays in a loop instead of a Select whose deferred execution could interleave it with another
    // substitute's setup.
    private static List<IMessageSummary> CreateSummaries(IList<UniqueId> uids)
    {
        var summaries = new List<IMessageSummary>(uids.Count);
        foreach (var uid in uids)
        {
            summaries.Add(CreateSummary(uid));
        }

        return summaries;
    }

    private static IMessageSummary CreateSummary(UniqueId uid)
    {
        var summary = Substitute.For<IMessageSummary>();
        summary.UniqueId.Returns(uid);
        summary.Envelope.Returns(new Envelope { Subject = $"Subject {uid.Id}" });
        summary.Size.Returns(128U);

        return summary;
    }

}
