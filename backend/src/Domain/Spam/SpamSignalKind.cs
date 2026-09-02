// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Spam;

/// <summary>Names what kind of fact one signal states about a message.</summary>
/// <remarks>
/// Each kind is recorded as itself rather than folded into one number, because the kinds are not comparable: an
/// authentication result is what the receiving server established at the moment it mattered, a provider score is that
/// provider's own opinion, and a folder placement is a decision somebody already acted on. A reader diagnosing a wrong
/// classification needs to know which of the three it rested on.
/// </remarks>
public enum SpamSignalKind
{
    /// <summary>An SPF, DKIM, or DMARC result the receiving server recorded for the message it accepted.</summary>
    SenderAuthentication = 0,

    /// <summary>The same kind of result preserved across a forwarding hop, where SPF and DKIM legitimately break.</summary>
    ForwardedSenderAuthentication = 1,

    /// <summary>A spam verdict, flag, or score the provider wrote into the message before MailFathom saw it.</summary>
    ProviderSpamVerdict = 2,

    /// <summary>The message being stored in the folder the account advertises as its junk folder.</summary>
    JunkFolderPlacement = 3,

    /// <summary>One rule of a scanner's corpus that fired on the message.</summary>
    ScannerRule = 4,
}
