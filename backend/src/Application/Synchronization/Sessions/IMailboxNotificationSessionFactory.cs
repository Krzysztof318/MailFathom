// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Synchronization;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Synchronization.Sessions;

/// <summary>Opens the long-lived session a folder in push mode waits on.</summary>
public interface IMailboxNotificationSessionFactory
{
    /// <summary>Opens a session that waits for changes to one folder, or reports that the server cannot serve one.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="folder">The alias binding whose remote path the session waits on, selected read-only.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy the implementation must obey.</param>
    /// <param name="cancellationToken">Cancels connecting, authenticating, and selecting the folder.</param>
    /// <returns>An open session the caller owns and must dispose, or the statement that the server advertises no push mechanism.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folder" /> is <see langword="null" />.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not accept the session within its configured resilience budget.</exception>
    /// <remarks>
    /// <para>
    /// A server that advertises no push mechanism is an ordinary answer rather than a failure, because the caller polls
    /// instead and the account keeps synchronizing. It is reported as a result for that reason, and reported at all
    /// because an operator who configured push needs to see that the server declined it.
    /// </para>
    /// <para>
    /// The capability is decided per open session rather than once per process. A server can gain or lose the mechanism
    /// across a restart or behind a load balancer, and a session that re-establishes itself asks again.
    /// </para>
    /// </remarks>
    Task<MailboxNotificationSessionResult> OpenAsync(
        MailAccountId accountId,
        MailFolderResolution folder,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken);

    /// <summary>Opens one session that watches several folders, or reports that the server cannot serve one.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="folders">The alias bindings to watch, in the order they are subscribed and led by the one the session selects.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy the implementation must obey.</param>
    /// <param name="cancellationToken">Cancels connecting, authenticating, selecting, and subscribing.</param>
    /// <returns>An open session watching every supplied folder, or the statement that the server advertises no mechanism for watching more than one.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="folders" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="folders" /> is empty.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not accept the session within its configured resilience budget.</exception>
    /// <remarks>
    /// <para>
    /// The caller decides how many folders to ask for, because the bound is an operator's answer about a server rather
    /// than something an adapter can discover: a subscription naming more mailboxes than the server will accept is
    /// refused as a whole, and the folders left out are polled instead of being dropped.
    /// </para>
    /// <para>
    /// A server that advertises no such mechanism is an ordinary answer rather than a failure, and the caller falls back
    /// to whatever it does for one folder at a time. It is reported for the same reason the single-folder result is:
    /// an operator who configured push needs to see which mechanism the server actually agreed to.
    /// </para>
    /// </remarks>
    Task<MailboxFolderSetNotificationSessionResult> OpenForFoldersAsync(
        MailAccountId accountId,
        IReadOnlyList<MailFolderResolution> folders,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken);
}

/// <summary>Reports whether a folder can be watched for changes, and the session to watch it with.</summary>
/// <param name="EffectiveMode">
/// What the folder actually got: <see cref="MailSynchronizationMode.Push" /> when a session was opened, and
/// <see cref="MailSynchronizationMode.Polling" /> when the server advertises no push mechanism.
/// </param>
/// <param name="Session">
/// The open session, which is present exactly when <paramref name="EffectiveMode" /> is
/// <see cref="MailSynchronizationMode.Push" /> and is the caller's to dispose.
/// </param>
public sealed record MailboxNotificationSessionResult(
    MailSynchronizationMode EffectiveMode,
    IMailboxNotificationSession? Session)
{
    /// <summary>Reports a folder the server will report changes for.</summary>
    /// <param name="session">The open session the caller owns.</param>
    /// <returns>A result carrying the session.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    public static MailboxNotificationSessionResult Watching(IMailboxNotificationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new MailboxNotificationSessionResult(MailSynchronizationMode.Push, session);
    }

    /// <summary>Reports a server that advertises no push mechanism, so the folder is polled instead.</summary>
    /// <returns>A result carrying no session.</returns>
    public static MailboxNotificationSessionResult PushNotAdvertised() =>
        new(MailSynchronizationMode.Polling, Session: null);
}

/// <summary>Reports whether one session can watch a set of folders, and the session to watch them with.</summary>
/// <param name="EffectiveMode">
/// What the folders actually got: <see cref="MailSynchronizationMode.Push" /> when a session was opened, and
/// <see cref="MailSynchronizationMode.Polling" /> when the server advertises no mechanism for watching a set.
/// </param>
/// <param name="Session">
/// The open session, which is present exactly when <paramref name="EffectiveMode" /> is
/// <see cref="MailSynchronizationMode.Push" /> and is the caller's to dispose.
/// </param>
/// <remarks>
/// A declined subscription says nothing about whether one folder at a time can be watched, so a caller that receives
/// <see cref="MailSynchronizationMode.Polling" /> here still has the single-folder session to try.
/// </remarks>
public sealed record MailboxFolderSetNotificationSessionResult(
    MailSynchronizationMode EffectiveMode,
    IMailboxFolderSetNotificationSession? Session)
{
    /// <summary>Reports a set of folders the server will report changes for over one connection.</summary>
    /// <param name="session">The open session the caller owns.</param>
    /// <returns>A result carrying the session.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="session" /> is <see langword="null" />.</exception>
    public static MailboxFolderSetNotificationSessionResult Watching(IMailboxFolderSetNotificationSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new MailboxFolderSetNotificationSessionResult(MailSynchronizationMode.Push, session);
    }

    /// <summary>Reports a server that advertises no mechanism for watching several folders over one connection.</summary>
    /// <returns>A result carrying no session.</returns>
    public static MailboxFolderSetNotificationSessionResult SubscriptionNotAdvertised() =>
        new(MailSynchronizationMode.Polling, Session: null);
}
