// Copyright © 2026 Krzysztof Kasprowicz

using MailKit;
using MailKit.Search;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Messages;
using MailMcp.Infrastructure.Mail.MailKit;
using NSubstitute;
using Xunit;

namespace MailMcp.Infrastructure.UnitTests;

public sealed class MailKitImapMailboxSessionTests
{
    [Fact]
    public async Task GetMessageBatchAfterAsync_EmptyFolder_DoesNotCheckpointFutureUid()
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
        var batch = await session.GetMessageBatchAfterAsync(null, 100, CancellationToken.None);

        // Assert
        Assert.Null(batch.InspectedThroughUid);
        Assert.False(batch.HasMore);
        await folder.DidNotReceive().SearchAsync(Arg.Any<SearchQuery>(), CancellationToken.None);
    }

    [Fact]
    public async Task GetMessageBatchAfterAsync_NonUtcEnvelopeDate_NormalizesSentAtToUtc()
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
        var batch = await session.GetMessageBatchAfterAsync(null, 100, CancellationToken.None);

        // Assert
        var metadata = Assert.Single(batch.Messages);
        Assert.Equal(TimeSpan.Zero, metadata.SentAt!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 7, 24, 6, 30, 0, TimeSpan.Zero), metadata.SentAt);
    }

    [Fact]
    public async Task OpenReadOnlyAsync_FolderOpenFails_DisposesClientBeforeRethrowing()
    {
        // Arrange
        await using var client = new FakeImapClient();
        var folder = Substitute.For<IMailFolder>();
        var settingsProvider = Substitute.For<IMailKitImapAccountSettingsProvider>();
        settingsProvider.GetSettings("primary").Returns(new MailKitImapAccountSettings("primary", "imap.example.test", 993, UseTls: true, "user", "password"));
        client.IsConnected = true;
        client.Folder = folder;
        folder.OpenAsync(FolderAccess.ReadOnly, CancellationToken.None).Returns<Task>(_ => throw new InvalidOperationException("missing folder"));
        var factory = new MailKitImapMailboxSessionFactory(() => client, settingsProvider);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.OpenReadOnlyAsync(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
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
        var settingsProvider = Substitute.For<IMailKitImapAccountSettingsProvider>();
        var folderOpenException = new InvalidOperationException("folder open failed");
        settingsProvider.GetSettings("primary").Returns(new MailKitImapAccountSettings("primary", "imap.example.test", 993, UseTls: true, "user", "password"));
        client.IsConnected = true;
        client.Folder = folder;
        client.DisconnectException = new IOException("disconnect failed");
        client.DisposeException = new IOException("dispose failed");
        folder.OpenAsync(FolderAccess.ReadOnly, CancellationToken.None).Returns<Task>(_ => throw folderOpenException);
        var factory = new MailKitImapMailboxSessionFactory(() => client, settingsProvider);

        // Act
        var observedException = await Assert.ThrowsAsync<InvalidOperationException>(() => factory.OpenReadOnlyAsync(
            MailAccountId.Create("primary"),
            MailFolderName.Create("INBOX"),
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
    public async Task FetchMessageContentWithoutSettingSeenAsync_ForeignOccurrence_RejectsBeforeRemoteFetch(
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
        var foreignOccurrence = MessageOccurrenceId.Create(
            MailAccountId.Create(occurrenceAccountId),
            MailFolderName.Create(occurrenceFolderName),
            ImapUidValidity.Create(occurrenceUidValidity),
            ImapUid.Create(10));

        // Act
        var exception = await Assert.ThrowsAsync<ArgumentException>(() => session.FetchMessageContentWithoutSettingSeenAsync(
            foreignOccurrence,
            1024,
            CancellationToken.None));

        // Assert
        Assert.Equal("occurrenceId", exception.ParamName);
        await folder.DidNotReceive().GetStreamAsync(Arg.Any<UniqueId>(), CancellationToken.None);
    }

    private sealed class FakeImapClient : IMailKitImapClient
    {
        public bool IsConnected { get; set; }

        public IMailFolder? Folder { get; set; }

        public int DisconnectCount { get; private set; }

        public int DisposeCount { get; private set; }

        public int GetFolderAsyncCount { get; private set; }

        public Exception? DisconnectException { get; set; }

        public Exception? DisposeException { get; set; }

        public Task ConnectAsync(
            string host,
            int port,
            MailKit.Security.SecureSocketOptions options,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task AuthenticateAsync(
            string userName,
            string password,
            CancellationToken cancellationToken) => Task.CompletedTask;

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
