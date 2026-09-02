// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules.Conditions;

/// <summary>Names why a condition produced no answer for an email, which is never the same thing as answering no.</summary>
/// <remarks>
/// Validation catches an unknown fact and a comparison that cannot hold, so neither of those reaches here. What is left
/// is what only the email in front of the condition can cause — a value the expression could not work with, or a
/// resolution that took longer than the rule set allows — and both are recorded as a rule that failed rather than
/// silently folded into a match or a non-match.
/// </remarks>
public enum MailRuleConditionFailure
{
    /// <summary>The expression raised a failure while it was being evaluated for this email.</summary>
    EvaluationFaulted = 0,

    /// <summary>The evaluation, including resolving the facts it named, outlasted the timeout the rule set declares.</summary>
    EvaluationTimedOut = 1,
}
