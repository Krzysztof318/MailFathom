// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Mail;

/// <summary>Resolves the transport security policy configured for one mail account.</summary>
/// <remarks>
/// The policy is read once per mailbox operation and handed to the transport adapter as an input, so an adapter can
/// only narrow what it is given and never choose its own connection or authentication behavior. Implementations
/// return an immutable already-validated policy; they must fail rather than return a policy that violates a domain
/// transport security rule.
/// </remarks>
public interface IMailTransportSecurityPolicyReader
{
    /// <summary>Gets the validated transport security policy for a configured account.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <returns>The account's transport security policy.</returns>
    /// <exception cref="MailTransportSecurityPolicyViolationException">Thrown when the configured policy is unsafe.</exception>
    MailTransportSecurityPolicy GetPolicy(MailAccountId accountId);

    /// <summary>Gets the validated policy the same account's submission endpoint is reached under.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <returns>The policy, or <see langword="null" /> when the account configures no submission endpoint.</returns>
    /// <exception cref="MailTransportSecurityPolicyViolationException">Thrown when the configured policy is unsafe.</exception>
    /// <remarks>
    /// It is a second policy rather than the one above because the two endpoints are two servers: a provider that
    /// serves reading over implicit TLS commonly serves submission over STARTTLS on a port of its own, and a policy
    /// forced to describe both would make one of them wrong. Everything else about the account is shared, so the
    /// permitted mechanisms, the accepted weakenings, and the certificate authority are the same values judged by the
    /// same rules — a downgrade refused for reading is refused here identically.
    /// </remarks>
    MailTransportSecurityPolicy? GetDeliveryPolicy(MailAccountId accountId);
}
