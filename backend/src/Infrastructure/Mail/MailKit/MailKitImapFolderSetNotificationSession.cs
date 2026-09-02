// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Sessions;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Folders;
using MailKit;
using MailKit.Net.Imap;

namespace MailFathom.Infrastructure.Mail.MailKit;

/// <summary>Asks an IMAP server to report changes to a set of folders over one connection.</summary>
/// <param name="client">The authenticated client the subscription is registered on.</param>
/// <param name="additionalFolders">The folders to watch beside the one the connection has open, which may be empty.</param>
/// <param name="cancellationToken">Cancels the subscription command.</param>
/// <returns>A task that completes once the server has accepted the subscription.</returns>
/// <remarks>
/// This is a delegate rather than a call because MailKit's mailbox filter accepts only its own concrete folder type,
/// which no substitute of the published <see cref="IMailFolder" /> interface can satisfy. Every other part of this
/// adapter is written against interfaces the library publishes and is exercised through them; keeping the one command
/// that cannot be behind a seam leaves the session's own behavior — resolving folders, watching events, renewing,
/// reporting which folder changed — provable without a mail server.
/// </remarks>
internal delegate Task ImapChangeSubscriptionCommand(
    IImapClient client,
    IReadOnlyList<IMailFolder> additionalFolders,
    CancellationToken cancellationToken);

/// <summary>Issues the IMAP <c>NOTIFY</c> command that puts a set of folders under one subscription.</summary>
internal static class MailKitImapChangeSubscription
{
    /// <summary>The three events that are a reason to synchronize, and the only ones this subscription asks for.</summary>
    /// <remarks>
    /// <c>MessageNew</c> carries no fetch request, which is both a protocol requirement for a mailbox other than the
    /// selected one and the read-only invariant expressed in the subscription itself: a fetch attached here would have
    /// the server retrieve arriving messages on its own, and a body item among them would set <c>\Seen</c> on mail
    /// nobody has read. RFC 5465 also requires the arrival and removal events to be asked for together, and permits a
    /// flag event only alongside both.
    /// </remarks>
    private static readonly ImapEvent[] WatchedEvents =
    [
        new ImapEvent.MessageNew(),
        ImapEvent.MessageExpunge,
        ImapEvent.FlagChange,
    ];

    /// <summary>Subscribes to changes in the selected folder and in every other folder supplied.</summary>
    /// <param name="client">The authenticated client the subscription is registered on.</param>
    /// <param name="additionalFolders">The folders to watch beside the one the connection has open.</param>
    /// <param name="cancellationToken">Cancels the subscription command.</param>
    /// <returns>A task that completes once the server has accepted the subscription.</returns>
    /// <remarks>
    /// <para>
    /// The selected folder needs its own event group. RFC 5465 makes a <c>NOTIFY SET</c> replace the server's default
    /// behavior, so a subscription that named only the other mailboxes would silence the folder this connection has
    /// open — the one folder a server reports on without being asked.
    /// </para>
    /// <para>
    /// The immediate status report is deliberately not requested. It would answer the subscription with the selected
    /// folder's current state, which arrives as a change and would start a synchronization pass on every renewal
    /// whether or not anything had happened.
    /// </para>
    /// </remarks>
    [RequiresIntegrationCoverage]
    internal static Task RequestFolderNotificationsAsync(
        IImapClient client,
        IReadOnlyList<IMailFolder> additionalFolders,
        CancellationToken cancellationToken)
    {
        var eventGroups = new List<ImapEventGroup>
        {
            new(ImapMailboxFilter.Selected, WatchedEvents),
        };

        if (additionalFolders.Count > 0)
        {
            eventGroups.Add(new ImapEventGroup(new ImapMailboxFilter.Mailboxes([.. additionalFolders]), WatchedEvents));
        }

        return client.NotifyAsync(status: false, eventGroups, cancellationToken);
    }
}

/// <summary>Holds one account's folders under a single IMAP subscription and reports which one the server says changed.</summary>
/// <remarks>
/// <para>
/// One connection serves every watched folder. The connection selects the first of them so that <c>IDLE</c> has a
/// mailbox to run against, and the subscription covers the rest; a server reports a change to a folder it has not
/// selected as an unsolicited status response, which MailKit raises on the folder it belongs to. That is why nothing
/// here reads a count, a UID, or a sequence out of an event: the events differ between the selected folder and the
/// others, and the only statement common to all of them — that this folder changed — is the only one acted on.
/// </para>
/// <para>
/// The subscription is re-issued on every wait rather than once when the session opens. A wait is where a dropped
/// connection is rebuilt, and a rebuilt connection has no subscription and holds different folder objects, so a
/// subscription registered once would quietly stop reporting anything after the first reconnection.
/// </para>
/// <para>
/// This session issues no <c>FETCH</c> of any kind and its subscription asks for no message data, so no path here can
/// set the remote <c>\Seen</c> flag. Nothing here is safe for concurrent use.
/// </para>
/// </remarks>
internal sealed class MailKitImapFolderSetNotificationSession(
    MailKitImapConnection connection,
    IReadOnlyList<MailFolderResolution> folders,
    ImapChangeSubscriptionCommand requestFolderNotifications,
    TimeProvider timeProvider) : IMailboxFolderSetNotificationSession
{
    /// <inheritdoc />
    public ValueTask DisposeAsync() => connection.DisposeAsync();

    /// <inheritdoc />
    public Task<MailboxFolderSetNotificationOutcome> WaitForFolderChangeAsync(
        TimeSpan maxWait,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxWait, TimeSpan.Zero);

        return connection.ExecuteUnrepeatedFolderOperationAsync(
            (client, selectedFolder, attemptToken) => this.WatchUntilOneFolderChangesAsync(
                client,
                selectedFolder,
                maxWait,
                attemptToken),
            cancellationToken);
    }

    /// <summary>Subscribes, idles, and leaves on the first folder to change, on the renewal deadline, or on cancellation.</summary>
    /// <remarks>
    /// The three endings arrive through one token, exactly as they do for a single folder: MailKit returns from
    /// <c>IDLE</c> when the done token is cancelled, and only the done token is linked to the caller's cancellation so
    /// that ending a wait leaves the connection usable rather than torn down mid-command.
    /// </remarks>
    private async Task<MailboxFolderSetNotificationOutcome> WatchUntilOneFolderChangesAsync(
        IImapClient client,
        IMailFolder selectedFolder,
        TimeSpan maxWait,
        CancellationToken cancellationToken)
    {
        var watchedFolders = await this.ResolveWatchedFoldersAsync(client, selectedFolder, cancellationToken);

        await requestFolderNotifications(
            client,
            [.. watchedFolders.Skip(1).Select(static watched => watched.Folder)],
            cancellationToken);

        using var renewalDeadline = new CancellationTokenSource(maxWait, timeProvider);
        using var idleState = CancellationTokenSource.CreateLinkedTokenSource(
            renewalDeadline.Token,
            cancellationToken);

        var changedFolder = new FirstReportedFolder();

        // Subscribing after the subscription command means a status response the server volunteers while accepting it
        // is not read as a change, which would otherwise start a pass on every renewal.
        var subscriptions = watchedFolders
            .Select(watched => new WatchedFolderSubscription(watched, changedFolder, idleState))
            .ToArray();

        try
        {
            await client.IdleAsync(idleState.Token, CancellationToken.None);
        }
        finally
        {
            foreach (var subscription in subscriptions)
            {
                subscription.Dispose();
            }
        }

        // Cancellation is reported as an ended wait rather than thrown, for the reason the single-folder session gives:
        // the caller stopped this wait rather than abandoning the session, and the connection is as usable as it was.
        return changedFolder.Alias is { } alias
            ? MailboxFolderSetNotificationOutcome.FolderChanged(alias)
            : MailboxFolderSetNotificationOutcome.WaitElapsed;
    }

    /// <summary>Pairs every watched alias with the folder object this connection reports its events on.</summary>
    /// <remarks>
    /// The first alias is the folder the connection already has open, so it is taken from the selection rather than
    /// requested again: asking for it a second time would hand back a second object, and the events would then be
    /// raised on the one nothing is listening to.
    /// </remarks>
    private async Task<IReadOnlyList<WatchedFolder>> ResolveWatchedFoldersAsync(
        IImapClient client,
        IMailFolder selectedFolder,
        CancellationToken cancellationToken)
    {
        var watchedFolders = new List<WatchedFolder>(folders.Count)
        {
            new(folders[0].Alias, selectedFolder),
        };

        foreach (var folder in folders.Skip(1))
        {
            watchedFolders.Add(new WatchedFolder(
                folder.Alias,
                await client.GetFolderAsync(folder.RemotePath.Value, cancellationToken)));
        }

        return watchedFolders;
    }

    /// <summary>One alias and the folder object the server's reports about it arrive on.</summary>
    private sealed record WatchedFolder(MailFolderAlias Alias, IMailFolder Folder);

    /// <summary>Records which folder was first to be reported, across the threads MailKit raises events on.</summary>
    /// <remarks>
    /// Several folders can be reported in one response, and the events arrive on the connection's own thread rather
    /// than the waiting one. The first writer wins and the rest are dropped, because the pass that follows covers the
    /// whole account and a second name would change nothing but the log line.
    /// </remarks>
    private sealed class FirstReportedFolder
    {
        private string? reportedAlias;

        internal MailFolderAlias? Alias => Volatile.Read(ref this.reportedAlias) is { } alias
            ? MailFolderAlias.Create(alias)
            : null;

        internal void Record(MailFolderAlias folderAlias) =>
            Interlocked.CompareExchange(ref this.reportedAlias, folderAlias.Value, null);
    }

    /// <summary>Watches one folder for the whole of one wait and stops watching it when the wait ends.</summary>
    /// <remarks>
    /// Seven events are watched because a change reaches a client in different shapes depending on whether the folder
    /// is the selected one. The selected folder reports an arrival, a removal, and a flag change directly; every other
    /// folder reports the same three things as a status response, which surfaces as a moved message count, a moved
    /// next UID, a moved unread count, or a moved modification sequence. A removal is watched through both of its
    /// events, because a connection with quick resynchronization enabled reports one and never the other.
    /// </remarks>
    private sealed class WatchedFolderSubscription : IDisposable
    {
        private readonly WatchedFolder watched;
        private readonly FirstReportedFolder changedFolder;
        private readonly CancellationTokenSource idleState;

        internal WatchedFolderSubscription(
            WatchedFolder watched,
            FirstReportedFolder changedFolder,
            CancellationTokenSource idleState)
        {
            this.watched = watched;
            this.changedFolder = changedFolder;
            this.idleState = idleState;

            this.watched.Folder.CountChanged += this.EndWait;
            this.watched.Folder.UidNextChanged += this.EndWait;
            this.watched.Folder.UnreadChanged += this.EndWait;
            this.watched.Folder.HighestModSeqChanged += this.EndWait;
            this.watched.Folder.MessageExpunged += this.EndWait;
            this.watched.Folder.MessagesVanished += this.EndWait;
            this.watched.Folder.MessageFlagsChanged += this.EndWait;
        }

        public void Dispose()
        {
            this.watched.Folder.CountChanged -= this.EndWait;
            this.watched.Folder.UidNextChanged -= this.EndWait;
            this.watched.Folder.UnreadChanged -= this.EndWait;
            this.watched.Folder.HighestModSeqChanged -= this.EndWait;
            this.watched.Folder.MessageExpunged -= this.EndWait;
            this.watched.Folder.MessagesVanished -= this.EndWait;
            this.watched.Folder.MessageFlagsChanged -= this.EndWait;
        }

        /// <summary>Records this folder as the reported one and ends the wait every watched folder shares.</summary>
        /// <remarks>
        /// It takes the base event argument type so that one method serves all seven events. A delegate instance could
        /// not: the handler type is generic and invariant, and only a method group converts to each of them.
        /// </remarks>
        private void EndWait(object? sender, EventArgs eventArgs)
        {
            this.changedFolder.Record(this.watched.Alias);

            // Ending the idle state is what returns the client to normal operation; the awaiting call then completes.
            this.idleState.Cancel();
        }
    }
}
