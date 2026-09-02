// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.Host.Configuration.Mail.Readers;

/// <summary>Reads which authentication results an account believes from the bound section.</summary>
internal sealed class ConfiguredTrustedAuthenticationAuthorityReader(MailSynchronizationOptions settings)
    : ITrustedAuthenticationAuthorityReader
{
    /// <inheritdoc />
    /// <remarks>
    /// An account this snapshot no longer names answers with none rather than failing, for the reason the audit
    /// settings do: the extraction backfill runs over accounts a reload may have removed between one run and the
    /// next, and believing no header there is the honest answer as well as the safe one.
    /// </remarks>
    public TrustedAuthenticationAuthority GetTrustedAuthority(MailAccountId accountId) =>
        settings.FindConfiguredAccount(accountId)?.CreateTrustedAuthority() ?? TrustedAuthenticationAuthority.None;
}
