// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Mcp.Tools.Senders;

/// <summary>Reports who reached an email's sender-authentication verdict.</summary>
/// <remarks>
/// The two values weigh differently and a client that treats them alike loses the distinction the value exists for.
/// One was reached by a party that observed the connection; the other by MailFathom, after delivery, from the signed
/// bytes and a published key.
/// </remarks>
internal enum SenderVerdictSource
{
    /// <summary>The verdict is what the receiving mail server wrote, read back out of its header.</summary>
    ReceivingServer = 0,

    /// <summary>MailFathom verified the email's own DKIM signatures, no trusted server statement being available.</summary>
    LocalVerification = 1,
}
