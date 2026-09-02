// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.MailKit.Delivery;

/// <summary>Whether a submission server's refusal is one that can clear on its own.</summary>
internal enum SmtpRejectionDisposition
{
    /// <summary>The server refused for now and invited the client to come back, which RFC 5321 gives the 4yz reply class to.</summary>
    Transient = 0,

    /// <summary>The server has decided, and every later attempt receives the same answer.</summary>
    /// <remarks>
    /// It is also what an unrecognized refusal becomes. A submission repeated against a server whose answer nobody
    /// understood is how a second copy reaches a recipient's mailbox, so anything that is not explicitly an invitation
    /// to return is treated as settled.
    /// </remarks>
    Permanent = 1,
}
