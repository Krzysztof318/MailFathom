// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Facts;

namespace MailFathom.Application.Rules;

/// <summary>What one rule concluded about one email, why it concluded nothing, and what the conclusion cost.</summary>
/// <remarks>
/// The facts and the duration are carried because they are what the recorded history explains a decision from, and the
/// evaluator is the only place either is observable: the facts a condition reached are known to the fact surface rather
/// than to the text, and how long an answer took is known to whoever bounded it.
/// </remarks>
public sealed record MailRuleEvaluation
{
    private MailRuleEvaluation(
        string ruleName,
        MailRuleOutcome outcome,
        MailRuleConditionFailure? failure,
        IReadOnlyList<MailRuleFact> readFacts,
        TimeSpan duration)
    {
        this.RuleName = ruleName;
        this.Outcome = outcome;
        this.Failure = failure;
        this.ReadFacts = readFacts;
        this.Duration = duration;
    }

    /// <summary>Gets the name of the rule this is about.</summary>
    public string RuleName { get; }

    /// <summary>Gets what the rule concluded.</summary>
    public MailRuleOutcome Outcome { get; }

    /// <summary>Gets the facts the condition reached, in the order it first reached each.</summary>
    /// <remarks>
    /// Empty for a condition that compared no fact at all, and for one whose evaluation failed before it reached one. A
    /// fact appears here whether it was computed or read from what an earlier rule of the same pass had computed, because
    /// the question this answers is what the condition needed rather than what the email cost.
    /// </remarks>
    public IReadOnlyList<MailRuleFact> ReadFacts { get; }

    /// <summary>Gets how long the condition took to answer, including resolving the facts it read.</summary>
    public TimeSpan Duration { get; }

    /// <summary>Gets why the rule produced no answer, which is absent for every rule that produced one.</summary>
    /// <remarks>
    /// Absence carries its own meaning here rather than standing in for a value: a rule that matched or did not match
    /// has no failure to name, and a reason with a member meaning "none" would let a caller read a successful evaluation
    /// as a failure with an unremarkable cause.
    /// </remarks>
    public MailRuleConditionFailure? Failure { get; }

    /// <summary>Records that the condition answered that the email matches.</summary>
    /// <param name="ruleName">The rule's name.</param>
    /// <param name="readFacts">The facts the condition reached, or <see langword="null" /> where the caller observed none.</param>
    /// <param name="duration">How long the condition took to answer.</param>
    /// <returns>The evaluation.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ruleName" /> is empty or whitespace.</exception>
    public static MailRuleEvaluation Matched(
        string ruleName,
        IReadOnlyList<MailRuleFact>? readFacts = null,
        TimeSpan duration = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);

        return new MailRuleEvaluation(ruleName, MailRuleOutcome.Matched, failure: null, [.. readFacts ?? []], duration);
    }

    /// <summary>Records that the condition answered that the email does not match.</summary>
    /// <param name="ruleName">The rule's name.</param>
    /// <param name="readFacts">The facts the condition reached, or <see langword="null" /> where the caller observed none.</param>
    /// <param name="duration">How long the condition took to answer.</param>
    /// <returns>The evaluation.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ruleName" /> is empty or whitespace.</exception>
    public static MailRuleEvaluation NotMatched(
        string ruleName,
        IReadOnlyList<MailRuleFact>? readFacts = null,
        TimeSpan duration = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);

        return new MailRuleEvaluation(ruleName, MailRuleOutcome.NotMatched, failure: null, [.. readFacts ?? []], duration);
    }

    /// <summary>Records that the condition produced no answer for this email.</summary>
    /// <param name="ruleName">The rule's name.</param>
    /// <param name="failure">Why no answer was produced.</param>
    /// <param name="readFacts">The facts the condition reached before it stopped answering.</param>
    /// <param name="duration">How long the condition ran before it stopped answering.</param>
    /// <returns>The evaluation.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ruleName" /> is empty or whitespace.</exception>
    public static MailRuleEvaluation Failed(
        string ruleName,
        MailRuleConditionFailure failure,
        IReadOnlyList<MailRuleFact>? readFacts = null,
        TimeSpan duration = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);

        return new MailRuleEvaluation(ruleName, MailRuleOutcome.Failed, failure, [.. readFacts ?? []], duration);
    }
}
