// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.Actions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Rules.History;

/// <summary>Turns what one email's evaluation concluded, and what recording its actions produced, into the history.</summary>
/// <remarks>
/// <para>
/// One execution per rule the pass reached, which is what makes the two silences different: a rule below one that ended
/// the pass produced no evaluation and therefore leaves no row, while a rule that was asked and said no leaves a row
/// saying so. An operator asking why a rule never fires can then tell "it is never reached" from "it is never true".
/// </para>
/// <para>
/// Composing here rather than in the pass keeps the join between a plan and its recording in one place. The plan states
/// what each matching rule asked for and which of those another rule had already settled; the recording states which of
/// the rest reached a mutation record and which were refused by something that had stopped being true. Neither half is
/// the answer on its own, and an action must appear exactly once whichever half it fell into.
/// </para>
/// </remarks>
public static class MailRuleExecutionComposer
{
    /// <summary>Composes the executions one evaluated email leaves behind.</summary>
    /// <param name="accountId">The account whose mail was evaluated.</param>
    /// <param name="storedEmailId">The local identity of the email.</param>
    /// <param name="evaluation">What the rule set concluded, under the revision it concluded it.</param>
    /// <param name="trigger">Which of the pass's two walks reached the email.</param>
    /// <param name="recording">What opening mutation records for the plan produced.</param>
    /// <param name="evaluatedAt">The instant the email was evaluated at.</param>
    /// <returns>One execution per rule the pass reached, in the order the rules were reached.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static IReadOnlyList<MailRuleExecution> Compose(
        MailAccountId accountId,
        StoredEmailId storedEmailId,
        MailRuleSetEvaluation evaluation,
        MailRuleExecutionTrigger trigger,
        MailRuleActionRecording recording,
        DateTimeOffset evaluatedAt)
    {
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(recording);

        return
        [
            .. evaluation.Evaluations.Select(ruleEvaluation => new MailRuleExecution
            {
                Id = MailRuleExecutionId.New(),
                AccountId = accountId,
                StoredEmailId = storedEmailId,
                RuleName = ruleEvaluation.RuleName,
                Revision = evaluation.Revision,
                Trigger = trigger,
                Outcome = ruleEvaluation.Outcome,
                ConditionFailure = ruleEvaluation.Failure,
                ReadFacts = ruleEvaluation.ReadFacts,
                Actions = ActionsOf(ruleEvaluation.RuleName, evaluation.ActionPlan, recording),
                EvaluatedAt = evaluatedAt,
                Duration = ruleEvaluation.Duration,
            }),
        ];
    }

    /// <summary>Collects what became of every change one rule declared, in the order that rule declares them.</summary>
    /// <remarks>
    /// Built from the three lists that already divide the actions rather than by looking each planned action up in them.
    /// The recorder puts every action it was handed into exactly one of the first two, and the plan holds the rest, so
    /// composing this way makes "appears exactly once" a property of the three sources instead of a lookup whose miss
    /// would have to be given an outcome nothing produced.
    /// </remarks>
    private static IReadOnlyList<MailRuleExecutedAction> ActionsOf(
        string ruleName,
        MailRuleActionPlan plan,
        MailRuleActionRecording recording) =>
    [
        .. recording.Recorded
            .Where(recorded => StringComparer.Ordinal.Equals(recorded.RuleName, ruleName))
            .Select(recorded => new MailRuleExecutedAction(
                recorded.Position,
                recorded.Mutation,
                MailRuleExecutedActionOutcome.Requested,
                recorded.DestinationAlias?.Value,
                MutationRecordId: recorded.RecordId))

            // The two below name what the rule wrote rather than an alias, because neither reached a folder: a role
            // that resolved to none has no alias, and reporting nothing would leave the operator unable to tell which
            // of a rule's destinations they have to correct.
            .Concat(recording.Failures
                .Where(refused => StringComparer.Ordinal.Equals(refused.RuleName, ruleName))
                .Select(refused => new MailRuleExecutedAction(
                    refused.Position,
                    refused.Mutation,
                    MailRuleExecutedActionOutcome.Refused,
                    refused.Destination?.ToString(),
                    refused.Reason)))
            .Concat(plan.WithheldActions
                .Where(withheld => StringComparer.Ordinal.Equals(withheld.RuleName, ruleName))
                .Select(withheld => new MailRuleExecutedAction(
                    withheld.Position,
                    withheld.Action.Mutation,
                    MailRuleExecutedActionOutcome.Withheld,
                    withheld.Action.Destination?.ToString())))
            .OrderBy(action => action.Position),
    ];
}
