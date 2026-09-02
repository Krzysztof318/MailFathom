// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.History;

/// <summary>What became of one action a matching rule declared, as far as the pass that matched it is concerned.</summary>
/// <remarks>
/// It stops where the pass stops. A requested action's lifecycle on the server — attempted, converged, dead-lettered —
/// belongs to the mutation record the execution points at, and copying it here would leave two answers to what happened
/// to one message.
/// </remarks>
public enum MailRuleExecutedActionOutcome
{
    /// <summary>A mutation record was opened for the action, and the account's convergence pass carries it from there.</summary>
    Requested = 0,

    /// <summary>Another rule matching the same email had already settled what this action would have decided.</summary>
    /// <remarks>
    /// Not a failure. Two rules filing one email into different folders is resolved by declared order, and the rule
    /// declared later is the one that gives way — which is a fact about the rule set rather than about the message.
    /// </remarks>
    Withheld = 1,

    /// <summary>The action was reached and nothing was recorded for it, because what permitted it has stopped being true.</summary>
    Refused = 2,
}
