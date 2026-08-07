// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Mutations;

/// <summary>Names what kind of authored act asked for a mutation.</summary>
/// <remarks>
/// The two differ in what makes a request the same request. A rule asks on its own, repeatedly, so its identity has to
/// survive the run it was evaluated in and change when the rule itself changes; an invocation asks once, so its identity
/// is the invocation. Keeping the kind beside the identity is what lets an operator read a stuck mutation and know
/// whether to look at a rule or at something they did.
/// </remarks>
public enum MailboxMutationOrigin
{
    /// <summary>A rule matched an email and asked for the mutation without anybody present.</summary>
    Rule = 0,

    /// <summary>Somebody asked for the mutation directly, through a tool call or an administrative command.</summary>
    Command = 1,
}
