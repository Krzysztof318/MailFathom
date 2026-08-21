// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Mail.Delivery;

/// <summary>Opens the one session in MailFathom able to reach a submission server.</summary>
/// <remarks>
/// <para>
/// It is a separate factory from every mailbox session factory rather than a second method on one, because the
/// separation is what the guarantee rests on: a component that never takes this dependency cannot obtain a session
/// that sends, whatever a later change does inside it.
/// </para>
/// <para>
/// Nothing is pooled. A mailbox session is held open because a run reads a folder many times, while a submission is
/// one exchange whose failure must never be repeated on a caller's behalf, so each delivery opens its own connection
/// and closes it. That also keeps a submission endpoint from counting a MailFathom deployment as a permanently
/// connected client.
/// </para>
/// </remarks>
public interface IMailDeliverySessionFactory
{
    /// <summary>Connects and authenticates against the account's submission server.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy the implementation must obey.</param>
    /// <param name="cancellationToken">Cancels resolving the credential, connecting, and authenticating.</param>
    /// <returns>An open delivery session the caller owns and must dispose.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transportSecurityPolicy" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the account configures no submission endpoint, which startup validation settles for every configured account.</exception>
    /// <exception cref="MailDeliveryUnavailableException">Thrown when the submission server did not accept the session within its configured resilience budget.</exception>
    /// <remarks>
    /// The policy is an input rather than something the implementation resolves, so an adapter cannot widen the
    /// permitted authentication mechanisms or downgrade the connection on its own. It is the account's own policy with
    /// the submission endpoint's connection security in it, because a provider that serves reading over implicit TLS
    /// commonly serves submission over STARTTLS, while which credentials may cross either channel is one decision.
    /// </remarks>
    Task<IMailDeliverySession> OpenForDeliveryAsync(
        MailAccountId accountId,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken);
}
