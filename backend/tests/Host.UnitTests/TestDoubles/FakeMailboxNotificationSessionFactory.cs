// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Concurrent;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Stands in for the mail server's push mechanism, so a test drives what a watched folder reports and when.</summary>
/// <remarks>
/// Hand-written rather than substituted because a test needs the sessions this factory handed out, keyed by the folder
/// they watch, and needs to report a change into one of them while the code under test is waiting on all of them. That
/// is state a substitute's call recording cannot express.
/// </remarks>
internal sealed class FakeMailboxNotificationSessionFactory(TimeProvider timeProvider) : IMailboxNotificationSessionFactory
{
    private readonly ConcurrentQueue<string> openedFolderAliases = new();
    private readonly ConcurrentQueue<string[]> subscriptionAttempts = new();
    private readonly ConcurrentDictionary<string, FakeMailboxNotificationSession> sessionsByAlias = new(StringComparer.Ordinal);

    /// <summary>Gets or sets whether the modelled server advertises a push mechanism at all.</summary>
    internal bool AdvertisesPush { get; set; } = true;

    /// <summary>Gets or sets whether the modelled server can report several folders over one connection.</summary>
    internal bool AdvertisesSubscription { get; set; }

    /// <summary>Gets or sets the failure every open ends with, which models a server that keeps refusing the session.</summary>
    internal Exception? OpenFailure { get; set; }

    /// <summary>Gets or sets the failure every subscription attempt ends with.</summary>
    internal Exception? SubscriptionOpenFailure { get; set; }

    /// <summary>Gets the folder aliases each subscription attempt named, in order, including the ones that were declined.</summary>
    internal IReadOnlyCollection<string[]> SubscriptionAttempts => this.subscriptionAttempts;

    /// <summary>Gets the subscription this factory last handed out, or <see langword="null" /> when it has handed out none.</summary>
    internal FakeMailboxFolderSetNotificationSession? Subscription { get; private set; }

    /// <summary>Gets a signal that completes once a subscription has been opened.</summary>
    internal TaskCompletionSource SubscriptionOpened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the alias of every session this factory was asked for, in order, including the ones that failed.</summary>
    internal IReadOnlyCollection<string> OpenedFolderAliases => this.openedFolderAliases;

    /// <summary>Gets a signal that completes once at least one session has been opened.</summary>
    internal TaskCompletionSource SessionOpened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets the session currently watching a folder, or <see langword="null" /> when none is.</summary>
    internal FakeMailboxNotificationSession? SessionWatching(string folderAlias) =>
        this.sessionsByAlias.TryGetValue(folderAlias, out var session) ? session : null;

    /// <inheritdoc />
    public Task<MailboxNotificationSessionResult> OpenAsync(
        MailAccountId accountId,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var folderAlias = folder.Alias.Value;
        this.openedFolderAliases.Enqueue(folderAlias);

        if (this.OpenFailure is { } failure)
        {
            return Task.FromException<MailboxNotificationSessionResult>(failure);
        }

        if (!this.AdvertisesPush)
        {
            return Task.FromResult(MailboxNotificationSessionResult.PushNotAdvertised());
        }

        var session = new FakeMailboxNotificationSession(timeProvider);
        this.sessionsByAlias[folderAlias] = session;
        this.SessionOpened.TrySetResult();

        return Task.FromResult(MailboxNotificationSessionResult.Watching(session));
    }

    /// <inheritdoc />
    public Task<MailboxFolderSetNotificationSessionResult> OpenForFoldersAsync(
        MailAccountId accountId,
        IReadOnlyList<MailFolderResolution> folders,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(folders);

        this.subscriptionAttempts.Enqueue([.. folders.Select(folder => folder.Alias.Value)]);

        if (this.SubscriptionOpenFailure is { } failure)
        {
            return Task.FromException<MailboxFolderSetNotificationSessionResult>(failure);
        }

        // A server that watches one folder at a time is the default, because that is the server every test written
        // before subscriptions existed models. A test that wants one says so.
        if (!this.AdvertisesSubscription)
        {
            return Task.FromResult(MailboxFolderSetNotificationSessionResult.SubscriptionNotAdvertised());
        }

        var session = new FakeMailboxFolderSetNotificationSession(timeProvider, folders);
        this.Subscription = session;
        this.SubscriptionOpened.TrySetResult();

        return Task.FromResult(MailboxFolderSetNotificationSessionResult.Watching(session));
    }
}

/// <summary>A watched folder whose changes a test reports by hand and whose waits end on a fake clock.</summary>
internal sealed class FakeMailboxNotificationSession(TimeProvider timeProvider) : IMailboxNotificationSession
{
    private readonly SemaphoreSlim reportedChanges = new(0);

    private int waitCount;

    /// <summary>Gets how many waits this session has served, which is how a renewal is counted.</summary>
    internal int WaitCount => Volatile.Read(ref this.waitCount);

    /// <summary>Gets a signal that completes once the session is inside its first wait.</summary>
    internal TaskCompletionSource WaitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets one permit per wait this session has entered, so a test can await the next one rather than spin for it.</summary>
    /// <remarks>
    /// A renewal is a wait ending and another beginning, and the two are separated by continuations a single yield does
    /// not reliably run. Counting entries lets a test advance the clock once per renewal it is actually waiting for,
    /// instead of advancing on a schedule that can outrun the session and collapse several renewals into one.
    /// </remarks>
    internal SemaphoreSlim WaitsEntered { get; } = new(0);

    /// <summary>Gets whether this session has been disposed, which is how a recycle and a shutdown are told from a leak.</summary>
    internal bool IsDisposed { get; private set; }

    /// <summary>Gets or sets the failure every wait ends with, which models a session that connects and then serves nothing.</summary>
    internal Exception? WaitFailure { get; set; }

    /// <summary>Reports one change, which the wait in flight — or the next one to start — answers with.</summary>
    /// <remarks>
    /// Counted rather than signalled, so a change reported a moment before a wait begins is still the change that wait
    /// observes. A test that had to interleave the two exactly would be asserting on its own timing.
    /// </remarks>
    internal void ReportFolderChange() => this.reportedChanges.Release();

    /// <inheritdoc />
    public async Task<MailboxNotificationOutcome> WaitForFolderChangeAsync(
        TimeSpan maxWait,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref this.waitCount);
        this.WaitStarted.TrySetResult();

        if (this.WaitFailure is { } failure)
        {
            this.WaitsEntered.Release();

            throw failure;
        }

        using var renewalDeadline = new CancellationTokenSource(maxWait, timeProvider);
        using var endOfWait = CancellationTokenSource.CreateLinkedTokenSource(
            renewalDeadline.Token,
            cancellationToken);

        // Released once the deadline is armed, so a test that advances the clock on this signal cannot advance it past
        // a renewal the session has not yet entered.
        this.WaitsEntered.Release();

        try
        {
            await this.reportedChanges.WaitAsync(endOfWait.Token);

            return MailboxNotificationOutcome.FolderChanged;
        }
        catch (OperationCanceledException)
        {
            // The port reports a cancelled wait as an ended one and leaves the session usable, so the real adapter and
            // this fake have to agree about it.
            return MailboxNotificationOutcome.WaitElapsed;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this.IsDisposed = true;
        this.reportedChanges.Dispose();
        this.WaitsEntered.Dispose();

        return ValueTask.CompletedTask;
    }
}

/// <summary>A set of folders watched over one connection, whose changes a test reports by naming the folder.</summary>
internal sealed class FakeMailboxFolderSetNotificationSession : IMailboxFolderSetNotificationSession
{
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim reportedChanges = new(0);
    private readonly ConcurrentQueue<MailFolderAlias> changedFolders = new();

    private int waitCount;

    internal FakeMailboxFolderSetNotificationSession(
        TimeProvider timeProvider,
        IReadOnlyList<MailFolderResolution> folders)
    {
        this.timeProvider = timeProvider;
        this.WatchedFolderAliases = [.. folders.Select(folder => folder.Alias.Value)];
    }

    /// <summary>Gets the aliases this subscription was opened for, which is what a bound is asserted against.</summary>
    internal IReadOnlyList<string> WatchedFolderAliases { get; }

    /// <summary>Gets how many waits this session has served, which is how a renewal is counted.</summary>
    internal int WaitCount => Volatile.Read(ref this.waitCount);

    /// <summary>Gets a signal that completes once the session is inside its first wait.</summary>
    internal TaskCompletionSource WaitStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets whether this session has been disposed, which is how a recycle and a shutdown are told from a leak.</summary>
    internal bool IsDisposed { get; private set; }

    /// <summary>Gets or sets the failure every wait ends with, which models a subscription that is accepted and then serves nothing.</summary>
    internal Exception? WaitFailure { get; set; }

    /// <summary>Reports that one of the watched folders changed, naming it the way a server's report does.</summary>
    internal void ReportFolderChange(MailFolderAlias folderAlias)
    {
        this.changedFolders.Enqueue(folderAlias);
        this.reportedChanges.Release();
    }

    /// <inheritdoc />
    public async Task<MailboxFolderSetNotificationOutcome> WaitForFolderChangeAsync(
        TimeSpan maxWait,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref this.waitCount);
        this.WaitStarted.TrySetResult();

        if (this.WaitFailure is { } failure)
        {
            throw failure;
        }

        using var renewalDeadline = new CancellationTokenSource(maxWait, this.timeProvider);
        using var endOfWait = CancellationTokenSource.CreateLinkedTokenSource(
            renewalDeadline.Token,
            cancellationToken);

        try
        {
            await this.reportedChanges.WaitAsync(endOfWait.Token);

            return this.changedFolders.TryDequeue(out var changedFolder)
                ? MailboxFolderSetNotificationOutcome.FolderChanged(changedFolder)
                : MailboxFolderSetNotificationOutcome.WaitElapsed;
        }
        catch (OperationCanceledException)
        {
            // The port reports a cancelled wait as an ended one and leaves the session usable, so the real adapter and
            // this fake have to agree about it.
            return MailboxFolderSetNotificationOutcome.WaitElapsed;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        this.IsDisposed = true;
        this.reportedChanges.Dispose();

        return ValueTask.CompletedTask;
    }
}
