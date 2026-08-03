// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
    private readonly ConcurrentDictionary<string, FakeMailboxNotificationSession> sessionsByAlias = new(StringComparer.Ordinal);

    /// <summary>Gets or sets whether the modelled server advertises a push mechanism at all.</summary>
    internal bool AdvertisesPush { get; set; } = true;

    /// <summary>Gets or sets the failure every open ends with, which models a server that keeps refusing the session.</summary>
    internal Exception? OpenFailure { get; set; }

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

    /// <summary>Gets whether this session has been disposed, which is how a recycle and a shutdown are told from a leak.</summary>
    internal bool IsDisposed { get; private set; }

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

        using var renewalDeadline = new CancellationTokenSource(maxWait, timeProvider);
        using var endOfWait = CancellationTokenSource.CreateLinkedTokenSource(
            renewalDeadline.Token,
            cancellationToken);

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

        return ValueTask.CompletedTask;
    }
}
