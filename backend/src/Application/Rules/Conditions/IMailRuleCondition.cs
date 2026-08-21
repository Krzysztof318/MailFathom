// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Facts;

namespace MailFathom.Application.Rules.Conditions;

/// <summary>A condition that has been parsed, checked against the fact surface, and proven to answer with a boolean.</summary>
/// <remarks>
/// <para>
/// The whole of what the rest of the system knows about the condition language. A compiled condition carries no
/// expression, no parser state, and no authored text — the text is the operator's configuration and stays there, which
/// is what keeps an address somebody typed into a condition out of every record that names the rule.
/// </para>
/// <para>
/// An implementation may throw. Totality is a classification the evaluator applies rather than a property of the
/// evaluator underneath, so a condition that cannot be evaluated for the email in front of it raises the failure and
/// <see cref="MailRuleSetEvaluator" /> turns it into a failed rule with a reason. What an implementation must never do
/// is answer <see langword="false" /> because something went wrong.
/// </para>
/// </remarks>
public interface IMailRuleCondition
{
    /// <summary>Gets the facts the condition names, which is every fact it can cause to be resolved and no other.</summary>
    IReadOnlyList<MailRuleFact> ReferencedFacts { get; }

    /// <summary>Evaluates the condition for one email.</summary>
    /// <param name="facts">The fact surface for the email being evaluated.</param>
    /// <param name="cancellationToken">Cancels the evaluation, and is what the evaluation timeout arrives through.</param>
    /// <returns>Whether the email matches the condition.</returns>
    /// <exception cref="OperationCanceledException">Thrown when the evaluation is cancelled or its timeout elapses.</exception>
    Task<bool> EvaluateAsync(MailRuleFacts facts, CancellationToken cancellationToken);
}
