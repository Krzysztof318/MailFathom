// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Selects who a reply is addressed to, as the protocol spells it.</summary>
/// <remarks>
/// <para>
/// It is a required argument with no default, and that is the whole reason it is an enumeration rather than a boolean.
/// The two values differ in who receives the message and in nothing else, so a caller that did not choose has not
/// asked for the safer one — it has asked for whichever this system happened to pick, and either pick is wrong half
/// the time: answering everybody when one person was meant publishes a private reply to every participant, and
/// answering one person when everybody was meant drops the rest of the conversation without saying so. Two named
/// values make the choice something a model states rather than something it omits.
/// </para>
/// <para>
/// The transport carries its own enumeration for the reason <see cref="SetMailFlagsKeywordChange" /> does: the member
/// names are the wire values — they are serialized camel-cased — so a rename inside the application would otherwise be
/// a silent change to the published tool contract.
/// </para>
/// </remarks>
internal enum ReplyAudience
{
    /// <summary>Only whoever the answered message asked for answers to, which is one mailbox.</summary>
    [Description("Addresses only the person who asked for answers — the original's Reply-To header where it set one, and its From address otherwise. Nobody else the original named receives anything. This is the private answer.")]
    SenderOnly = 0,

    /// <summary>Everybody the answered message was between, less the mailboxes the sending account owns.</summary>
    [Description("Addresses the person who asked for answers AND everybody the original named in its To and Cc headers, minus this account's own address. Every one of them receives the reply and sees the others. Choose it only when the answer is meant for the whole conversation.")]
    Everyone = 1,
}
