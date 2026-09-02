// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Resilience;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Resilience;
using MailKit;
using MailKit.Net.Imap;

namespace MailFathom.Infrastructure.Mail.MailKit;

/// <summary>MailKit-backed factory for the long-lived IMAP IDLE session a folder in push mode waits on.</summary>
internal sealed class MailKitImapNotificationSessionFactory(
    Func<IImapClient> clientFactory,
    IImapAccountSettingsProvider settingsProvider,
    IMailAccessTokenSource accessTokenSource,
    OutboundOperationExecutor operationExecutor,
    ITransientFailureClassifier transientFailureClassifier,
    MailServerConnectionBudget connectionBudget,
    ImapChangeSubscriptionCommand requestFolderNotifications,
    TimeProvider timeProvider) : IMailboxNotificationSessionFactory
{
    /// <inheritdoc />
    /// <remarks>
    /// The capability is read from the connection this call just established rather than from anything cached, and the
    /// connection is closed again when the server advertises no <c>IDLE</c>. Holding an authenticated connection open
    /// for a folder that will be polled anyway would spend one of the account's connection slots on nothing.
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the connection passes to the returned session; every other path disposes it here.")]
    public async Task<MailboxNotificationSessionResult> OpenAsync(
        MailAccountId accountId,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var connection = MailKitImapConnection.ForReading(
            clientFactory,
            settingsProvider,
            accessTokenSource,
            operationExecutor,
            transientFailureClassifier,
            connectionBudget,
            MailServerConnectionPurpose.PushNotification,
            accountId,
            folder,
            transportSecurityPolicy);

        try
        {
            var client = await connection.EnsureAuthenticatedClientAsync(cancellationToken);
            await connection.EnsureOpenFolderAsync(cancellationToken);

            if (!client.Capabilities.HasFlag(ImapCapabilities.Idle))
            {
                await connection.DisposeAsync();

                return MailboxNotificationSessionResult.PushNotAdvertised();
            }
        }
        catch
        {
            await connection.DisposeAsync();

            throw;
        }

        return MailboxNotificationSessionResult.Watching(
            new MailKitImapNotificationSession(connection, timeProvider));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Both capabilities are required, because they answer different halves of one question: <c>NOTIFY</c> is what
    /// lets a server report a folder this connection has not selected, and <c>IDLE</c> is what keeps the connection in
    /// a state where an unsolicited report can reach it at all. A server offering one without the other cannot serve
    /// this session, and saying so here is what lets the caller fall back to watching folders one at a time.
    /// </para>
    /// <para>
    /// The connection selects the first folder of the set. It has to select something for <c>IDLE</c> to run against,
    /// and selecting one of the watched folders means the mailbox the server reports on by default is one the caller
    /// asked to watch rather than an arbitrary one.
    /// </para>
    /// </remarks>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the connection passes to the returned session; every other path disposes it here.")]
    public async Task<MailboxFolderSetNotificationSessionResult> OpenForFoldersAsync(
        MailAccountId accountId,
        IReadOnlyList<MailFolderResolution> folders,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);

        if (folders.Count == 0)
        {
            throw new ArgumentException("A subscription watches at least one folder.", nameof(folders));
        }

        var connection = MailKitImapConnection.ForReading(
            clientFactory,
            settingsProvider,
            accessTokenSource,
            operationExecutor,
            transientFailureClassifier,
            connectionBudget,
            MailServerConnectionPurpose.PushNotification,
            accountId,
            folders[0],
            transportSecurityPolicy);

        try
        {
            var client = await connection.EnsureAuthenticatedClientAsync(cancellationToken);
            await connection.EnsureOpenFolderAsync(cancellationToken);

            if (!client.Capabilities.HasFlag(ImapCapabilities.Notify) || !client.Capabilities.HasFlag(ImapCapabilities.Idle))
            {
                await connection.DisposeAsync();

                return MailboxFolderSetNotificationSessionResult.SubscriptionNotAdvertised();
            }
        }
        catch
        {
            await connection.DisposeAsync();

            throw;
        }

        return MailboxFolderSetNotificationSessionResult.Watching(
            new MailKitImapFolderSetNotificationSession(
                connection,
                folders,
                requestFolderNotifications,
                timeProvider));
    }
}

/// <summary>Holds one folder in IMAP <c>IDLE</c> and reports when the server says it changed.</summary>
/// <remarks>
/// <para>
/// The folder is selected read-only by the same connection every other session uses, and this type issues no
/// <c>FETCH</c> of any kind. <c>IDLE</c> retrieves nothing — it asks the server to push untagged responses about the
/// selected folder — so no part of this path can set the remote <c>\Seen</c> flag, and the synchronization a
/// notification triggers runs over its own session through the ordinary synchronizer.
/// </para>
/// <para>
/// What the server reported is deliberately discarded. A count, a UID, or a flag from an untagged response describes
/// the folder at an instant this session has no transaction over, and acting on it would be a second, unbounded source
/// of truth beside the checkpoint. The only thing carried out of a wait is that something changed.
/// </para>
/// </remarks>
internal sealed class MailKitImapNotificationSession(
    MailKitImapConnection connection,
    TimeProvider timeProvider) : IMailboxNotificationSession
{
    /// <inheritdoc />
    public ValueTask DisposeAsync() => connection.DisposeAsync();

    /// <inheritdoc />
    public Task<MailboxNotificationOutcome> WaitForFolderChangeAsync(
        TimeSpan maxWait,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxWait, TimeSpan.Zero);

        return connection.ExecuteUnrepeatedFolderOperationAsync(
            (client, openFolder, attemptToken) => this.IdleUntilFolderChangesAsync(
                client,
                openFolder,
                maxWait,
                attemptToken),
            cancellationToken);
    }

    /// <summary>Enters <c>IDLE</c> and leaves it on the first reported change, on the renewal deadline, or on cancellation.</summary>
    /// <remarks>
    /// <para>
    /// MailKit exits <c>IDLE</c> when the done token is cancelled and returns normally, so all three endings arrive
    /// through the same token and are told apart by whether an event fired. The deadline is armed on
    /// <see cref="TimeProvider" /> rather than on a wall-clock timer, which is what lets a test drive a renewal without
    /// waiting for one.
    /// </para>
    /// <para>
    /// Only the done token is linked to the caller's cancellation, and the command's own token is left alone. MailKit
    /// documents that ordering explicitly: cancelling the command instead of the done state tears the connection down
    /// mid-command, which over TLS leaves the stream unusable rather than merely closed. Ending the idle state and
    /// letting the command return is what makes a host shutdown leave a session that can still be disposed politely.
    /// </para>
    /// <para>
    /// Three things are a reason to synchronize and all three are watched: mail arrived, mail was removed, and a flag
    /// changed elsewhere. Watching arrival alone would leave a deletion or a flag change waiting for the account's
    /// interval, which is the half of reconciliation a push mode would otherwise silently opt out of.
    /// </para>
    /// <para>
    /// Removal is watched through both of the events that can carry it. A connection with quick resynchronization
    /// enabled reports an expunge as <c>MessagesVanished</c> and never as <c>MessageExpunged</c>, so a session
    /// subscribed to one of them would stop noticing deletions on exactly the servers that support the most
    /// synchronization machinery — a silent regression with no failure to point at.
    /// </para>
    /// </remarks>
    private async Task<MailboxNotificationOutcome> IdleUntilFolderChangesAsync(
        IImapClient client,
        IMailFolder openFolder,
        TimeSpan maxWait,
        CancellationToken cancellationToken)
    {
        using var renewalDeadline = new CancellationTokenSource(maxWait, timeProvider);
        using var idleState = CancellationTokenSource.CreateLinkedTokenSource(
            renewalDeadline.Token,
            cancellationToken);

        var folderChanged = false;

        void EndIdleState(object? sender, EventArgs eventArgs)
        {
            folderChanged = true;

            // Ending the idle state is what returns the client to normal operation; the awaiting call then completes.
            idleState.Cancel();
        }

        openFolder.CountChanged += EndIdleState;
        openFolder.MessageExpunged += EndIdleState;
        openFolder.MessagesVanished += EndIdleState;
        openFolder.MessageFlagsChanged += EndIdleState;

        try
        {
            await client.IdleAsync(idleState.Token, CancellationToken.None);
        }
        finally
        {
            openFolder.CountChanged -= EndIdleState;
            openFolder.MessageExpunged -= EndIdleState;
            openFolder.MessagesVanished -= EndIdleState;
            openFolder.MessageFlagsChanged -= EndIdleState;
        }

        // Cancellation is reported as an ended wait rather than thrown. The idle state was left in order and the
        // command returned, so the connection is exactly as usable as it was — and the caller cancelled it, so nothing
        // here is news to anyone. Throwing would instead make the connection look abandoned mid-command and get it
        // discarded, which is the one thing a caller stopping several waits at once must not pay for.
        return folderChanged ? MailboxNotificationOutcome.FolderChanged : MailboxNotificationOutcome.WaitElapsed;
    }
}
