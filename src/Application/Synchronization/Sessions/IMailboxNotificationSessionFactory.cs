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
