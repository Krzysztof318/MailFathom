// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Transport;

namespace MailMcp.Application.Mail;

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
}
