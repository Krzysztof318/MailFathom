// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Accounts;

/// <summary>Which copy of a mail account's mailbox is the truth at this moment.</summary>
/// <remarks>
/// The phase is stored beside the account rather than configured, because an administrator switches it for one account
/// and a switch is a period of work rather than an instant. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </remarks>
public enum MailAccountCustodyPhase
{
    /// <summary>The source server is the truth, and MailFathom keeps a copy of it.</summary>
    Mirrored = 0,

    /// <summary>MailFathom is the truth, and the source is drained of what MailFathom holds.</summary>
    Held = 1,

    /// <summary>MailFathom is still the truth while the mailbox is appended back to the source.</summary>
    Restoring = 2,
}
