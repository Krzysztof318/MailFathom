// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Senders;

/// <summary>Reports whether this deployment recognizes an email's authenticated author, as the protocol spells it.</summary>
/// <remarks>
/// It is this deployment's own classification rather than the result of any check, which is why it is published beside
/// <see cref="AuthorAuthenticationState" /> and never folded into it.
/// </remarks>
internal enum DeploymentTrustState
{
    /// <summary>This deployment recognizes nobody in the email, which is the ordinary state of legitimate mail.</summary>
    Unknown = 0,

    /// <summary>The email's authenticated author is one this deployment recognizes.</summary>
    Trusted = 1,
}
