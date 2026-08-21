// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>Answers whether this deployment may send as an account at all, before anything about a message is looked at.</summary>
/// <remarks>
/// <para>
/// It is a reader of its own rather than a value handed to the outbox, because the answer is per account and an account
/// list is reloaded while the process runs: an operator turning sending off reaches the next send rather than the next
/// restart.
/// </para>
/// <para>
/// What it reports is a capability rather than a grant. Whether the caller may ask is
/// <see cref="Domain.Access.MailFathomPermission.MailSend" />, which the outbox asks separately and which an operator
/// writes on a credential; this is whether the deployment can send as this mailbox whoever is asking, so work nobody
/// called — a rule, a worker — meets it identically.
/// </para>
/// </remarks>
public interface IOutgoingSendPermissionReader
{
    /// <summary>Reports why this deployment may not send as an account.</summary>
    /// <param name="accountId">The account a message would be sent as.</param>
    /// <returns>The reason sending is refused, or <see langword="null" /> when this deployment may send as that account.</returns>
    /// <remarks>
    /// An account this deployment does not serve is refused as one nobody turned sending on for, which is what it is:
    /// the switch that would admit it exists on no account of this installation. Answering anything else would let a
    /// refusal report which accounts are configured.
    /// </remarks>
    OutgoingSendRefusalReason? FindRefusal(MailAccountId accountId);
}
