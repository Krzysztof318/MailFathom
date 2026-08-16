// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Delivery;

/// <summary>Names what kind of authored act asked for a message to be sent.</summary>
/// <remarks>
/// The two differ in what makes a request the same request. A rule asks on its own, repeatedly, so its identity has to
/// survive the run it was evaluated in and change when the rule itself changes; somebody present asks once, so the
/// identity is theirs to supply and a retried call carries the one the first call carried. Keeping the kind beside the
/// identity is what lets an operator read a stuck send and know whether to look at a rule or at something they did.
/// <para>
/// Nothing composes a message on its own initiative, which is why there is no third member for one: every outgoing
/// message originates from an act somebody authored.
/// </para>
/// </remarks>
public enum OutgoingEmailOrigin
{
    /// <summary>A rule matched an email and asked for the message without anybody present.</summary>
    Rule = 0,

    /// <summary>Somebody asked for the message directly, through a tool call or an administrative command.</summary>
    Command = 1,
}
