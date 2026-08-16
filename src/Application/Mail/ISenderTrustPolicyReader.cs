// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.Mail;

/// <summary>Resolves the trusted senders one account judges its arriving mail by.</summary>
/// <remarks>
/// <para>
/// The list is per account because the accounts an instance synchronizes are different correspondence: a work account's
/// counterparties have nothing to do with a personal one's, and a single list would either recognize too much on one
/// account or make an owner maintain the union of both. What is deployment-wide is the set of domains the configured
/// accounts themselves use, which every account's policy is built with unless the deployment says otherwise.
/// </para>
/// <para>
/// The configured half is validated at startup, so this never fails and never returns a policy a caller has to check
/// again. An account this deployment no longer configures answers with
/// <see cref="SenderTrustPolicy.RecognizingNobody" /> for the reason every per-account reader here does: a
/// reading over mail whose account a reload removed must recognize nobody rather than throw.
/// </para>
/// </remarks>
public interface ISenderTrustPolicyReader
{
    /// <summary>Gets the policy one account's mail is judged by.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <returns>The effective policy, or <see cref="SenderTrustPolicy.RecognizingNobody" /> when the account is no longer configured.</returns>
    SenderTrustPolicy GetTrustPolicy(MailAccountId accountId);
}
