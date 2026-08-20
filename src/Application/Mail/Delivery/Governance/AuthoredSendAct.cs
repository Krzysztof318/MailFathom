// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Mail.Delivery.Governance;

/// <summary>Names what a caller asked for when it asked this deployment to send something.</summary>
/// <remarks>
/// It is the act rather than the name of whatever published it, because a use case has no business knowing which
/// protocol reached it. On the MCP surface each value is exactly one tool — a new message is <c>send_email</c>, the two
/// replies are <c>reply_to_email</c>, and a forward is <c>forward_email</c> — so a record naming the act names the tool
/// call for anybody reading it, and stays correct for the entrypoint added next.
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
}
