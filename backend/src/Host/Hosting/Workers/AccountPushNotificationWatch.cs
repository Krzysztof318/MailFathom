// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Keeps one account's push-mode folders watched, and ends a supervisor's wait as soon as one of them changes.</summary>
/// <remarks>
/// <para>
/// This decides <em>when</em> a pass starts and nothing about what one does. A watched folder reports that it changed;
/// the pass that follows is the ordinary synchronization run over its own read-only session, with the same bounds, the
/// same checkpoint, and the same backward pass. Keeping one implementation of the correctness-critical work is the
/// whole point of triggering it from here rather than fetching from here.
/// </para>
/// <para>
/// How the folders are watched is the server's answer rather than a setting. A server that supports subscriptions
/// reports every folder over one connection, and that is what this asks for first; a server that does not is asked to
/// watch one folder per connection instead, which is what an account paid for before subscriptions were used at all.
/// The two are never mixed, because a subscription already covers what a per-folder session would watch and the second
/// connection would be spent on nothing.
/// </para>
/// <para>
/// The wait it replaces is a wait and not a schedule. A supervisor still computes how long its account should wait
/// before the next run, including any backoff; this only ends that wait early. An account whose folders are all
/// polling therefore behaves exactly as it did before push existed.
/// </para>
/// <para>
/// Each open session holds its own dependency-injection scope for as long as it lives. That is what makes the rotation
/// boundary work: the scope pins the settings snapshot the session connected under, so a newly published snapshot is a
/// session to recycle rather than a value the running session would ignore. A long-lived connection is the one place a
/// rotated password or a revoked token could otherwise stay in use until the process ends.
/// </para>
/// <para>
/// Nothing here is safe for concurrent use. One supervisor owns one watch and calls it between its own runs.
/// </para>
/// </remarks>
internal sealed partial class AccountPushNotificationWatch : IAsyncDisposable
{
    private readonly Dictionary<string, WatchedFolder> watchedFolders = new(StringComparer.Ordinal);
    private readonly FolderSubscription subscription = new();
    private readonly MailAccountId accountId;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ILogger<AccountPushNotificationWatch> logger;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes a watch that is holding no session yet.</summary>
    /// <param name="accountId">The account whose folders are watched, and which every line this logs names.</param>
    /// <param name="scopeFactory">Creates the scope each watched folder's session lives in.</param>
    /// <param name="logger">Records the effective mode, its changes, and the notifications that started a pass.</param>
    /// <param name="timeProvider">Bounds each wait and decides when a degraded folder may try push again.</param>
    public AccountPushNotificationWatch(
        MailAccountId accountId,
        IServiceScopeFactory scopeFactory,
        ILogger<AccountPushNotificationWatch> logger,
        TimeProvider timeProvider)
    {
        this.accountId = accountId;
        this.scopeFactory = scopeFactory;
        this.logger = logger;
        this.timeProvider = timeProvider;
    }

    /// <summary>Brings the watched set in line with the folders the run that just finished actually resolved.</summary>
    /// <param name="runSettings">The snapshot the finished run used, which the sessions opened here connect under.</param>
    /// <param name="account">The account's configured settings, including the mode the operator asked for.</param>
    /// <param name="resolvedFolders">The bindings the run resolved, which are the remote folders a session may select.</param>
    /// <param name="cancellationToken">Cancels opening a session.</param>
    /// <returns>A task that completes when the watched set matches the folders, and that never faults.</returns>
    /// <remarks>
    /// <para>
    /// The folders come from the run rather than from configuration because an alias names a remote folder only after
    /// discovery has matched it. Watching what the run resolved is also what keeps the two in step: a repointed alias
    /// synchronizes a new remote folder and this closes the session on the old one, instead of leaving a connection
    /// idling on a folder nothing reads any more.
    /// </para>
    /// <para>
    /// It never throws. A folder that cannot be watched is a folder that gets polled, which is the fallback the whole
    /// design rests on, so a mail server refusing a session must cost the account nothing beyond its push mode.
    /// </para>
    /// </remarks>
    internal async Task WatchResolvedFoldersAsync(
        MailSynchronizationOptions runSettings,
        MailSynchronizationAccountOptions account,
        IReadOnlyList<MailFolderResolution> resolvedFolders,
        CancellationToken cancellationToken)
    {
        if (account.Mode != MailSynchronizationMode.Push)
        {
            // An account moved back to polling by a reload releases its connections at the first run that observes it.
            await this.CloseSubscriptionAsync();
            await this.StopWatchingAsync(this.watchedFolders.Keys.ToArray());

            return;
        }

        var resolvedAliases = resolvedFolders
            .Select(folder => folder.Alias.Value)
            .ToHashSet(StringComparer.Ordinal);

        await this.StopWatchingAsync(
            [.. this.watchedFolders.Keys.Where(alias => !resolvedAliases.Contains(alias))]);

        var subscribedFolders = resolvedFolders.Take(runSettings.MaxSubscribedFolders).ToArray();
        await this.UpdateSubscriptionAsync(runSettings, account, subscribedFolders, cancellationToken);

        if (this.subscription.Session is not null)
        {
            await this.ReleaseFolderSessionsAsync();
            this.ReportSubscribedModes(resolvedFolders, subscribedFolders);

            return;
        }

        if (!this.subscription.NotAdvertised)
        {
            // The server accepts subscriptions and this attempt still failed. Opening a session per folder now would
            // ask the same server for several more connections in the same moment it refused one, so the account waits
            // out the degradation on its interval instead.
            await this.ReleaseFolderSessionsAsync();
            this.ReportPolledModes(resolvedFolders);

            return;
        }

        foreach (var folder in resolvedFolders)
        {
            await this.WatchFolderAsync(runSettings, account, folder, cancellationToken);
        }
    }

    /// <summary>Waits out an account's delay, ending it as soon as one watched folder reports a change.</summary>
    /// <param name="runSettings">The snapshot the wait's renewal interval is read from.</param>
    /// <param name="delay">How long the supervisor decided this account should wait before its next run.</param>
    /// <param name="cancellationToken">Stops the wait when the host stops scheduling.</param>
    /// <returns>A task that completes when the delay elapses, a folder changes, or scheduling stops.</returns>
    /// <exception cref="OperationCanceledException">Thrown when scheduling stops while no folder is being watched.</exception>
    /// <remarks>
    /// An account with nothing watched simply waits, which is what every account did before push existed. Otherwise the
    /// open session — one subscription, or one per folder — waits in renewal-sized commands until the delay is spent,
    /// and the first change ends all of them: the pass that follows covers every folder of the account, so there is
    /// nothing to gain by letting the others keep waiting.
    /// </remarks>
    internal async Task WaitForNextPassAsync(
        MailSynchronizationOptions runSettings,
        TimeSpan delay,
        CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        var watchedFolderCount = this.watchedFolders.Count(entry => entry.Value.Session is not null);
        if (watchedFolderCount == 0 && this.subscription.Session is null)
        {
            await Task.Delay(delay, this.timeProvider, cancellationToken);

            return;
        }

        using var endOfWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await Task.WhenAll(
        [
            .. this.watchedFolders
                .Where(entry => entry.Value.Session is not null)
                .Select(entry => this.WaitOnFolderAsync(runSettings, entry.Key, entry.Value, delay, endOfWait)),
            .. this.subscription.Session is null
                ? Array.Empty<Task>()
                : [this.WaitOnSubscriptionAsync(runSettings, delay, endOfWait)],
        ]);
    }

    /// <summary>Closes every session and the scope each one lives in.</summary>
    public async ValueTask DisposeAsync()
    {
        await this.CloseSubscriptionAsync();
        await this.StopWatchingAsync(this.watchedFolders.Keys.ToArray());
    }

    /// <summary>Opens or keeps the one session that watches the account's folders together.</summary>
    /// <remarks>
    /// A server that advertises no subscription mechanism is remembered only until the next attempt: the answer is
    /// re-read whenever the session would be opened, so a server that gains the capability across a restart or behind a
    /// load balancer is followed rather than written off for the lifetime of the process.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A subscription that could not be opened leaves the account's folders polled; the account keeps synchronizing and the next run attempts it again.")]
    private async Task UpdateSubscriptionAsync(
        MailSynchronizationOptions runSettings,
        MailSynchronizationAccountOptions account,
        IReadOnlyList<MailFolderResolution> subscribedFolders,
        CancellationToken cancellationToken)
    {
        if (this.subscription.Session is not null
            && ReferenceEquals(this.subscription.Snapshot, runSettings)
            && this.subscription.Covers(subscribedFolders))
        {
            return;
        }

        if (this.subscription.Session is not null)
        {
            // The published snapshot moved on, or the folders did. A reload is not distinguishable from a rotated
            // password, a replaced trust anchor, or a withdrawn token, so the connection that resolved the previous
            // ones is recycled rather than reasoned about.
            await this.CloseSubscriptionAsync();
            this.LogSubscriptionRecycled(this.accountId.Value);
        }

        if (this.subscription.RetryAfter is { } retryAfter && this.timeProvider.GetUtcNow() < retryAfter)
        {
            return;
        }

        try
        {
            await this.OpenSubscriptionAsync(runSettings, account, subscribedFolders, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            await this.DegradeSubscriptionAfterFailureAsync(runSettings, failure);
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The scope's lifetime is the session's; it is disposed by CloseSubscriptionAsync, by a recycle, and on every failing path here.")]
    private async Task OpenSubscriptionAsync(
        MailSynchronizationOptions runSettings,
        MailSynchronizationAccountOptions account,
        IReadOnlyList<MailFolderResolution> subscribedFolders,
        CancellationToken cancellationToken)
    {
        var scope = this.scopeFactory.CreateAsyncScope();

        try
        {
            // The session is opened under the snapshot the run used, exactly as a folder work unit is, so the endpoint,
            // the policy, and the credential a long-lived connection holds all come from one reload.
            scope.ServiceProvider.GetRequiredService<ScopedMailSynchronizationSettings>().UseRunSnapshot(runSettings);

            var result = await scope.ServiceProvider
                .GetRequiredService<IMailboxNotificationSessionFactory>()
                .OpenForFoldersAsync(
                    this.accountId,
                    subscribedFolders,
                    account.CreateTransportSecurityPolicy(),
                    cancellationToken);

            if (result.Session is not { } session)
            {
                await scope.DisposeAsync();

                if (this.subscription.DeclinedByServer(this.timeProvider.GetUtcNow() + runSettings.PushDegradationPeriod))
                {
                    this.LogSubscriptionNotAdvertised(this.accountId.Value, runSettings.PushDegradationPeriod);
                }

                return;
            }

            this.subscription.Opened(scope, session, runSettings, subscribedFolders);

            this.LogSubscriptionOpened(this.accountId.Value, subscribedFolders.Count);
        }
        catch
        {
            await scope.DisposeAsync();

            throw;
        }
    }

    /// <summary>Waits on the account's subscription until a folder changes, the delay is spent, or another wait ended.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A subscription that failed while waiting is closed and the account's folders are polled; its next run attempts it again.")]
    private async Task WaitOnSubscriptionAsync(
        MailSynchronizationOptions runSettings,
        TimeSpan delay,
        CancellationTokenSource endOfWait)
    {
        var startedAt = this.timeProvider.GetTimestamp();
        var remaining = delay;

        try
        {
            while (remaining > TimeSpan.Zero && !endOfWait.IsCancellationRequested)
            {
                var outcome = await this.subscription.Session!.WaitForFolderChangeAsync(
                    remaining < runSettings.PushRenewalInterval ? remaining : runSettings.PushRenewalInterval,
                    endOfWait.Token);

                // A wait that returned is a session that worked, which is the only evidence that clears the failures
                // counted against it. Clearing them when the connection opened instead would let a session that opens
                // and then fails every wait reset its own count on each attempt and never reach the bound.
                this.subscription.ConsecutiveFailureCount = 0;

                if (outcome.ChangedFolder is { } changedFolder)
                {
                    this.LogFolderChangeStartedPass(this.accountId.Value, changedFolder.Value);

                    await endOfWait.CancelAsync();

                    return;
                }

                remaining = delay - this.timeProvider.GetElapsedTime(startedAt);
            }
        }
        catch (OperationCanceledException) when (endOfWait.IsCancellationRequested)
        {
            // A folder reported a change, or the host stopped scheduling. This wait is simply over.
        }
        catch (Exception failure)
        {
            await this.DegradeSubscriptionAfterFailureAsync(runSettings, failure);
        }
    }

    /// <summary>Counts one subscription failure and leaves the account polled once its bound is spent.</summary>
    private async Task DegradeSubscriptionAfterFailureAsync(MailSynchronizationOptions runSettings, Exception failure)
    {
        await this.CloseSubscriptionAsync();

        var failureCount = this.subscription.AttemptFailed();

        if (failureCount < runSettings.MaxConsecutivePushFailures)
        {
            this.LogSubscriptionAttemptFailed(failure, this.accountId.Value, failureCount);

            return;
        }

        this.subscription.RetryAfter = this.timeProvider.GetUtcNow() + runSettings.PushDegradationPeriod;

        this.LogSubscriptionDegraded(
            failure,
            this.accountId.Value,
            this.subscription.ConsecutiveFailureCount,
            runSettings.PushDegradationPeriod);
    }

    /// <summary>Records which folders the open subscription covers and which are left on the account's interval.</summary>
    private void ReportSubscribedModes(
        IReadOnlyList<MailFolderResolution> resolvedFolders,
        IReadOnlyList<MailFolderResolution> subscribedFolders)
    {
        var subscribedAliases = subscribedFolders
            .Select(folder => folder.Alias.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var folder in resolvedFolders)
        {
            var folderAlias = folder.Alias.Value;

            this.ReportEffectiveMode(
                folderAlias,
                this.WatchStateOf(folderAlias),
                subscribedAliases.Contains(folderAlias)
                    ? MailSynchronizationMode.Push
                    : MailSynchronizationMode.Polling);
        }
    }

    private void ReportPolledModes(IReadOnlyList<MailFolderResolution> resolvedFolders)
    {
        foreach (var folder in resolvedFolders)
        {
            var folderAlias = folder.Alias.Value;

            this.ReportEffectiveMode(folderAlias, this.WatchStateOf(folderAlias), MailSynchronizationMode.Polling);
        }
    }

    /// <summary>Opens or keeps a session for one resolved folder, and leaves it polled when it cannot have one.</summary>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A folder whose session could not be opened is degraded to polling; the account keeps synchronizing and its other folders are unaffected.")]
    private async Task WatchFolderAsync(
        MailSynchronizationOptions runSettings,
        MailSynchronizationAccountOptions account,
        MailFolderResolution folder,
        CancellationToken cancellationToken)
    {
        var folderAlias = folder.Alias.Value;
        var watched = this.WatchStateOf(folderAlias);

        if (watched.Session is not null && ReferenceEquals(watched.Snapshot, runSettings))
        {
            return;
        }

        if (watched.Session is not null)
        {
            // The published snapshot moved on. A reload is not distinguishable from a rotated password, a replaced
            // trust anchor, or a withdrawn token, so the connection that resolved the previous ones is recycled rather
            // than reasoned about. Reconnecting costs one handshake; keeping a revoked credential in use until the
            // process ends is the alternative this refuses.
            await CloseSessionAsync(watched);
            this.LogPushSessionRecycled(this.accountId.Value, folderAlias);
        }

        if (watched.RetryPushAfter is { } retryAfter && this.timeProvider.GetUtcNow() < retryAfter)
        {
            return;
        }

        try
        {
            await this.OpenSessionAsync(runSettings, account, folder, watched, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            await this.DegradeAfterFailureAsync(runSettings, folderAlias, watched, failure);
        }
    }

    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The scope's lifetime is the session's; it is disposed by StopWatchingAsync, by a recycle, and on every failing path here.")]
    private async Task OpenSessionAsync(
        MailSynchronizationOptions runSettings,
        MailSynchronizationAccountOptions account,
        MailFolderResolution folder,
        WatchedFolder watched,
        CancellationToken cancellationToken)
    {
        var folderAlias = folder.Alias.Value;
        var scope = this.scopeFactory.CreateAsyncScope();

        try
        {
            // The session is opened under the snapshot the run used, exactly as a folder work unit is, so the endpoint,
            // the policy, and the credential a long-lived connection holds all come from one reload.
            scope.ServiceProvider.GetRequiredService<ScopedMailSynchronizationSettings>().UseRunSnapshot(runSettings);

            var result = await scope.ServiceProvider
                .GetRequiredService<IMailboxNotificationSessionFactory>()
                .OpenAsync(this.accountId, folder, account.CreateTransportSecurityPolicy(), cancellationToken);

            if (result.Session is not { } session)
            {
                await scope.DisposeAsync();
                this.StayOnPolling(runSettings, folderAlias, watched);
                this.LogPushNotAdvertised(this.accountId.Value, folderAlias, runSettings.PushDegradationPeriod);

                return;
            }

            watched.Scope = scope;
            watched.Session = session;
            watched.Snapshot = runSettings;
            watched.RetryPushAfter = null;

            this.ReportEffectiveMode(folderAlias, watched, MailSynchronizationMode.Push);
        }
        catch
        {
            await scope.DisposeAsync();

            throw;
        }
    }

    /// <summary>Waits on one folder until it changes, until the account's delay is spent, or until another folder ended the wait.</summary>
    /// <remarks>
    /// The remaining delay is recomputed from the elapsed time rather than counted down per command, so a renewal that
    /// took longer than it asked for shortens the next one instead of extending the account's whole wait.
    /// </remarks>
    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A folder that failed while waiting is degraded to polling and its session closed; the account's other folders and its next run continue.")]
    private async Task WaitOnFolderAsync(
        MailSynchronizationOptions runSettings,
        string folderAlias,
        WatchedFolder watched,
        TimeSpan delay,
        CancellationTokenSource endOfWait)
    {
        var startedAt = this.timeProvider.GetTimestamp();
        var remaining = delay;

        try
        {
            while (remaining > TimeSpan.Zero && !endOfWait.IsCancellationRequested)
            {
                var outcome = await watched.Session!.WaitForFolderChangeAsync(
                    remaining < runSettings.PushRenewalInterval ? remaining : runSettings.PushRenewalInterval,
                    endOfWait.Token);

                // A wait that returned is a session that worked, which is the only evidence that clears the failures
                // counted against it. Clearing them when the connection opened instead would let a session that opens
                // and then fails every wait reset its own count on each attempt and never reach the bound.
                watched.ConsecutiveFailureCount = 0;

                if (outcome == MailboxNotificationOutcome.FolderChanged)
                {
                    this.LogFolderChangeStartedPass(this.accountId.Value, folderAlias);

                    await endOfWait.CancelAsync();

                    return;
                }

                remaining = delay - this.timeProvider.GetElapsedTime(startedAt);
            }
        }
        catch (OperationCanceledException) when (endOfWait.IsCancellationRequested)
        {
            // Another folder reported a change, or the host stopped scheduling. This wait is simply over.
        }
        catch (Exception failure)
        {
            await this.DegradeAfterFailureAsync(runSettings, folderAlias, watched, failure);
        }
    }

    /// <summary>Counts one push failure and degrades the folder to polling once the account's bound is spent.</summary>
    /// <remarks>
    /// The session is closed either way. A failure says nothing about whether the connection behind it is still
    /// carrying a protocol conversation, and a folder whose next attempt is a fresh session is the only shape in which
    /// a retry proves anything.
    /// </remarks>
    private async Task DegradeAfterFailureAsync(
        MailSynchronizationOptions runSettings,
        string folderAlias,
        WatchedFolder watched,
        Exception failure)
    {
        await CloseSessionAsync(watched);

        watched.ConsecutiveFailureCount++;

        if (watched.ConsecutiveFailureCount < runSettings.MaxConsecutivePushFailures)
        {
            this.LogPushAttemptFailed(
                failure,
                this.accountId.Value,
                folderAlias,
                watched.ConsecutiveFailureCount);

            return;
        }

        this.StayOnPolling(runSettings, folderAlias, watched);

        this.LogPushDegraded(
            failure,
            this.accountId.Value,
            folderAlias,
            watched.ConsecutiveFailureCount,
            runSettings.PushDegradationPeriod);
    }

    /// <summary>Leaves the folder polled for the configured period, after which push is attempted again.</summary>
    private void StayOnPolling(
        MailSynchronizationOptions runSettings,
        string folderAlias,
        WatchedFolder watched)
    {
        watched.RetryPushAfter = this.timeProvider.GetUtcNow() + runSettings.PushDegradationPeriod;

        this.ReportEffectiveMode(folderAlias, watched, MailSynchronizationMode.Polling);
    }

    /// <summary>Records the mode a folder is actually running in, and says so only when it changed.</summary>
    /// <remarks>
    /// The transition is what an operator needs; repeating the current mode once per interval would bury it. The
    /// warnings that accompany a move onto polling carry the reason, so this line states the mode itself and leaves the
    /// reason to them.
    /// </remarks>
    private void ReportEffectiveMode(string folderAlias, WatchedFolder watched, MailSynchronizationMode effectiveMode)
    {
        if (watched.ReportedMode == effectiveMode)
        {
            return;
        }

        watched.ReportedMode = effectiveMode;

        this.LogEffectiveModeChanged(this.accountId.Value, folderAlias, effectiveMode);
    }

    private WatchedFolder WatchStateOf(string folderAlias)
    {
        if (!this.watchedFolders.TryGetValue(folderAlias, out var watched))
        {
            watched = new WatchedFolder();
            this.watchedFolders[folderAlias] = watched;
        }

        return watched;
    }

    /// <summary>Closes the sessions of the named folders and forgets their state entirely.</summary>
    private async Task StopWatchingAsync(IReadOnlyList<string> folderAliases)
    {
        foreach (var folderAlias in folderAliases)
        {
            if (this.watchedFolders.Remove(folderAlias, out var watched))
            {
                await CloseSessionAsync(watched);
            }
        }
    }

    /// <summary>Closes every per-folder session while keeping what is known about each folder.</summary>
    /// <remarks>
    /// The subscription has taken the folders over, and the state kept here is the mode each one was last reported in.
    /// Forgetting it would make the next run announce a mode change that nobody made.
    /// </remarks>
    private async Task ReleaseFolderSessionsAsync()
    {
        foreach (var watched in this.watchedFolders.Values)
        {
            await CloseSessionAsync(watched);
        }
    }

    private async Task CloseSubscriptionAsync()
    {
        if (this.subscription.Session is { } session)
        {
            this.subscription.Session = null;

            await session.DisposeAsync();
        }

        if (this.subscription.Scope is { } scope)
        {
            this.subscription.Scope = null;

            await scope.DisposeAsync();
        }

        this.subscription.Snapshot = null;
    }

    /// <summary>Releases the session before the scope it resolved from, because the session is what that scope owns.</summary>
    private static async Task CloseSessionAsync(WatchedFolder watched)
    {
        if (watched.Session is { } session)
        {
            watched.Session = null;

            await session.DisposeAsync();
        }

        if (watched.Scope is { } scope)
        {
            watched.Scope = null;

            await scope.DisposeAsync();
        }

        watched.Snapshot = null;
    }

    /// <summary>States which mechanism a folder is actually kept in sync by, which is the answer configuration alone cannot give.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Folder {AccountId}/{FolderAlias} is now synchronized in {EffectiveMode} mode.")]
    private partial void LogEffectiveModeChanged(
        string accountId,
        string folderAlias,
        MailSynchronizationMode effectiveMode);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mail server reported a change in {AccountId}/{FolderAlias}, so the account's next synchronization pass starts now instead of waiting out its interval.")]
    private partial void LogFolderChangeStartedPass(string accountId, string folderAlias);

    /// <summary>Records that one connection now covers several folders, which is what a folder's own mode line cannot say.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Account {AccountId} watches {SubscribedFolderCount} folders through one push subscription.")]
    private partial void LogSubscriptionOpened(string accountId, int subscribedFolderCount);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Push subscription for {AccountId} failed {ConsecutiveFailureCount} times in a row; the next run attempts it again.")]
    private partial void LogSubscriptionAttemptFailed(
        Exception exception,
        string accountId,
        int consecutiveFailureCount);

    /// <summary>Records a whole account losing push, which is a wider statement than any one folder's degradation.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Push subscription for {AccountId} failed {ConsecutiveFailureCount} times in a row, so its folders are synchronized by polling; the subscription is attempted again after {PushDegradationPeriod}.")]
    private partial void LogSubscriptionDegraded(
        Exception exception,
        string accountId,
        int consecutiveFailureCount,
        TimeSpan pushDegradationPeriod);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Push subscription for {AccountId} was recycled because a newly published configuration snapshot or folder set supersedes the one it was opened under.")]
    private partial void LogSubscriptionRecycled(string accountId);

    /// <summary>States that the account watches folders one connection at a time, which is a cost an operator may want to know about.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Mail server for {AccountId} advertises no NOTIFY capability, so each push folder is watched over its own connection; a subscription is attempted again after {PushDegradationPeriod}.")]
    private partial void LogSubscriptionNotAdvertised(string accountId, TimeSpan pushDegradationPeriod);

    /// <summary>Reports the one degradation an operator cannot fix, so it names the server rather than asking for a change.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Folder {AccountId}/{FolderAlias} is configured for push, but the mail server advertises no IDLE capability; it is synchronized by polling and push is attempted again after {PushDegradationPeriod}.")]
    private partial void LogPushNotAdvertised(
        string accountId,
        string folderAlias,
        TimeSpan pushDegradationPeriod);

    /// <summary>Records a push attempt that failed without yet spending the account's bound, so a single dropped connection is visible but not alarming.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Push session for {AccountId}/{FolderAlias} failed {ConsecutiveFailureCount} times in a row; the next run attempts it again.")]
    private partial void LogPushAttemptFailed(
        Exception exception,
        string accountId,
        string folderAlias,
        int consecutiveFailureCount);

    /// <summary>Records the degradation itself, which is the line that has to exist for a folder quietly running in the other mode.</summary>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Push session for {AccountId}/{FolderAlias} failed {ConsecutiveFailureCount} times in a row, so the folder is synchronized by polling; push is attempted again after {PushDegradationPeriod}.")]
    private partial void LogPushDegraded(
        Exception exception,
        string accountId,
        string folderAlias,
        int consecutiveFailureCount,
        TimeSpan pushDegradationPeriod);

    /// <summary>Records that a long-lived connection was rebuilt, which is where a rotated credential takes effect for push.</summary>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Push session for {AccountId}/{FolderAlias} was recycled because a newly published configuration snapshot supersedes the one it connected under.")]
    private partial void LogPushSessionRecycled(string accountId, string folderAlias);

    /// <summary>Holds what one folder's push mode currently amounts to.</summary>
    /// <remarks>
    /// A folder keeps its entry while it is degraded, because the failure count and the retry instant are the whole
    /// reason it is not being watched. Only a folder the account no longer synchronizes is forgotten.
    /// </remarks>
    private sealed class WatchedFolder
    {
        /// <summary>Gets or sets the scope the open session resolved from, which outlives every run.</summary>
        internal AsyncServiceScope? Scope { get; set; }

        /// <summary>Gets or sets the open session, or <see langword="null" /> while the folder is polled.</summary>
        internal IMailboxNotificationSession? Session { get; set; }

        /// <summary>Gets or sets the snapshot the open session connected under, which a newer one supersedes.</summary>
        internal MailSynchronizationOptions? Snapshot { get; set; }

        /// <summary>Gets or sets how many push attempts have failed since the last one that succeeded.</summary>
        internal int ConsecutiveFailureCount { get; set; }

        /// <summary>Gets or sets when push may be attempted again, or <see langword="null" /> when nothing is holding it back.</summary>
        internal DateTimeOffset? RetryPushAfter { get; set; }

        /// <summary>Gets or sets the mode this folder was last reported to be running in, so only a change is logged.</summary>
        internal MailSynchronizationMode? ReportedMode { get; set; }
    }

    /// <summary>Holds what the account's one subscription currently amounts to.</summary>
    private sealed class FolderSubscription
    {
        private string[] subscribedAliases = [];

        /// <summary>Gets or sets the scope the open session resolved from, which outlives every run.</summary>
        internal AsyncServiceScope? Scope { get; set; }

        /// <summary>Gets or sets the open session, or <see langword="null" /> while no subscription is held.</summary>
        internal IMailboxFolderSetNotificationSession? Session { get; set; }

        /// <summary>Gets or sets the snapshot the open session connected under, which a newer one supersedes.</summary>
        internal MailSynchronizationOptions? Snapshot { get; set; }

        /// <summary>Gets or sets how many subscription attempts have failed since the last one that succeeded.</summary>
        internal int ConsecutiveFailureCount { get; set; }

        /// <summary>Gets or sets when a subscription may be attempted again, or <see langword="null" /> when nothing is holding it back.</summary>
        internal DateTimeOffset? RetryAfter { get; set; }

        /// <summary>Gets a value indicating whether the server answered the last attempt by advertising no subscription mechanism.</summary>
        /// <remarks>
        /// This is what tells a failed attempt apart from a declined one, and the two lead to different fallbacks: a
        /// server that cannot subscribe is asked to watch one folder at a time, while one that refused a connection is
        /// left alone until the degradation expires.
        /// </remarks>
        internal bool NotAdvertised { get; private set; }

        /// <summary>Reports whether the open subscription already watches exactly the supplied folders.</summary>
        internal bool Covers(IReadOnlyList<MailFolderResolution> folders) =>
            this.subscribedAliases.SequenceEqual(folders.Select(folder => folder.Alias.Value), StringComparer.Ordinal);

        /// <summary>Records an accepted subscription and the folders it covers.</summary>
        internal void Opened(
            AsyncServiceScope scope,
            IMailboxFolderSetNotificationSession session,
            MailSynchronizationOptions snapshot,
            IReadOnlyList<MailFolderResolution> folders)
        {
            this.Scope = scope;
            this.Session = session;
            this.Snapshot = snapshot;
            this.RetryAfter = null;
            this.NotAdvertised = false;
            this.subscribedAliases = [.. folders.Select(folder => folder.Alias.Value)];
        }

        /// <summary>Counts one attempt that failed rather than being declined.</summary>
        /// <returns>How many attempts have now failed in a row.</returns>
        /// <remarks>
        /// The capability answer is cleared with it. Reaching here means the server was asked and something else went
        /// wrong, so whatever an earlier attempt concluded about the mechanism is no longer what this one observed, and
        /// a decline left standing would send the account down the per-folder fallback that a failing subscription must
        /// not take.
        /// </remarks>
        internal int AttemptFailed()
        {
            this.NotAdvertised = false;

            return ++this.ConsecutiveFailureCount;
        }

        /// <summary>Records a server that advertises no mechanism for watching several folders over one connection.</summary>
        /// <param name="retryAfter">When the capability is asked about again.</param>
        /// <returns><see langword="true" /> when this is the first attempt the server declined; otherwise, <see langword="false" />.</returns>
        /// <remarks>
        /// A decline is not a failure and costs no failure count. It does set a retry instant, because reading the
        /// capability costs a connection and an authentication: asking a server that has never supported subscriptions
        /// once per run would spend one of those on every interval for as long as the process lives, while the folders
        /// are already being watched one at a time — a working push mode rather than a degradation.
        /// </remarks>
        internal bool DeclinedByServer(DateTimeOffset retryAfter)
        {
            var firstDecline = !this.NotAdvertised;

            this.NotAdvertised = true;
            this.ConsecutiveFailureCount = 0;
            this.RetryAfter = retryAfter;
            this.subscribedAliases = [];

            return firstDecline;
        }
    }
}
