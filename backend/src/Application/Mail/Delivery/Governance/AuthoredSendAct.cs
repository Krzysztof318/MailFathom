// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>Names what a caller asked for when it asked this deployment to send something.</summary>
/// <remarks>
/// It is the act rather than the name of whatever published it, because a use case has no business knowing which
/// protocol reached it. On the MCP surface each value is exactly one tool — a new message is <c>send_email</c>, the two
/// replies are <c>reply_to_email</c>, a forward is <c>forward_email</c>, and a draft dispatched as it stands is
/// <c>send_draft</c> — so a record naming the act names the tool call for anybody reading it, and stays correct for the
/// entrypoint added next.
/// </remarks>
public enum AuthoredSendAct
{
    /// <summary>A message that answers nothing.</summary>
    NewMessage = 0,

    /// <summary>An answer to whoever the message being answered asked for answers to.</summary>
    Reply = 1,

    /// <summary>An answer to everybody the message being answered was between.</summary>
    ReplyToAll = 2,

    /// <summary>A copy of a message this deployment holds, sent to people the original never named.</summary>
    Forward = 3,

    /// <summary>A message that was held as a draft, dispatched byte for byte as it was written.</summary>
    /// <remarks>
    /// The message was composed by one of the acts above, possibly long before and possibly under a policy that has
    /// since changed, and this names the act that put it on its way rather than the one that wrote it. What the caller
    /// is charged and audited for is this one, because promoting is what makes a message leave.
    /// </remarks>
    PromotedDraft = 4,
}
