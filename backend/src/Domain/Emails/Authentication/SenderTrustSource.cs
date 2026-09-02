// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>Which part of what this deployment knows recognized an authenticated author.</summary>
/// <remarks>
/// A verdict that says only <em>trusted</em> cannot be reviewed. Trust an operator declared when they set the
/// deployment up, trust that follows from the accounts they synchronize, and trust somebody granted while it was running
/// are three different statements, and the last of them is the one worth being able to find again — particularly where
/// the somebody was an agent. So the verdict names the half that matched rather than only the fact that one did.
/// </remarks>
public enum SenderTrustSource
{
    /// <summary>Nothing recognized the author, which is what every <see cref="SenderTrustLevel.Unknown" /> verdict carries.</summary>
    None = 0,

    /// <summary>The author's domain belongs to an account this deployment synchronizes.</summary>
    /// <remarks>
    /// Every configured account's domain counts and not only the receiving one, because an instance synchronizing a
    /// work mailbox and a personal one is synchronizing one person's correspondence. It is a default rather than the
    /// only option, and a deployment whose accounts sit on a large shared provider is the case for turning it off.
    /// </remarks>
    OwnAccountDomain = 1,

    /// <summary>An entry on the receiving account's configured trusted-sender list named the author.</summary>
    ConfiguredTrustedSender = 2,

    /// <summary>An entry somebody added to the receiving account's stored trusted-sender list named the author.</summary>
    /// <remarks>
    /// The stored half is what a reader adds without editing a file and restarting. Where both halves name the same
    /// author the configured one is reported, so a deployment's declared trust is never described as something added
    /// later.
    /// </remarks>
    StoredTrustedSender = 3,
}
