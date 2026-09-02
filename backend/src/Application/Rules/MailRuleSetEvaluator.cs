// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Facts;

namespace MailFathom.Application.Rules;

/// <summary>Runs a rule set over one email in declared order, bounding each condition and classifying every failure.</summary>
/// <remarks>
/// <para>
/// What the matching rules ask the mailbox for is composed here as well, because composing it needs the matched rules
/// in declared order and this is the only place that order and those rules are held together. A rule that failed asked
/// for nothing, since it did not match.
/// </para>
/// <para>
/// A rule scoped to accounts this email does not belong to is passed over rather than evaluated, and leaves no
/// evaluation behind: it did not decline to match, it was not one of this account's rules. That also means it cannot end
/// the pass, whatever it declares, so narrowing a stopping rule to one account is a statement about that account alone.
/// A rule the walk's reach does not hold is passed over on exactly the same terms, so the history stays a record of
/// decisions rather than of rules that were never asked.
/// </para>
/// <para>
/// The one place totality is applied. The expression evaluator underneath raises failures rather than returning them,
/// so every way a condition can fail to answer is caught here and recorded as a rule that failed with a reason. A rule
/// that failed did not match, so the pass carries on to the rules below it: a single unlucky email must not stop a rule
/// set that works from being applied.
/// </para>
/// <para>
/// The timeout is applied per rule rather than per pass, because it exists to bound one condition rather than to bound
/// how much work an email is worth. It reaches the fact resolution as well as the expression, which is what makes it
/// the enforceable half of the cost bound: the shape of an expression is already limited when the configuration is
/// read, and what nothing can read from the text is how long a stored-content read will take.
/// </para>
/// </remarks>
public sealed class MailRuleSetEvaluator
{
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the evaluator.</summary>
    /// <param name="timeProvider">Times the evaluation timeout each rule set declares.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="timeProvider" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The timeout is read from the rule set rather than held here, so an operator who changes it gets the new value on
    /// the next pass along with whatever else they changed, instead of on the next restart.
    /// </remarks>
    public MailRuleSetEvaluator(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.timeProvider = timeProvider;
    }

    /// <summary>Evaluates every rule of the set against one email until a matching rule ends the pass.</summary>
    /// <param name="ruleSet">The bound rule set, whose declared order is the order the rules are reached in.</param>
    /// <param name="facts">The fact surface for the email, which resolves each fact at most once for the whole pass.</param>
    /// <param name="reach">Which rules this walk runs: the ones its trigger reaches, or every rule somebody asked for.</param>
    /// <param name="cancellationToken">Cancels the pass, which is reported as cancellation rather than as a failed rule.</param>
    /// <returns>What each rule the pass reached concluded, under the rule set's revision.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancellationToken" /> is cancelled.</exception>
    public async Task<MailRuleSetEvaluation> EvaluateAsync(
        MailRuleSet ruleSet,
        MailRuleFacts facts,
        MailRuleReach reach,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(reach);

        var evaluations = new List<MailRuleEvaluation>(ruleSet.Rules.Count);
        var matchedRules = new List<MailRule>();
        var stoppedEarly = false;

        foreach (var rule in ruleSet.Rules)
        {
            if (!rule.AppliesTo(facts.Account) || !reach.Reaches(rule))
            {
                continue;
            }

            var evaluation = await this.EvaluateRuleAsync(rule, facts, ruleSet.Bounds, cancellationToken);

            evaluations.Add(evaluation);

            if (evaluation.Outcome != MailRuleOutcome.Matched)
            {
                continue;
            }

            matchedRules.Add(rule);

            if (rule.StopWhenMatched)
            {
                stoppedEarly = true;

                break;
            }
        }

        return MailRuleSetEvaluation.Create(
            ruleSet.Revision,
            evaluations,
            stoppedEarly,
            MailRuleActionPlan.Compose(matchedRules));
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "A condition that fails for any reason must be recorded as a failed rule rather than end the pass, which is the totality this type exists to apply.")]
    private async Task<MailRuleEvaluation> EvaluateRuleAsync(
        MailRule rule,
        MailRuleFacts facts,
        MailRuleConditionBounds bounds,
        CancellationToken cancellationToken)
    {
        using var evaluationTimeout = new CancellationTokenSource(bounds.EvaluationTimeout, this.timeProvider);
        using var boundedEvaluation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            evaluationTimeout.Token);

        // Whatever the rule above this one read is taken and discarded first, so what this rule is credited with is what
        // this rule reached. The fact surface is shared across the set for its cache, which is the whole reason the reads
        // have to be divided here rather than counted from it afterwards.
        _ = facts.TakeFactsRead();

        var startedAt = this.timeProvider.GetTimestamp();

        try
        {
            var matched = await rule.Condition.EvaluateAsync(facts, boundedEvaluation.Token);
            var elapsed = this.timeProvider.GetElapsedTime(startedAt);
            var readFacts = facts.TakeFactsRead();

            return matched
                ? MailRuleEvaluation.Matched(rule.Name, readFacts, elapsed)
                : MailRuleEvaluation.NotMatched(rule.Name, readFacts, elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The caller withdrew the whole pass, which is not something a rule did and not something to record as one.
            throw;
        }
        catch (OperationCanceledException)
        {
            return MailRuleEvaluation.Failed(
                rule.Name,
                MailRuleConditionFailure.EvaluationTimedOut,
                facts.TakeFactsRead(),
                this.timeProvider.GetElapsedTime(startedAt));
        }
        catch (Exception)
        {
            // The failure itself is deliberately not carried out of here. An expression evaluator's message quotes the
            // operands it could not work with, and an operand is mail content; the reason an operator needs is which
            // rule stopped answering, which the classification already states. The facts it reached before it stopped
            // are recorded, because a condition that fails on one of them is exactly the case an operator debugs.
            return MailRuleEvaluation.Failed(
                rule.Name,
                MailRuleConditionFailure.EvaluationFaulted,
                facts.TakeFactsRead(),
                this.timeProvider.GetElapsedTime(startedAt));
        }
    }
}
