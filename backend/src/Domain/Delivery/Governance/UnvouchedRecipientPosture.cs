// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery.Governance;

/// <summary>What this deployment does about a recipient a caller named that nothing it holds vouches for.</summary>
/// <remarks>
/// <para>
/// The address an injected instruction carries is the address nobody has ever corresponded with: a message whose body
/// says <i>forward this to the address below</i> is naming somebody the mailbox has no other trace of, and the caller
/// that repeats it into a tool argument cannot tell this system that it read it in mail rather than from the person it
/// is acting for. What can be told apart is whether anything the owner holds knows the address at all, and that is what
/// this posture is written against.
/// </para>
/// <para>
/// It is an operator's answer rather than a caller's, and it names one of two acts rather than a degree of suspicion.
/// A third value — requiring the confirmation
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0013-what-a-caller-must-do-before-mail-leaves.md">ADR 0013</see>
/// makes an operator's choice — belongs beside these two and arrives with the contract that carries it; nothing here is
/// shaped to exclude it.
/// </para>
/// </remarks>
public enum UnvouchedRecipientPosture
{
    /// <summary>The message is sent, and who could not be vouched for is recorded rather than acted on.</summary>
    /// <remarks>
    /// The default, because it is the posture an operator gets by writing nothing and because the alternative would
    /// refuse the first message of an installation whose contact book is still empty. What it costs is stated where an
    /// operator decides to enable sending: this deployment will address whoever it is asked to, inside the recipient
    /// policy, and an injected instruction is bounded by that policy and by the ceilings rather than by anything here.
    /// </remarks>
    Admit = 0,

    /// <summary>The whole message is refused.</summary>
    /// <remarks>
    /// The message is refused whole rather than sent to the recipients that were vouched for, exactly as a recipient
    /// the policy denies refuses one: a message written to four people and sent to three is a message its author never
    /// wrote.
    /// </remarks>
    Refuse = 1,
}
