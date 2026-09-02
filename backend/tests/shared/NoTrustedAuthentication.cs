// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails.Authentication;

namespace MailFathom.TestSupport;

/// <summary>An account that believes no server's authentication statements, which is a configured deployment state.</summary>
/// <remarks>
/// Written out rather than substituted, because a substitute records every call it is given and the recording is an
/// allocation charged to the path being measured. What this suite needs of the reader is one answer and no memory of
/// having been asked.
/// </remarks>
internal sealed class NoTrustedAuthentication : ITrustedAuthenticationAuthorityReader
{
    /// <inheritdoc />
    public TrustedAuthenticationAuthority GetTrustedAuthority(MailAccountId accountId) =>
        TrustedAuthenticationAuthority.None;
}
