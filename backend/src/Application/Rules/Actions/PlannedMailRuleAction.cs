// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Actions;

/// <summary>One action a pass will ask the mailbox for, and the rule that asked.</summary>
/// <param name="RuleName">The name of the rule the action was declared on, which is what the request is identified by.</param>
/// <param name="Action">The change asked for.</param>
/// <param name="Position">Where the action sits in the order its own rule declares its changes, counted from zero.</param>
/// <remarks>
/// The rule name travels with the action because it is half of the request's idempotency identity: the same rule asking
/// again for the same email is the same request, and a different rule asking for the same change is a different one.
/// It carries no mail content, being MailFathom's own configured name for the rule.
/// <para>
/// The position is the rule's own, not the plan's. A plan reorders across rules so that a flag is written before the
/// occurrence carrying it is moved, so the plan's order says when a change is applied while this says which of the
/// rule's declared changes it is — which is what a record of the decision has to name it by.
/// </para>
/// </remarks>
public sealed record PlannedMailRuleAction(string RuleName, MailRuleAction Action, int Position);
