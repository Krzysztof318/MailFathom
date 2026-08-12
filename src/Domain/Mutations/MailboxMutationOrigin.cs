// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Mutations;

/// <summary>Names what kind of act asked for a mutation.</summary>
/// <remarks>
/// The three differ in what makes a request the same request. A rule asks on its own, repeatedly, so its identity has to
/// survive the run it was evaluated in and change when the rule itself changes; an invocation asks once, so its identity
/// is the invocation; a classification asks on its own and repeatedly like a rule, but nobody authored it, so what stands
/// in for the rule's name is the profile the verdict was decided under. Keeping the kind beside the identity is what lets
/// an operator read a stuck mutation and know whether to look at a rule, at a scanner, or at something they did.
/// </remarks>
public enum MailboxMutationOrigin
{
    /// <summary>A rule matched an email and asked for the mutation without anybody present.</summary>
    Rule = 0,

    /// <summary>Somebody asked for the mutation directly, through a tool call or an administrative command.</summary>
    Command = 1,

    /// <summary>A spam classification reached a verdict the operator asked to be acted on.</summary>
    /// <remarks>
    /// It is a third kind rather than a rule with a reserved name, because neither of the other two describes it: nothing
    /// was authored, so it is not a rule, and nobody was present, so it is not a command. An operator reading a stuck
    /// mutation of this kind looks at the classification section rather than at a rule they wrote.
    /// </remarks>
    Classification = 2,
}
