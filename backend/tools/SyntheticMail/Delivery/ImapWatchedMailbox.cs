// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Net.Sockets;
using MailFathom.SyntheticMail.Configuration;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace MailFathom.SyntheticMail.Delivery;

/// <summary>The real IMAP session against the mailbox MailFathom synchronizes.</summary>
/// <remarks>
/// <para>
/// The security option is chosen from the account and is never <c>Auto</c> or the opportunistic
/// <c>StartTlsWhenAvailable</c>, for the reason <see cref="SmtpSyntheticMailTransport" /> refuses those: the next
/// command after the connection is the password.
/// </para>
/// <para>
/// Nothing here asks for anything that could set <c>\Seen</c>. The inbox is opened read-only, the search returns
/// identifiers, and the fetch asks for envelopes — none of which is a body read, which is the operation the flag
/// follows. A mailbox this tool filled would otherwise arrive at MailFathom already read, and every screen built on
/// unread mail would have nothing to show.
/// </para>
/// </remarks>
internal sealed class ImapWatchedMailbox : IWatchedMailbox
{
    private readonly WatchedMailboxAccount account;
    private readonly IImapClient client;
    private IMailFolder? inbox;
    private IMailFolder? sent;

    /// <summary>Initializes a session against one mailbox, over a real IMAP client.</summary>
    /// <param name="account">The throwaway mailbox to read and append to.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="account" /> is <see langword="null" />.</exception>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the client passes to the instance being constructed, which disposes it.")]
    internal ImapWatchedMailbox(WatchedMailboxAccount account)
        : this(account, new ImapClient())
    {
    }

    /// <summary>Initializes a session over a client the caller supplies.</summary>
    /// <param name="account">The throwaway mailbox to read and append to.</param>
    /// <param name="client">The client to work through, which this instance owns and disposes.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <remarks>
    /// MailKit publishes <see cref="IImapClient" />, so the seam is the library's own interface rather than a port
    /// restating it. It exists because what this class decides is the part that must never regress quietly: which
    /// <see cref="SecureSocketOptions" /> the connection is opened with, that the inbox is opened read-only, that
    /// nothing asked of it can set <c>\Seen</c>, and which folder an appended copy lands in.
    /// </remarks>
    internal ImapWatchedMailbox(WatchedMailboxAccount account, IImapClient client)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(client);

        this.account = account;
        this.client = client;
    }

    /// <summary>Chooses the socket option an account's security is opened with.</summary>
    /// <param name="security">How the connection carrying the credential is to be secured.</param>
    /// <returns>An option that fails the connection rather than continuing unencrypted.</returns>
    /// <remarks>Separate from <see cref="OpenAsync" /> for the reason the submission transport's own mapping is, and subject to the same rule: neither answer may ever become an option that continues in the clear.</remarks>
    internal static SecureSocketOptions ResolveSocketOptions(MailTransportSecurity security) =>
        security == MailTransportSecurity.ImplicitTls
            ? SecureSocketOptions.SslOnConnect
            : SecureSocketOptions.StartTls;

    /// <inheritdoc />
    public async Task OpenAsync(CancellationToken cancellationToken)
    {
        var security = ResolveSocketOptions(this.account.Security);

        try
        {
            await this.client.ConnectAsync(this.account.Host, this.account.Port, security, cancellationToken);
        }
        catch (Exception failure) when (IsTransportFailure(failure))
        {
            throw new SyntheticMailFailure(
                $"Could not open a {this.account.Security} IMAP connection to {this.account.Host}:{this.account.Port}: {failure.Message}",
                failure);
        }

        try
        {
            await this.client.AuthenticateAsync(this.account.UserName, this.account.Password, cancellationToken);
        }
        catch (Exception failure) when (IsTransportFailure(failure))
        {
            throw new SyntheticMailFailure(
                $"{this.account.Host} refused the credential configured for {this.account.Address.Address}: {failure.Message}",
                failure);
        }

        this.sent = await this.ResolveSentFolderAsync(cancellationToken);
        this.inbox = this.client.Inbox;

        try
        {
            await this.inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken);
        }
        catch (Exception failure) when (IsTransportFailure(failure))
        {
            throw new SyntheticMailFailure(
                $"Could not open the inbox of {this.account.Address.Address}: {failure.Message}",
                failure);
        }
    }

    /// <inheritdoc />
    public async Task<string?> FindDeliveredMessageIdAsync(string marker, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(marker);

        var openInbox = this.inbox
            ?? throw new InvalidOperationException("The mailbox is searched after it has been opened.");

        try
        {
            // A folder that was opened before the message was submitted knows nothing about it until the server is
            // given a chance to report the arrival, which any command does and this one costs nothing.
            await this.client.NoOpAsync(cancellationToken);

            var matches = await openInbox.SearchAsync(
                SearchQuery.HeaderContains(SyntheticDeliveryMarker.HeaderName, marker),
                cancellationToken);

            if (matches.Count == 0)
            {
                return null;
            }

            // Envelopes and nothing else. A body read is what sets `\Seen`, and this needs the identifier the server
            // assigned rather than anything the message says. MailKit reports it in the form MimeKit stores one, so
            // the value compares directly with what the run proposed.
            var summaries = await openInbox.FetchAsync(matches, MessageSummaryItems.Envelope, cancellationToken);

            return summaries
                .Select(summary => summary.Envelope?.MessageId?.Trim())
                .FirstOrDefault(messageId => !string.IsNullOrWhiteSpace(messageId));
        }
        catch (Exception failure) when (IsTransportFailure(failure))
        {
            throw new SyntheticMailFailure(
                $"Could not search the inbox of {this.account.Address.Address}: {failure.Message}",
                failure);
        }
    }

    /// <inheritdoc />
    public async Task AppendToSentAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var sentFolder = this.sent
            ?? throw new InvalidOperationException("The mailbox is appended to after it has been opened.");

        try
        {
            // Filed as read and dated when the message says it was sent, which is what a mail client's own copy looks
            // like. An internal date left to the server would put a corpus spread over ninety days into one afternoon,
            // and every date-ordered screen would read as though the mailbox arrived at once.
            await sentFolder.AppendAsync(
                new AppendRequest(message, MessageFlags.Seen, message.Date),
                cancellationToken);
        }
        catch (Exception failure) when (IsTransportFailure(failure))
        {
            throw new SyntheticMailFailure(
                $"Could not file a message in '{sentFolder.FullName}': {failure.Message}",
                failure);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (this.client.IsConnected)
            {
                await this.client.DisconnectAsync(quit: true, CancellationToken.None);
            }
        }
        catch (Exception failure) when (IsTransportFailure(failure))
        {
            // A session being torn down has nothing left to report, for the reason the submission transport gives.
        }
        finally
        {
            this.client.Dispose();
        }
    }

    /// <summary>Resolves the folder a mailbox keeps its own sent mail in.</summary>
    /// <remarks>
    /// A configured name wins, because a server that advertises no special-use folder is the case the setting exists
    /// for and a developer naming one has already answered the question. Without one, the server's own answer is
    /// taken; a server that gives neither is refused before anything is submitted, since an exchange whose outgoing
    /// half had nowhere to go would fill the mailbox with half a thread.
    /// </remarks>
    private async Task<IMailFolder> ResolveSentFolderAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (this.account.SentFolder is { } named)
            {
                return await this.client.GetFolderAsync(named, cancellationToken);
            }

            return this.client.GetFolder(SpecialFolder.Sent)
                ?? throw new SyntheticMailFailure(
                    $"{this.account.Host} advertises no Sent folder for {this.account.Address.Address}. Set 'mailbox.sentFolder' to the folder this mailbox keeps its own mail in.");
        }
        catch (FolderNotFoundException failure)
        {
            throw new SyntheticMailFailure(
                $"'{this.account.SentFolder}' is not a folder of {this.account.Address.Address}: set 'mailbox.sentFolder' to one the server has.",
                failure);
        }
        catch (Exception failure) when (IsTransportFailure(failure))
        {
            throw new SyntheticMailFailure(
                $"Could not resolve the Sent folder of {this.account.Address.Address}: {failure.Message}",
                failure);
        }
    }

    /// <summary>Reports whether a failure is one the server or the network produced rather than a defect here.</summary>
    /// <remarks>
    /// <see cref="NotSupportedException" /> is in the set for the reason the submission transport keeps it there: it
    /// is what MailKit raises when a server does not advertise <c>STARTTLS</c>, which is the refusal that keeps the
    /// password off an unencrypted socket.
    /// </remarks>
    private static bool IsTransportFailure(Exception failure) => failure is ImapCommandException
        or ImapProtocolException
        or AuthenticationException
        or SslHandshakeException
        or ServiceNotConnectedException
        or ServiceNotAuthenticatedException
        or SocketException
        or IOException
        or NotSupportedException;
}
