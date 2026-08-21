// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Domain.Mutations.Audit;

/// <summary>Names how a mutation ended, in the only two endings a change is allowed to have.</summary>
/// <remarks>
/// There is no pending member, because an entry is written when the mutation reaches a terminal stage and never before.
/// A trail whose entries could say "still happening" would need rewriting as the mutation moved, and a history that is
/// rewritten is not the thing an audit exists to hold.
/// </remarks>
public enum MailboxMutationAuditOutcome
{
    /// <summary>The mail server made the change that was asked for.</summary>
    Performed = 0,

    /// <summary>The change was given up on, and the entry's failure code says what it was given up on for.</summary>
    Abandoned = 1,
}
