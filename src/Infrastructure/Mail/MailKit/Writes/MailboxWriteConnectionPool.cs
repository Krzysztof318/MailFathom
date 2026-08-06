// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Resilience;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Resilience;
using MailKit.Net.Imap;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MailFathom.Infrastructure.Mail.MailKit.Writes;

/// <summary>Holds at most one write connection per account, opened on demand and closed once it has been idle long enough.</summary>
/// <remarks>
/// <para>
/// The bound is one connection per account, and it is a property of this type rather than of how many mutations happen
/// to be in flight: a second caller for the same account waits for the first, so a burst of changes can never turn into
/// a burst of logins. That matters because a mail server answers an account holding too many connections by refusing
/// one — a provider limit, or Dovecot's <c>mail_max_userip_connections</c> — and the refusal lands on whichever
/// connection asked next, which is usually synchronization rather than the write that caused it.
/// </para>
/// <para>
/// The connection outlives the session that used it because the alternative is worse in both directions. Closing after
/// each mutation would make a run of changes pay for a TCP connection, a TLS handshake, and an authentication every
/// time; keeping it forever would spend one of the account's slots on an account nobody is changing. So it is kept for
/// a bounded idle period, measured from the moment the last session was disposed, and closed when that elapses.
/// </para>
/// <para>
/// A connection is pinned to the folder it selected, because that is what an IMAP selection is. A session for a
/// different folder of the same account therefore replaces the connection rather than joining it, which keeps the
/// one-per-account bound exact instead of turning it into one per folder.
/// </para>
/// <para>
/// Each live connection owns a dependency-injection scope of its own, disposed with it. The collaborators it needs to
/// re-establish itself — the account's settings and its access token source — are scoped services, and a process-wide
/// singleton holding one resolved elsewhere would be reading a snapshot that ended long ago. A configuration reload
/// therefore reaches the next connection this pool opens rather than the one it is holding, which is the same rule a
/// synchronization run follows.
/// </para>
/// </remarks>
internal sealed partial class MailboxWriteConnectionPool : IAsyncDisposable
{
    private readonly Func<IImapClient> clientFactory;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly OutboundOperationExecutor operationExecutor;
    private readonly ITransientFailureClassifier transientFailureClassifier;
    private readonly MailboxWriteSessionOptions options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<MailboxWriteConnectionPool> logger;
    private readonly ConcurrentDictionary<string, AccountWriteConnection> accounts = new(StringComparer.Ordinal);

    private bool disposed;

    /// <summary>Initializes an empty pool that opens nothing until a mutation asks for a connection.</summary>
    /// <param name="clientFactory">Creates one IMAP client per establishment attempt.</param>
    /// <param name="scopeFactory">Creates the dependency-injection scope each live connection owns.</param>
    /// <param name="operationExecutor">Runs establishment and mutation under their configured pipelines.</param>
    /// <param name="transientFailureClassifier">Decides whether a failure left a connection worth keeping.</param>
    /// <param name="options">Bounds how long a connection is kept once it falls idle.</param>
    /// <param name="timeProvider">Schedules the idle expiry.</param>
    /// <param name="logger">Records connections opened and closed.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    internal MailboxWriteConnectionPool(
        Func<IImapClient> clientFactory,
        IServiceScopeFactory scopeFactory,
        OutboundOperationExecutor operationExecutor,
        ITransientFailureClassifier transientFailureClassifier,
        MailboxWriteSessionOptions options,
        TimeProvider timeProvider,
        ILogger<MailboxWriteConnectionPool> logger)
    {
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(operationExecutor);
        ArgumentNullException.ThrowIfNull(transientFailureClassifier);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        this.clientFactory = clientFactory;
        this.scopeFactory = scopeFactory;
        this.operationExecutor = operationExecutor;
        this.transientFailureClassifier = transientFailureClassifier;
        this.options = options;
        this.timeProvider = timeProvider;
        this.logger = logger;
    }

    /// <summary>Takes the account's write connection for exclusive use, establishing it when none is held.</summary>
    /// <param name="accountId">The account whose mailbox is to be changed.</param>
    /// <param name="folder">The alias binding the connection selects for writing.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy every attempt must obey.</param>
    /// <param name="cancellationToken">Cancels waiting for the connection, connecting, authenticating, and selecting.</param>
    /// <returns>The lease, which the caller must dispose to give the connection back.</returns>
    /// <exception cref="ObjectDisposedException">Thrown when the pool has been disposed with the host.</exception>
    internal async Task<MailboxWriteConnectionLease> LeaseAsync(
        MailAccountId accountId,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        var account = this.accounts.GetOrAdd(accountId.Value, _ => new AccountWriteConnection(this, accountId));

        return await account.LeaseAsync(folder, transportSecurityPolicy, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;

        // Drained rather than iterated over a snapshot. A caller that read the disposed flag above as false a moment
        // before this ran can still reach GetOrAdd afterwards, and a snapshot would walk past the account it inserts:
        // that connection would then be opened normally and kept alive by its own idle timer, against a mail server the
        // host has already reported closing every connection to. Draining until the dictionary is empty collects it,
        // and the flag the account's own lease checks is what stops one arriving after even that.
        while (!this.accounts.IsEmpty)
        {
            foreach (var accountKey in this.accounts.Keys)
            {
                if (this.accounts.TryRemove(accountKey, out var account))
                {
                    await account.DisposeAsync();
                }
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Opened the write connection for account {AccountId}, selecting folder {FolderAlias}.")]
    private partial void LogWriteConnectionOpened(string accountId, string folderAlias);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "Closed the write connection for account {AccountId}.")]
    private partial void LogWriteConnectionClosed(string accountId);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The write connection for account {AccountId} did not close cleanly.")]
    private partial void LogWriteConnectionCloseFailed(Exception failure, string accountId);

    /// <summary>One account's write connection, the gate that keeps it to one user, and the clock that closes it.</summary>
    private sealed class AccountWriteConnection(MailboxWriteConnectionPool pool, MailAccountId accountId) : IAsyncDisposable
    {
        private readonly SemaphoreSlim gate = new(1, 1);

        private MailKitImapConnection? connection;
        private IServiceScope? connectionScope;
        private MailFolderResolutionId? selectedFolderId;
        private ITimer? idleTimer;
        private bool disposed;

        internal async Task<MailboxWriteConnectionLease> LeaseAsync(
            MailFolderResolution folder,
            MailTransportSecurityPolicy transportSecurityPolicy,
            CancellationToken cancellationToken)
        {
            await this.gate.WaitAsync(cancellationToken);

            try
            {
                ObjectDisposedException.ThrowIf(this.disposed, this);

                // The pool's flag as well as this account's, because an account inserted after the shutdown drain had
                // already passed its key would never be disposed by anything. Refusing here is what keeps that race
                // from opening a connection nothing will ever close.
                ObjectDisposedException.ThrowIf(pool.disposed, pool);

                // Nothing may expire the connection while a caller holds it; the clock starts again on release.
                this.idleTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                if (this.selectedFolderId is { } heldFolderId && heldFolderId != folder.Id)
                {
                    await this.CloseHeldConnectionAsync();
                }

                this.connection ??= this.OpenConnection(folder, transportSecurityPolicy);
                await this.connection.EnsureOpenFolderAsync(cancellationToken);
                this.selectedFolderId = folder.Id;

                return new MailboxWriteConnectionLease(accountId, this.connection, this.ReleaseAsync);
            }
            catch
            {
                // An establishment that failed leaves nothing worth holding, and the gate has to be given back before
                // the failure reaches a caller that will never dispose a lease it did not receive.
                await this.CloseHeldConnectionAsync();
                this.ReleaseGate();

                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;

            // The timer goes first, so no expiry can be scheduled against a gate this is about to take and never give
            // back. An expiry already running holds the gate, and the wait below is what lets it finish.
            this.idleTimer?.Dispose();

            await this.gate.WaitAsync(CancellationToken.None);
            try
            {
                await this.CloseHeldConnectionAsync();
            }
            finally
            {
                // Released before it is disposed so a caller already waiting on it is let go rather than left waiting
                // for ever; every release goes through ReleaseGate, which is what makes the disposal that follows safe
                // for an expiry still unwinding on a background task.
                this.ReleaseGate();
                this.gate.Dispose();
            }
        }

        private MailKitImapConnection OpenConnection(
            MailFolderResolution folder,
            MailTransportSecurityPolicy transportSecurityPolicy)
        {
            var scope = pool.scopeFactory.CreateScope();

            try
            {
                var establishedConnection = MailKitImapConnection.ForWriting(
                    pool.clientFactory,
                    scope.ServiceProvider.GetRequiredService<IImapAccountSettingsProvider>(),
                    scope.ServiceProvider.GetRequiredService<IMailAccessTokenSource>(),
                    pool.operationExecutor,
                    pool.transientFailureClassifier,
                    accountId,
                    folder,
                    transportSecurityPolicy);

                this.connectionScope = scope;
                pool.LogWriteConnectionOpened(accountId.Value, folder.Alias.Value);

                return establishedConnection;
            }
            catch
            {
                scope.Dispose();

                throw;
            }
        }

        private ValueTask ReleaseAsync()
        {
            this.idleTimer ??= pool.timeProvider.CreateTimer(
                _ => _ = this.CloseIdleConnectionAsync(),
                state: null,
                Timeout.InfiniteTimeSpan,
                Timeout.InfiniteTimeSpan);

            this.idleTimer.Change(pool.options.ConnectionIdlePeriod, Timeout.InfiniteTimeSpan);
            this.ReleaseGate();

            return ValueTask.CompletedTask;
        }

        /// <summary>Closes the connection when the idle period elapsed without anybody taking it again.</summary>
        /// <remarks>
        /// A caller that took the connection between the expiry being scheduled and this running holds the gate, and
        /// finding it held is the whole answer: the connection is in use, and the release that follows schedules a
        /// fresh expiry. Waiting for the gate instead would close a connection in the middle of a mutation.
        /// </remarks>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A timer callback has no caller to report to, and a connection that failed to close cleanly must not bring the process down.")]
        private async Task CloseIdleConnectionAsync()
        {
            try
            {
                if (!await this.gate.WaitAsync(TimeSpan.Zero, CancellationToken.None))
                {
                    return;
                }
            }
            catch (ObjectDisposedException)
            {
                // The account was disposed with the host between this expiry being scheduled and it running, so the
                // connection it would have closed is closed already.
                return;
            }

            try
            {
                await this.CloseHeldConnectionAsync();
            }
            catch (Exception failure)
            {
                pool.LogWriteConnectionCloseFailed(failure, accountId.Value);
            }
            finally
            {
                this.ReleaseGate();
            }
        }

        /// <summary>Gives the gate back, tolerating the one disposal that can race a release.</summary>
        /// <remarks>
        /// Only the idle expiry can reach this after the account was disposed with the host, and it holds the gate when
        /// it does. Nothing is waiting on a gate that has been disposed, so there is nothing this release would have
        /// let through and nothing to report.
        /// </remarks>
        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Only ObjectDisposedException is caught, which is the documented outcome of releasing a semaphore the host has already disposed.")]
        private void ReleaseGate()
        {
            try
            {
                this.gate.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>Disposes the held connection and the scope it was resolved from, leaving the account holding neither.</summary>
        /// <remarks>
        /// The scope is released in a <c>finally</c> because the connection's own disposal genuinely throws: it logs out
        /// politely first, and a socket the server reset while the connection sat idle fails that. Letting the failure
        /// skip the scope would leak the account's settings provider and access token source on exactly the path that
        /// meets a broken connection most often — the idle eviction, which catches the failure and only logs it.
        /// </remarks>
        private async ValueTask CloseHeldConnectionAsync()
        {
            var closedConnection = this.connection;
            var closedScope = this.connectionScope;

            this.connection = null;
            this.connectionScope = null;
            this.selectedFolderId = null;

            try
            {
                if (closedConnection is not null)
                {
                    await closedConnection.DisposeAsync();
                    pool.LogWriteConnectionClosed(accountId.Value);
                }
            }
            finally
            {
                closedScope?.Dispose();
            }
        }
    }
}
