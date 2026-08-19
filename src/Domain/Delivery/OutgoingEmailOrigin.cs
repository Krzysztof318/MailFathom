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
/// Nothing composes a message on its own initiative, and every member here names an act somebody authored: a schedule
/// asks on an occasion rather than on its own behalf, and what it repeats is a message its owner wrote once.
/// </para>
/// </remarks>
public enum OutgoingEmailOrigin
{
    /// <summary>A rule matched an email and asked for the message without anybody present.</summary>
    Rule = 0,

    /// <summary>Somebody asked for the message directly, through a tool call or an administrative command.</summary>
    Command = 1,

    /// <summary>An occasion of a recurring send the owner declared came round, and asked for that occurrence.</summary>
    /// <remarks>
    /// The act was authored once and the occasion is what asks, which is why the identity is the declaration and the
    /// occasion together rather than a key somebody supplies per occurrence: nobody is present when the message is
    /// composed, and two instances reaching one occasion have to compose one request rather than two messages.
    /// </remarks>
    Schedule = 2,
}
