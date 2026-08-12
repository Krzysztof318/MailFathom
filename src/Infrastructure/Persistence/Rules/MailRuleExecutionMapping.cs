// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Conditions;
using MailFathom.Application.Rules.Facts;
using MailFathom.Application.Rules.History;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Rules;

/// <summary>Turns one recorded rule execution into the rows that keep it, and back.</summary>
internal static class MailRuleExecutionMapping
{
    /// <summary>Builds the rows one execution is kept as.</summary>
    /// <param name="execution">The execution to keep.</param>
    /// <returns>The row to append, with the actions it recorded already attached.</returns>
    internal static MailRuleExecutionEntity ToEntity(MailRuleExecution execution)
    {
        var entity = new MailRuleExecutionEntity
        {
            Id = execution.Id.Value,
            MailboxAccountId = execution.AccountId.Value,
            StoredEmailId = execution.StoredEmailId.Value,
            RuleName = execution.RuleName,
            Revision = execution.Revision.Value,
            Trigger = execution.Trigger.ToString(),
            Outcome = execution.Outcome.ToString(),
            ConditionFailure = execution.ConditionFailure?.ToString(),
            ReadFacts = [.. execution.ReadFacts.Select(fact => fact.Name)],
            EvaluatedAt = execution.EvaluatedAt,
            Duration = execution.Duration,
        };

        foreach (var action in execution.Actions)
        {
            entity.Actions.Add(new MailRuleExecutedActionEntity
            {
                MailRuleExecutionId = entity.Id,
                Position = action.Position,
                Mutation = action.Mutation.Name,
                Outcome = action.Outcome.ToString(),
                DestinationAlias = action.DestinationAlias?.Value,
                FailureReason = action.FailureReason?.ToString(),
                MutationRecordId = action.MutationRecordId?.Value,
            });
        }

        return entity;
    }

    /// <summary>Rebuilds the execution one stored row states, or reports a row this build cannot interpret.</summary>
    /// <param name="entity">The stored row, with its actions loaded.</param>
    /// <param name="execution">The execution that row states, when this build can read it.</param>
    /// <returns><see langword="true" /> when the row was rebuilt; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// <para>
    /// A row is refused rather than approximated when it names an outcome, a trigger, a failure, a mutation, or a fact
    /// this build does not recognize — which is version skew rather than corruption: a later build that declares a
    /// further fact or a further way an action can fail writes rows this one has no value for, and a rollback then reads
    /// them.
    /// </para>
    /// <para>
    /// It is reported rather than thrown because the history is read a page at a time and paginated by position, so one
    /// unreadable row thrown out of the mapping would fail the whole page and every page after it. The caller leaves the
    /// row out, says so, and walks on.
    /// </para>
    /// </remarks>
    internal static bool TryToExecution(
        MailRuleExecutionEntity entity,
        [NotNullWhen(true)] out MailRuleExecution? execution)
    {
        execution = null;

        if (!TryToDefinedEnum<MailRuleExecutionTrigger>(entity.Trigger, out var trigger)
            || !TryToDefinedEnum<MailRuleOutcome>(entity.Outcome, out var outcome)
            || !TryToConditionFailure(entity.ConditionFailure, out var conditionFailure)
            || !TryToFacts(entity.ReadFacts, out var readFacts)
            || !TryToActions(entity.Actions, out var actions))
        {
            return false;
        }

        execution = new MailRuleExecution
        {
            Id = MailRuleExecutionId.Create(entity.Id),
            AccountId = MailAccountId.Create(entity.MailboxAccountId),
            StoredEmailId = StoredEmailId.Create(entity.StoredEmailId),
            RuleName = entity.RuleName,
            Revision = MailRuleSetRevision.Restore(entity.Revision),
            Trigger = trigger,
            Outcome = outcome,
            ConditionFailure = conditionFailure,
            ReadFacts = readFacts,
            Actions = actions,
            EvaluatedAt = entity.EvaluatedAt,
            Duration = entity.Duration,
        };

        return true;
    }

    /// <summary>Reads back a bounded value a stored row names, refusing one this build declares no member for.</summary>
    /// <remarks>
    /// The definition check is what makes this more than a parse: the stored text is a member name, but the parser also
    /// accepts a number, and a value nothing declares would otherwise be published as one no reader could interpret.
    /// </remarks>
    private static bool TryToDefinedEnum<TEnum>(string stored, out TEnum value)
        where TEnum : struct, Enum =>
        Enum.TryParse(stored, out value) && Enum.IsDefined(value);

    /// <summary>Reads back the condition failure a row names, where it names one at all.</summary>
    /// <remarks>
    /// Absence is a value here rather than a missing one: an execution that produced an answer has no failure to name,
    /// and refusing the row for that would refuse every execution that worked.
    /// </remarks>
    private static bool TryToConditionFailure(string? stored, out MailRuleConditionFailure? failure)
    {
        failure = null;

        if (stored is null)
        {
            return true;
        }

        if (!TryToDefinedEnum<MailRuleConditionFailure>(stored, out var parsed))
        {
            return false;
        }

        failure = parsed;

        return true;
    }

    /// <summary>Reads back the facts a row names, refusing one this build's fact surface does not declare.</summary>
    private static bool TryToFacts(string[] stored, out IReadOnlyList<MailRuleFact> facts)
    {
        var read = new List<MailRuleFact>(stored.Length);

        foreach (var name in stored)
        {
            if (!MailRuleFact.TryParseName(name, out var fact))
            {
                facts = [];

                return false;
            }

            read.Add(fact);
        }

        facts = read;

        return true;
    }

    /// <summary>Reads back what became of each declared change, in the order the rule declares them.</summary>
    private static bool TryToActions(
        IEnumerable<MailRuleExecutedActionEntity> stored,
        out IReadOnlyList<MailRuleExecutedAction> actions)
    {
        var read = new List<MailRuleExecutedAction>();

        foreach (var entity in stored.OrderBy(action => action.Position))
        {
            if (!TryToAction(entity, out var action))
            {
                actions = [];

                return false;
            }

            read.Add(action);
        }

        actions = read;

        return true;
    }

    private static bool TryToAction(
        MailRuleExecutedActionEntity entity,
        [NotNullWhen(true)] out MailRuleExecutedAction? action)
    {
        action = null;

        if (!MailboxMutation.TryParseName(entity.Mutation, out var mutation)
            || !TryToDefinedEnum<MailRuleExecutedActionOutcome>(entity.Outcome, out var outcome)
            || !TryToFailureReason(entity.FailureReason, out var failureReason))
        {
            return false;
        }

        action = new MailRuleExecutedAction(
            entity.Position,
            mutation,
            outcome,
            entity.DestinationAlias is { } alias ? MailFolderAlias.Create(alias) : null,
            failureReason,
            entity.MutationRecordId is { } recordId ? MailboxMutationRecordId.Create(recordId) : null);

        return true;
    }

    private static bool TryToFailureReason(string? stored, out MailRuleActionFailureReason? reason)
    {
        reason = null;

        if (stored is null)
        {
            return true;
        }

        if (!TryToDefinedEnum<MailRuleActionFailureReason>(stored, out var parsed))
        {
            return false;
        }

        reason = parsed;

        return true;
    }
}
