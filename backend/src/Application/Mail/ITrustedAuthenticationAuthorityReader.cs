// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Application.Mail;

/// <summary>Resolves the one server whose sender-authentication statements an account believes.</summary>
/// <remarks>
/// <para>
/// The authserv-id is a property of who receives that account's mail rather than of MailFathom, so it is per-account
/// configuration and there is no value this system could pick on a deployment's behalf. An account that names none is
/// an ordinary state, not a failure: it reads as <see cref="TrustedAuthenticationAuthority.None" /> and every message it
/// holds carries the not-established verdict.
/// </para>
/// <para>
/// The configured value is validated at startup, so this never fails and never returns something a reading has to check
/// again. An account this deployment no longer configures answers with <see cref="TrustedAuthenticationAuthority.None" />
/// for the same reason: a reading over mail whose account a reload removed must believe nothing rather than throw.
/// </para>
/// </remarks>
public interface ITrustedAuthenticationAuthorityReader
{
    /// <summary>Gets the authority one account believes.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <returns>The configured authority, or <see cref="TrustedAuthenticationAuthority.None" /> when the account names one no longer or never did.</returns>
    TrustedAuthenticationAuthority GetTrustedAuthority(MailAccountId accountId);
}
