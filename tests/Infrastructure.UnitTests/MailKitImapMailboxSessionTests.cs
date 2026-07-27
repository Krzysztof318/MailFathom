// Copyright © 2026 Krzysztof Kasprowicz

using MailKit;
using MailKit.Search;
using MailKit.Security;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Mail.MailKit;
using MailMcp.Infrastructure.Secrets;
using NSubstitute;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class MailKitImapMailboxSessionTests
{
    private static readonly MailTransportSecurityPolicy TlsOnConnectWithPlainPolicy =
        CreatePolicy(MailConnectionSecurity.TlsOnConnect, MailAuthenticationMechanism.Plain);

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
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        var factory = CreateFactory(client, folder);
        client.AuthenticationMechanisms.Add("SCRAM-SHA-256");

        // Act
        await using var session = await factory.OpenReadOnlyAsync(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            CreatePolicy(connectionSecurity, MailAuthenticationMechanism.ScramSha256),
            CancellationToken.None);

        // Assert
        Assert.Equal(expectedSocketOptions, client.ConnectSocketOptions);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_ServerAdvertisesMechanismsOutsideThePolicy_RemovesThemBeforeAuthenticating()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        var factory = CreateFactory(client, folder);
        client.AuthenticationMechanisms.Add("PLAIN");
        client.AuthenticationMechanisms.Add("LOGIN");
        client.AuthenticationMechanisms.Add("SCRAM-SHA-256");

        // Act
        await using var session = await factory.OpenReadOnlyAsync(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            CreatePolicy(MailConnectionSecurity.TlsOnConnect, MailAuthenticationMechanism.ScramSha256),
            CancellationToken.None);

        // Assert
        Assert.Equal(["SCRAM-SHA-256"], client.MechanismsWhenAuthenticated);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_ServerAdvertisesNoPermittedMechanism_FailsWithoutAuthenticatingOrWideningTheSet()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        var factory = CreateFactory(client, folder);
        client.IsConnected = true;
        client.AuthenticationMechanisms.Add("LOGIN");

        // Act
        var exception = await Assert.ThrowsAsync<MailAuthenticationMechanismUnavailableException>(() => factory.OpenReadOnlyAsync(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
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
    public async Task GetEmailBatchAfterAsync_EmptyFolder_DoesNotCheckpointFutureUid()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        folder.UidValidity.Returns(7U);
        folder.UidNext.Returns(new UniqueId(1));
        await using var session = new MailKitImapMailboxSession(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            client,
            folder);

        // Act
        var batch = await session.GetEmailBatchAfterAsync(null, 100, CancellationToken.None);

        // Assert
        Assert.Null(batch.InspectedThroughUid);
        Assert.False(batch.HasMore);
        await folder.DidNotReceive().SearchAsync(Arg.Any<SearchQuery>(), CancellationToken.None);
    }

    [Fact]
    public async Task GetEmailBatchAfterAsync_NonUtcEnvelopeDate_NormalizesSentAtToUtc()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
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
        folder.UidValidity.Returns(7U);
        folder.UidNext.Returns(new UniqueId(11));
        folder.SearchAsync(Arg.Any<SearchQuery>(), CancellationToken.None).Returns([uid]);
        folder.FetchAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IFetchRequest>(),
            CancellationToken.None).Returns([summary]);
        await using var session = new MailKitImapMailboxSession(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            client,
            folder);

        // Act
        var batch = await session.GetEmailBatchAfterAsync(null, 100, CancellationToken.None);

        // Assert
        var metadata = Assert.Single(batch.Emails);
        Assert.Equal(TimeSpan.Zero, metadata.SentAt!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 7, 24, 6, 30, 0, TimeSpan.Zero), metadata.SentAt);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_FolderOpenFails_DisposesClientBeforeRethrowing()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        var settingsProvider = CreateSettingsProvider();
        client.IsConnected = true;
        client.AuthenticationMechanisms.Add("PLAIN");
        client.Folder = folder;
        folder.OpenAsync(FolderAccess.ReadOnly, CancellationToken.None).Returns<Task>(_ => throw new InvalidOperationException("missing folder"));
        var factory = new MailKitImapMailboxSessionFactory(() => client, settingsProvider);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.OpenReadOnlyAsync(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None));

        // Assert
        Assert.Equal("missing folder", exception.Message);
        Assert.Equal(1, client.GetFolderAsyncCount);
        Assert.Equal(1, client.DisconnectCount);
        Assert.Equal(1, client.DisposeCount);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_FolderOpenAndCleanupFail_PreservesFolderOpenExceptionAndAttemptsAllCleanup()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        var settingsProvider = CreateSettingsProvider();
        var folderOpenException = new InvalidOperationException("folder open failed");
        client.IsConnected = true;
        client.AuthenticationMechanisms.Add("PLAIN");
        client.Folder = folder;
        client.DisconnectException = new IOException("disconnect failed");
        client.DisposeException = new IOException("dispose failed");
        folder.OpenAsync(FolderAccess.ReadOnly, CancellationToken.None).Returns<Task>(_ => throw folderOpenException);
        var factory = new MailKitImapMailboxSessionFactory(() => client, settingsProvider);

        // Act
        var observedException = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.OpenReadOnlyAsync(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None));

        // Assert
        Assert.Same(folderOpenException, observedException);
        Assert.Equal(1, client.DisconnectCount);
        Assert.Equal(1, client.DisposeCount);
        client.DisposeException = null;
    }

    [Fact]
    public async Task DisposeAsync_DisconnectFails_StillDisposesClientAndPreservesDisconnectFailure()
    {
        // Arrange
        await using var client = new FakeImapClient
        {
            IsConnected = true,
            DisconnectException = new IOException("disconnect failed"),
        };
        var folder = Substitute.For<IMailFolder>();
        await using var session = new MailKitImapMailboxSession(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            client,
            folder);

        // Act
        var observedException = await Assert.ThrowsAsync<IOException>(() => session.DisposeAsync().AsTask());

        // Assert
        Assert.Equal("disconnect failed", observedException.Message);
        Assert.Equal(1, client.DisconnectCount);
        Assert.Equal(1, client.DisposeCount);
        client.DisconnectException = null;
    }

    [Theory]
    [InlineData("secondary", "INBOX", 7U)]
    [InlineData("primary", "Archive", 7U)]
    [InlineData("primary", "INBOX", 8U)]
    public async Task FetchEmailContentWithoutSettingSeenAsync_ForeignOccurrence_RejectsBeforeRemoteFetch(
        string occurrenceAccountId,
        string occurrenceFolderName,
        uint occurrenceUidValidity)
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        folder.UidValidity.Returns(7U);
        await using var session = new MailKitImapMailboxSession(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            client,
            folder);
        var foreignOccurrence = EmailOccurrenceId.Create(
            MailAccountId.Create(occurrenceAccountId),
            MailFolderName.Create(occurrenceFolderName),
            ImapUidValidity.Create(occurrenceUidValidity),
            ImapUid.Create(10));

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => session.FetchEmailContentWithoutSettingSeenAsync(
            foreignOccurrence,
            1024,
            CancellationToken.None));

        // Assert
        Assert.Equal("occurrenceId", exception.ParamName);
        await folder.DidNotReceive().GetStreamAsync(Arg.Any<UniqueId>(), CancellationToken.None);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_Always_SelectsFolderWithReadOnlyAccess()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        var settingsProvider = CreateSettingsProvider();
        client.AuthenticationMechanisms.Add("PLAIN");
        client.Folder = folder;
        var factory = new MailKitImapMailboxSessionFactory(() => client, settingsProvider);

        // Act
        await using var session = await factory.OpenReadOnlyAsync(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None);

        // Assert
        await folder.Received(1).OpenAsync(FolderAccess.ReadOnly, CancellationToken.None);
        await folder.DidNotReceive().OpenAsync(FolderAccess.ReadWrite, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenReadOnlyAsync_FolderOpened_ErasesTheResolvedPasswordMaterial()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        var settingsProvider = CreateSettingsProvider(out var resolvedSecrets);
        client.AuthenticationMechanisms.Add("PLAIN");
        client.Folder = folder;
        var factory = new MailKitImapMailboxSessionFactory(() => client, settingsProvider);

        // Act
        await using var session = await factory.OpenReadOnlyAsync(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None);

        // Assert
        var secrets = Assert.Single(resolvedSecrets);
        Assert.Throws<ObjectDisposedException>(() => secrets.Password.RevealAsString());
    }

    [Fact]
    public async Task OpenReadOnlyAsync_ConnectionFails_StillErasesTheResolvedPasswordMaterial()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var settingsProvider = CreateSettingsProvider(out var resolvedSecrets);
        client.ConnectException = new IOException("connect failed");
        var factory = new MailKitImapMailboxSessionFactory(() => client, settingsProvider);

        // Act
        await Assert.ThrowsAsync<IOException>(() => factory.OpenReadOnlyAsync(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            TlsOnConnectWithPlainPolicy,
            CancellationToken.None));

        // Assert
        var secrets = Assert.Single(resolvedSecrets);
        Assert.Throws<ObjectDisposedException>(() => secrets.Password.RevealAsString());
    }

    [Fact]
    public async Task FetchEmailContentWithoutSettingSeenAsync_ValidOccurrence_ReturnsContentWithoutRequestingAnySeenSettingOperation()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        var rawMime = "From: sender@example.test\r\nSubject: Subject\r\n\r\nBody"u8.ToArray();
        folder.UidValidity.Returns(7U);
        folder.GetStreamAsync(new UniqueId(10), CancellationToken.None).Returns(_ => new MemoryStream(rawMime));
        await using var session = new MailKitImapMailboxSession(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            client,
            folder);
        var occurrenceId = EmailOccurrenceId.Create(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            ImapUidValidity.Create(7),
            ImapUid.Create(10));

        // Act
        var content = await session.FetchEmailContentWithoutSettingSeenAsync(occurrenceId, 1024, CancellationToken.None);

        // Assert
        Assert.Equal(occurrenceId, content.OccurrenceId);
        Assert.Equal(rawMime, content.RawMime.ToArray());

        // GetStreamAsync(uid) is MailKit's BODY.PEEK[] retrieval; StoreAsync is the only IMailFolder member able to change
        // flags, and a read-write reselection would let the server set \Seen implicitly.
        await folder.Received(1).GetStreamAsync(new UniqueId(10), CancellationToken.None);
        await folder.DidNotReceive().StoreAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IStoreFlagsRequest>(), Arg.Any<CancellationToken>());
        await folder.DidNotReceive().OpenAsync(FolderAccess.ReadWrite, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchEmailContentWithoutSettingSeenAsync_ContentStreamExceedsLimit_ThrowsMessageContentTooLarge()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        folder.UidValidity.Returns(7U);
        folder.GetStreamAsync(new UniqueId(10), CancellationToken.None).Returns(_ => new MemoryStream(new byte[2048]));
        await using var session = new MailKitImapMailboxSession(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            client,
            folder);
        var occurrenceId = EmailOccurrenceId.Create(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            ImapUidValidity.Create(7),
            ImapUid.Create(10));

        // Act
        var exception = await Assert.ThrowsAsync<EmailContentTooLargeException>(() => session.FetchEmailContentWithoutSettingSeenAsync(
            occurrenceId,
            1024,
            CancellationToken.None));

        // Assert
        Assert.Equal(occurrenceId, exception.OccurrenceId);
        Assert.Equal(1024, exception.MaxAllowedOctets);
        await folder.DidNotReceive().StoreAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IStoreFlagsRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetEmailBatchAfterAsync_CheckpointAtHighestPossibleUid_StopsWithoutSearchingBeyondTheUidSpace()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        folder.UidValidity.Returns(7U);
        folder.UidNext.Returns(new UniqueId(uint.MaxValue));
        await using var session = new MailKitImapMailboxSession(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            client,
            folder);
        var exhaustedUid = ImapUid.Create(uint.MaxValue);

        // Act
        var batch = await session.GetEmailBatchAfterAsync(exhaustedUid, 100, CancellationToken.None);

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
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        folder.UidValidity.Returns(7U);
        await using var session = new MailKitImapMailboxSession(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            client,
            folder);

        // Act
        var uidValidity = await session.GetUidValidityAsync(CancellationToken.None);

        // Assert
        Assert.Equal(ImapUidValidity.Create(7), uidValidity);
    }

    [Fact]
    public async Task GetEmailBatchAfterAsync_SparseUidsExceedingBatchSize_BoundsBatchByMessageCountAndCheckpointsLastFetchedUid()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        folder.UidValidity.Returns(7U);
        folder.UidNext.Returns(new UniqueId(1001));
        folder.SearchAsync(Arg.Any<SearchQuery>(), CancellationToken.None).Returns([new UniqueId(100), new UniqueId(400), new UniqueId(900)]);
        folder.FetchAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IFetchRequest>(),
            CancellationToken.None).Returns(callInfo => CreateSummaries(callInfo.Arg<IList<UniqueId>>() ?? []));
        await using var session = new MailKitImapMailboxSession(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            client,
            folder);

        // Act
        var batch = await session.GetEmailBatchAfterAsync(null, 2, CancellationToken.None);

        // Assert
        Assert.Equal([100U, 400U], batch.Emails.Select(message => message.OccurrenceId.Uid.Value));
        Assert.True(batch.HasMore);
        Assert.Equal(400U, batch.InspectedThroughUid!.Value.Value);
    }

    [Fact]
    public async Task GetEmailBatchAfterAsync_FewerMatchesThanBatchSize_CheckpointsThroughHighestAssignedUid()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        folder.UidValidity.Returns(7U);
        folder.UidNext.Returns(new UniqueId(1001));
        folder.SearchAsync(Arg.Any<SearchQuery>(), CancellationToken.None).Returns([new UniqueId(900)]);
        folder.FetchAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IFetchRequest>(),
            CancellationToken.None).Returns(callInfo => CreateSummaries(callInfo.Arg<IList<UniqueId>>() ?? []));
        await using var session = new MailKitImapMailboxSession(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
            client,
            folder);

        // Act
        var batch = await session.GetEmailBatchAfterAsync(ImapUid.Create(400), 2, CancellationToken.None);

        // Assert
        Assert.Equal([900U], batch.Emails.Select(message => message.OccurrenceId.Uid.Value));
        Assert.False(batch.HasMore);
        Assert.Equal(1000U, batch.InspectedThroughUid!.Value.Value);
    }

    private static List<IMessageSummary> CreateSummaries(IList<UniqueId> uids) => [.. uids.Select(CreateSummary)];

    private static IMessageSummary CreateSummary(UniqueId uid)
    {
        var summary = Substitute.For<IMessageSummary>();
        summary.UniqueId.Returns(uid);
        summary.Envelope.Returns(new Envelope { Subject = $"Subject {uid.Id}" });
        summary.Size.Returns(128U);

        return summary;
    }

    private static MailTransportSecurityPolicy CreatePolicy(
        MailConnectionSecurity connectionSecurity,
        MailAuthenticationMechanism permittedMechanism) => MailTransportSecurityPolicy.Create(
            connectionSecurity,
            MailAuthenticationPolicy.Create(
                [permittedMechanism],
                allowInsecureConnection: !MailTransportSecurityPolicy.GuaranteesEncryptedChannel(connectionSecurity),
                allowClearTextAuthenticationOverUnencryptedConnection: permittedMechanism.TransmitsCredentialsInClearText),
            MailServerCertificateTrust.SystemTrustStore,
            trustedCertificateAuthorityReference: null);

    private static MailKitImapMailboxSessionFactory CreateFactory(FakeImapClient client, IMailFolder folder)
    {
        var settingsProvider = CreateSettingsProvider();
        client.Folder = folder;

        return new MailKitImapMailboxSessionFactory(() => client, settingsProvider);
    }

    private static IImapAccountSettingsProvider CreateSettingsProvider() => CreateSettingsProvider(out _);

    private static IImapAccountSettingsProvider CreateSettingsProvider(out List<MailAccountSecrets> resolvedSecrets)
    {
        var issuedSecrets = new List<MailAccountSecrets>();
        var settingsProvider = Substitute.For<IImapAccountSettingsProvider>();
        settingsProvider.GetSettingsAsync("primary", Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var secrets = new MailAccountSecrets(ResolvedSecret.FromText("password"));
            issuedSecrets.Add(secrets);

            return Task.FromResult(new ImapAccountSettings("primary", "imap.example.test", 993, "user", secrets));
        });

        resolvedSecrets = issuedSecrets;

        return settingsProvider;
    }

    private sealed class FakeImapClient : IMailKitImapClient
    {
        public bool IsConnected { get; set; }

        public ISet<string> AuthenticationMechanisms { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> MechanismsWhenAuthenticated { get; private set; } = [];

        public bool AuthenticateCalled { get; private set; }

        public IMailFolder? Folder { get; set; }

        public int DisconnectCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int GetFolderAsyncCount { get; private set; }

        public Exception? ConnectException { get; set; }

        public Exception? DisconnectException { get; set; }

        public Exception? DisposeException { get; set; }

        public SecureSocketOptions? ConnectSocketOptions { get; private set; }

        public Task ConnectAsync(
            string host,
            int port,
            SecureSocketOptions options,
            CancellationToken cancellationToken)
        {
            this.ConnectSocketOptions = options;
            if (this.ConnectException is not null)
            {
                throw this.ConnectException;
            }

            return Task.CompletedTask;
        }

        public Task AuthenticateAsync(
            string userName,
            string password,
            CancellationToken cancellationToken)
        {
            this.AuthenticateCalled = true;
            this.MechanismsWhenAuthenticated = [.. this.AuthenticationMechanisms.Order(StringComparer.Ordinal)];

            return Task.CompletedTask;
        }

        public Task<IMailFolder> GetFolderAsync(
            string path,
            CancellationToken cancellationToken)
        {
            this.GetFolderAsyncCount++;
            return Task.FromResult(this.Folder ?? throw new InvalidOperationException("No test folder configured."));
        }

        public Task DisconnectAsync(
            bool quit,
            CancellationToken cancellationToken)
        {
            this.DisconnectCount++;
            if (this.DisconnectException is not null)
            {
                throw this.DisconnectException;
            }

            this.IsConnected = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            this.DisposeCount++;
            if (this.DisposeException is not null)
            {
                throw this.DisposeException;
            }

            return ValueTask.CompletedTask;
        }
    }

}
