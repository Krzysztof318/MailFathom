// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Accounts;

/// <summary>What an administrator has asked MailFathom to do with one mail account's mailbox.</summary>
/// <remarks>
/// <para>
/// This is the value somebody asked for; <see cref="MailAccountCustodyPhase" /> is how far the work of granting it has
/// got. The two are separate because a switch in either direction is a period of background work rather than an
/// instant, and a requested value that also had to say how far it had got could not say either thing clearly.
/// </para>
/// <para>
/// It is never a configuration key. An administrator switches it for one user's one mail account through a command of
/// its own, and what was asked for is stored in the database beside the account, because emptying somebody's source
/// server is a deliberate act against one mailbox rather than a setting copied between deployments. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0034-holding-a-mailbox-mailfathom-alone-keeps.md">ADR 0034</see>.
/// </para>
/// </remarks>
public enum MailAccountCustody
{
    /// <summary>The source server keeps the mailbox and MailFathom keeps a copy of it, which is what every account has until somebody switches it.</summary>
    MirrorSource = 0,

    /// <summary>MailFathom holds the mailbox and empties the source of everything it durably holds.</summary>
    HoldMailbox = 1,
}
