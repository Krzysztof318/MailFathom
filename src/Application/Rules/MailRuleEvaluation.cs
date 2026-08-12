// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Conditions;

namespace MailFathom.Application.Rules;

/// <summary>What one rule concluded about one email, and why, when it concluded nothing.</summary>
public sealed record MailRuleEvaluation
{
    private MailRuleEvaluation(string ruleName, MailRuleOutcome outcome, MailRuleConditionFailure? failure)
    {
        this.RuleName = ruleName;
        this.Outcome = outcome;
        this.Failure = failure;
    }

    /// <summary>Gets the name of the rule this is about.</summary>
    public string RuleName { get; }

    /// <summary>Gets what the rule concluded.</summary>
    public MailRuleOutcome Outcome { get; }

    /// <summary>Gets why the rule produced no answer, which is absent for every rule that produced one.</summary>
    /// <remarks>
    /// Absence carries its own meaning here rather than standing in for a value: a rule that matched or did not match
    /// has no failure to name, and a reason with a member meaning "none" would let a caller read a successful evaluation
    /// as a failure with an unremarkable cause.
    /// </remarks>
    public MailRuleConditionFailure? Failure { get; }

    /// <summary>Records that the condition answered that the email matches.</summary>
    /// <param name="ruleName">The rule's name.</param>
    /// <returns>The evaluation.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ruleName" /> is empty or whitespace.</exception>
    public static MailRuleEvaluation Matched(string ruleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);

        return new MailRuleEvaluation(ruleName, MailRuleOutcome.Matched, failure: null);
    }

    /// <summary>Records that the condition answered that the email does not match.</summary>
    /// <param name="ruleName">The rule's name.</param>
    /// <returns>The evaluation.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ruleName" /> is empty or whitespace.</exception>
    public static MailRuleEvaluation NotMatched(string ruleName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);

        return new MailRuleEvaluation(ruleName, MailRuleOutcome.NotMatched, failure: null);
    }

    /// <summary>Records that the condition produced no answer for this email.</summary>
    /// <param name="ruleName">The rule's name.</param>
    /// <param name="failure">Why no answer was produced.</param>
    /// <returns>The evaluation.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="ruleName" /> is empty or whitespace.</exception>
    public static MailRuleEvaluation Failed(string ruleName, MailRuleConditionFailure failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleName);

        return new MailRuleEvaluation(ruleName, MailRuleOutcome.Failed, failure);
    }
}
