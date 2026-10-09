// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;

namespace MailFathom.Infrastructure.Mail;

/// <summary>Prepares a scope this boundary opens with the settings of the one account its work is for.</summary>
/// <remarks>
/// A scope sees an account's settings only once it has been prepared with that account, because no snapshot holds every
/// account a deployment serves. The host prepares the scopes it opens itself; this is how a scope opened here — a
/// connection kept open across several operations — is prepared before anything in it reads the account's endpoint,
/// credentials, or policy.
/// </remarks>
public interface IMailAccountSettingsScope
{
    /// <summary>Prepares the scope this instance belongs to with one account, before anything in it reads that account's settings.</summary>
    /// <param name="account">The account the scope's work is for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><see langword="true" /> once the scope holds the account; <see langword="false" /> when the account is not served, which leaves the scope holding none.</returns>
    Task<bool> UseAccountSettingsAsync(MailAccountId account, CancellationToken cancellationToken);
}
