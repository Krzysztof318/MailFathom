// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.SyntheticMail.Configuration;
using MailFathom.SyntheticMail.Delivery;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using NSubstitute;
using Xunit;

namespace MailFathom.SyntheticMail.UnitTests.Delivery;

/// <summary>What the IMAP session does with the credential, the socket, the inbox, and the Sent folder.</summary>
/// <remarks>
/// Driven through MailKit's own published <see cref="IImapClient" /> and <see cref="IMailFolder" /> rather than a
/// hand-written copy of them, which is what lets the decisions worth protecting be observed: that the connection
/// cannot continue unencrypted, that the inbox is opened read-only, that nothing asked of it can set <c>\Seen</c>, and
/// that an appended copy lands in the folder a mailbox keeps its own mail in.
/// </remarks>
public sealed class ImapWatchedMailboxTests
{
    [Theory]
    [InlineData(nameof(MailTransportSecurity.StartTls), nameof(SecureSocketOptions.StartTls))]
    [InlineData(nameof(MailTransportSecurity.ImplicitTls), nameof(SecureSocketOptions.SslOnConnect))]
    public void ResolveSocketOptions_ASecurity_ChoosesTheOptionThatCannotContinueUnencrypted(
        string securityName,
        string expectedOptionName)
    {
        // Arrange
        var security = Enum.Parse<MailTransportSecurity>(securityName);

        // Act
        var option = ImapWatchedMailbox.ResolveSocketOptions(security);

        // Assert
        Assert.Equal(Enum.Parse<SecureSocketOptions>(expectedOptionName), option);
    }

    [Fact]
    public void ResolveSocketOptions_EverySecurity_RefusesEveryOptionThatWouldSendThePasswordInTheClear()
    {
        // Arrange
        SecureSocketOptions[] downgrading =
        [
            SecureSocketOptions.None,
            SecureSocketOptions.Auto,
            SecureSocketOptions.StartTlsWhenAvailable,
        ];

        // Act
        var chosen = Enum
            .GetValues<MailTransportSecurity>()
            .Select(ImapWatchedMailbox.ResolveSocketOptions)
            .ToArray();

        // Assert
        // Written over the whole enumeration for the reason the submission transport's own version of this is: a third
        // value added later fails here instead of quietly reintroducing the downgrade.
        Assert.NotEmpty(chosen);
        Assert.All(chosen, option => Assert.DoesNotContain(option, downgrading));
    }

    [Fact]
    public async Task OpenAsync_AMailbox_SecuresTheConnectionThenAuthenticatesThenOpensTheInboxReadOnly()
    {
        // Arrange
        var inbox = Substitute.For<IMailFolder>();
        var client = Client(inbox);
        var calls = new List<string>();

        client
            .When(substitute => substitute.ConnectAsync(
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<SecureSocketOptions>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => calls.Add("connect"));
        client
            .When(substitute => substitute.AuthenticateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()))
            .Do(_ => calls.Add("authenticate"));
        inbox
            .When(substitute => substitute.OpenAsync(Arg.Any<FolderAccess>(), Arg.Any<CancellationToken>()))
            .Do(call => calls.Add($"open:{call.Arg<FolderAccess>()}"));

        // Act
        await using var mailbox = new ImapWatchedMailbox(Account(), client);
        await mailbox.OpenAsync(TestContext.Current.CancellationToken);

        // Assert
        // Read-only rather than read-write, because a folder opened for writing is one an accidental fetch could set
        // `\Seen` through, and a development mailbox that arrives already read shows nothing on any unread screen.
        Assert.Equal(["connect", "authenticate", $"open:{FolderAccess.ReadOnly}"], calls);
        await client.Received(1).ConnectAsync(
            "imap.example.test",
            993,
            SecureSocketOptions.SslOnConnect,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OpenAsync_AServerThatRefusesTheCredential_IsReportedWithoutTheCredentialInIt()
    {
        // Arrange
        var client = Client(Substitute.For<IMailFolder>());

        client
            .AuthenticateAsync("developer@example.com", "not-a-real-password", Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new AuthenticationException("AUTHENTICATIONFAILED")));

        await using var mailbox = new ImapWatchedMailbox(Account(), client);

        // Act
        var failure = await Assert.ThrowsAsync<SyntheticMailFailure>(
            () => mailbox.OpenAsync(TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("refused the credential", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("not-a-real-password", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsync_AServerAdvertisingNoSentFolder_IsRefusedNamingTheSettingThatAnswersIt()
    {
        // Arrange
        var client = Client(Substitute.For<IMailFolder>(), sent: null);

        await using var mailbox = new ImapWatchedMailbox(Account(), client);

        // Act
        var failure = await Assert.ThrowsAsync<SyntheticMailFailure>(
            () => mailbox.OpenAsync(TestContext.Current.CancellationToken));

        // Assert
        // Refused before anything is submitted, because an exchange whose outgoing half had nowhere to go would leave
        // the mailbox holding half a thread.
        Assert.Contains("mailbox.sentFolder", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAsync_AConfiguredSentFolder_IsPreferredOverWhateverTheServerAdvertises()
    {
        // Arrange
        var advertised = Substitute.For<IMailFolder>();
        var named = Substitute.For<IMailFolder>();
        var client = Client(Substitute.For<IMailFolder>(), sent: advertised);

        client.GetFolderAsync("INBOX.Sent", Arg.Any<CancellationToken>()).Returns(Task.FromResult(named));

        await using var mailbox = new ImapWatchedMailbox(Account() with { SentFolder = "INBOX.Sent" }, client);

        // Act
        await mailbox.OpenAsync(TestContext.Current.CancellationToken);

        using var message = new MimeMessage();
        await mailbox.AppendToSentAsync(message, TestContext.Current.CancellationToken);

        // Assert
        // A developer who named a folder has already answered the question, and a server advertising a different one
        // does not overrule them.
        await named.Received(1).AppendAsync(Arg.Any<AppendRequest>(), Arg.Any<CancellationToken>());
        await advertised.DidNotReceive().AppendAsync(Arg.Any<AppendRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindDeliveredMessageIdAsync_ADeliveredCopy_ReadsTheIdentifierItsServerAssignedWithoutTouchingTheBody()
    {
        // Arrange
        var inbox = Substitute.For<IMailFolder>();
        var client = Client(inbox);
        var summary = Substitute.For<IMessageSummary>();

        summary.Envelope.Returns(new Envelope { MessageId = " assigned@example.test " });
        inbox
            .SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<UniqueId>>([new UniqueId(7)]));
        inbox
            .FetchAsync(Arg.Any<IList<UniqueId>>(), Arg.Any<IFetchRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<IMessageSummary>>([summary]));

        await using var mailbox = new ImapWatchedMailbox(Account(), client);
        await mailbox.OpenAsync(TestContext.Current.CancellationToken);

        // Act
        var assigned = await mailbox.FindDeliveredMessageIdAsync("proposed@example.test", TestContext.Current.CancellationToken);

        // Assert
        // Envelopes and nothing else. A body read is the operation `\Seen` follows, and nothing here needs one.
        Assert.Equal("assigned@example.test", assigned);
        // Asserted against the request MailKit's convenience overload builds, because that overload is an extension
        // method and only the interface member underneath it is observable at all.
        await inbox.Received(1).FetchAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Is<IFetchRequest>(request => request != null && request.Items == MessageSummaryItems.Envelope),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindDeliveredMessageIdAsync_AMessageThatHasNotArrived_AnswersNothingRatherThanFetching()
    {
        // Arrange
        var inbox = Substitute.For<IMailFolder>();
        var client = Client(inbox);

        inbox
            .SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IList<UniqueId>>([]));

        await using var mailbox = new ImapWatchedMailbox(Account(), client);
        await mailbox.OpenAsync(TestContext.Current.CancellationToken);

        // Act
        var assigned = await mailbox.FindDeliveredMessageIdAsync("proposed@example.test", TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(assigned);
        await inbox.DidNotReceive().FetchAsync(
            Arg.Any<IList<UniqueId>>(),
            Arg.Any<IFetchRequest>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FindDeliveredMessageIdAsync_Always_AsksTheServerForNewArrivalsBeforeSearching()
    {
        // Arrange
        var inbox = Substitute.For<IMailFolder>();
        var client = Client(inbox);
        var calls = new List<string>();

        client.When(substitute => substitute.NoOpAsync(Arg.Any<CancellationToken>())).Do(_ => calls.Add("noop"));
        inbox
            .SearchAsync(Arg.Any<SearchQuery>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                calls.Add("search");

                return Task.FromResult<IList<UniqueId>>([]);
            });

        await using var mailbox = new ImapWatchedMailbox(Account(), client);
        await mailbox.OpenAsync(TestContext.Current.CancellationToken);

        // Act
        await mailbox.FindDeliveredMessageIdAsync("proposed@example.test", TestContext.Current.CancellationToken);

        // Assert
        // A folder opened before the message was submitted knows nothing about it until a command gives the server a
        // chance to report the arrival, so a search issued first would answer "not yet" for as long as the run waits.
        Assert.Equal(["noop", "search"], calls);
    }

    [Fact]
    public async Task AppendToSentAsync_AMessageTheMailboxWrote_FilesItAsReadAndDatedWhenItWasSent()
    {
        // Arrange
        var sent = Substitute.For<IMailFolder>();
        var client = Client(Substitute.For<IMailFolder>(), sent);
        var sentAt = new DateTimeOffset(2026, 6, 1, 9, 15, 0, TimeSpan.Zero);

        using var message = new MimeMessage { Date = sentAt };

        await using var mailbox = new ImapWatchedMailbox(Account(), client);
        await mailbox.OpenAsync(TestContext.Current.CancellationToken);

        // Act
        await mailbox.AppendToSentAsync(message, TestContext.Current.CancellationToken);

        // Assert
        // The internal date is the message's own, because a corpus spread over ninety days that the server dated on
        // arrival would read on every date-ordered screen as a mailbox that filled up in one afternoon.
        await sent.Received(1).AppendAsync(
            Arg.Is<AppendRequest>(request => request != null && request.Flags == MessageFlags.Seen && request.InternalDate == sentAt),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AppendToSentAsync_AServerThatRefusesTheAppend_IsReportedNamingTheFolder()
    {
        // Arrange
        var sent = Substitute.For<IMailFolder>();
        var client = Client(Substitute.For<IMailFolder>(), sent);

        sent.FullName.Returns("INBOX.Sent");
        sent
            .AppendAsync(Arg.Any<AppendRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<UniqueId?>(new ImapCommandException(ImapCommandResponse.No, "OVERQUOTA")));

        using var message = new MimeMessage();

        await using var mailbox = new ImapWatchedMailbox(Account(), client);
        await mailbox.OpenAsync(TestContext.Current.CancellationToken);

        // Act
        var failure = await Assert.ThrowsAsync<SyntheticMailFailure>(
            () => mailbox.AppendToSentAsync(message, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains("INBOX.Sent", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisposeAsync_AnOpenSession_DisconnectsAndDisposesTheClient()
    {
        // Arrange
        var client = Client(Substitute.For<IMailFolder>());

        client.IsConnected.Returns(true);

        // Act
        await using (var mailbox = new ImapWatchedMailbox(Account(), client))
        {
            await mailbox.OpenAsync(TestContext.Current.CancellationToken);
        }

        // Assert
        await client.Received(1).DisconnectAsync(true, Arg.Any<CancellationToken>());
        client.Received(1).Dispose();
    }

    [Fact]
    public void Constructor_ANullArgument_IsRefused()
    {
        // Arrange
        using var client = Substitute.For<IImapClient>();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new ImapWatchedMailbox(null!, client));
        Assert.Throws<ArgumentNullException>(() => new ImapWatchedMailbox(Account(), null!));
    }

    private static IImapClient Client(IMailFolder inbox) => Client(inbox, Substitute.For<IMailFolder>());

    /// <summary>Builds a client whose server advertises the given Sent folder, and none at all when it is absent.</summary>
    private static IImapClient Client(IMailFolder inbox, IMailFolder? sent)
    {
        var client = Substitute.For<IImapClient>();

        client.Inbox.Returns(inbox);
        client.GetFolder(SpecialFolder.Sent).Returns(sent);

        return client;
    }

    private static WatchedMailboxAccount Account() => new(
        "imap.example.test",
        993,
        MailTransportSecurity.ImplicitTls,
        new MailboxAddress("Developer", "developer@example.com"),
        "developer@example.com",
        "not-a-real-password",
        SentFolder: null);
}
