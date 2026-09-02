// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Actions;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;

namespace MailFathom.Host.Api;

/// <summary>The rule set in force, as the administrative endpoint serves it.</summary>
/// <param name="Revision">The derived identity of the set, which every recorded execution names.</param>
/// <param name="ConfigurationAccepted">Whether the configuration as it now stands is the one this set was read from.</param>
/// <param name="RefusedSettingCount">How many settings a refusal named, which is zero whenever the set is current.</param>
/// <param name="Rules">The rules, in the order they are evaluated in.</param>
/// <remarks>
/// The order is the answer as much as the rules are, so nothing here sorts: a caller reading this is finding out which
/// rule reaches a message first and whether anything above it ends the pass.
/// </remarks>
internal sealed record MailRuleSetResponse(
    string Revision,
    bool ConfigurationAccepted,
    int RefusedSettingCount,
    IReadOnlyList<MailRuleResponse> Rules)
{
    /// <summary>Describes the loaded set for the wire.</summary>
    /// <param name="ruleSet">The set the pass runs against.</param>
    /// <param name="latestReloadRefused">Whether the most recent candidate was refused.</param>
    /// <param name="refusedSettingCount">How many settings that refusal named.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ruleSet" /> is <see langword="null" />.</exception>
    internal static MailRuleSetResponse For(
        MailRuleSet ruleSet,
        bool latestReloadRefused,
        int refusedSettingCount)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);

        return new MailRuleSetResponse(
            ruleSet.Revision.Value,
            !latestReloadRefused,
            refusedSettingCount,
            [.. ruleSet.Rules.Select(MailRuleResponse.For)]);
    }
}

/// <summary>One loaded rule, as the administrative endpoint serves it.</summary>
/// <param name="Name">The name the rule is declared and reported under.</param>
/// <param name="Accounts">The accounts it applies to, empty for a rule that applies to every account.</param>
/// <param name="ReadableFacts">The facts its condition names, which is every fact it can cause to be resolved.</param>
/// <param name="Actions">What a match asks the mailbox for, in the order the rule declares the changes.</param>
/// <param name="StopWhenMatched">Whether a match ends the pass rather than continuing to the rules below.</param>
/// <param name="Triggers">The automatic triggers it takes part in, empty for a rule only a requested run applies.</param>
/// <param name="Schedule">The occasions the rule declares, in their canonical form, absent for a rule declaring none.</param>
/// <remarks>
/// The authored condition is deliberately absent. A compiled rule carries no text — which is what keeps an address the
/// operator typed into a condition out of every record naming the rule — so what this reports of a condition is the
/// facts it reaches. The text itself is in the operator's own configuration file, where it was written.
/// </remarks>
internal sealed record MailRuleResponse(
    string Name,
    IReadOnlyList<string> Accounts,
    IReadOnlyList<string> ReadableFacts,
    IReadOnlyList<MailRuleActionResponse> Actions,
    bool StopWhenMatched,
    IReadOnlyList<string> Triggers,
    string? Schedule)
{
    /// <summary>Describes one rule for the wire.</summary>
    /// <param name="rule">The bound rule.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule" /> is <see langword="null" />.</exception>
    internal static MailRuleResponse For(MailRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return new MailRuleResponse(
            rule.Name,
            [.. rule.Accounts.Order(StringComparer.Ordinal)],
            [.. rule.Condition.ReferencedFacts.Select(fact => fact.Name)],
            [.. rule.Actions.Actions.Select((action, position) => MailRuleActionResponse.For(action, position))],
            rule.StopWhenMatched,
            [.. MailRuleTrigger.All.Where(rule.RunsOn).Select(trigger => trigger.Name)],
            rule.Schedule?.CanonicalForm);
    }
}

/// <summary>One change a rule declares, as the administrative endpoint serves it.</summary>
/// <param name="Position">Where the change sits in the order the rule declares its changes, counted from zero.</param>
/// <param name="Mutation">The change, named as the mutation it will be requested through.</param>
/// <param name="Destination">
/// How a relocation or a copy names its folder, absent for every other change. It is the text the rule wrote — an alias,
/// or a role as <c>role:Junk</c> — rather than the folder it resolves to, because what this serves is the declaration.
/// </param>
/// <param name="DesiredSeenState">Which way a <c>\Seen</c> change was asked for, absent for every other change.</param>
internal sealed record MailRuleActionResponse(
    int Position,
    string Mutation,
    string? Destination,
    bool? DesiredSeenState)
{
    /// <summary>Describes one declared change for the wire.</summary>
    /// <param name="action">The change the rule declares.</param>
    /// <param name="position">Where it sits in the rule's declared order.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
    internal static MailRuleActionResponse For(MailRuleAction action, int position)
    {
        ArgumentNullException.ThrowIfNull(action);

        return new MailRuleActionResponse(
            position,
            action.Mutation.Name,
            action.Destination?.ToString(),
            action.DesiredSeenState);
    }
}

/// <summary>What a caller asks a whole-mailbox rule run for.</summary>
/// <param name="Account">The account to run the rules over, as this deployment's configuration names it.</param>
internal sealed record MailRuleRunRequest(string? Account);

/// <summary>What asking for a run did, as the administrative endpoint serves it.</summary>
/// <param name="Started">Whether this request is what put the run in front of the account.</param>
/// <param name="Run">The run the account now has outstanding, which is the one already under way when nothing started.</param>
/// <remarks>
/// The two are reported together because a caller that asked twice needs both: the run is the answer either way, and
/// whether this request started it is what tells "I have just begun a pass" from "one was already going".
/// </remarks>
internal sealed record MailRuleRunStartResponse(bool Started, MailRuleRunResponse Run);

/// <summary>Where an account's run stands, as the administrative endpoint serves it.</summary>
/// <param name="Account">The account the answer is about.</param>
/// <param name="Run">The run, or <see langword="null" /> where the account has never been asked for one.</param>
internal sealed record MailRuleRunStateResponse(string Account, MailRuleRunResponse? Run);

/// <summary>One whole-mailbox rule run, as the administrative endpoint serves it.</summary>
/// <param name="RequestedAt">When the run was asked for.</param>
/// <param name="Trigger">What started the run, which is what says whether anybody asked for it.</param>
/// <param name="Revision">The rule set the run is bound to, absent until the first pass picks it up.</param>
/// <param name="EvaluatedEmailCount">How many of the account's emails the run has evaluated.</param>
/// <param name="MatchedEmailCount">How many of those at least one rule matched.</param>
/// <param name="SkippedEmailCount">How many were stepped over because their body text had not been extracted yet.</param>
/// <param name="EndedAt">When the run stopped being outstanding, absent while it still is.</param>
/// <param name="Ending">How it ended, absent for exactly as long as <paramref name="EndedAt" /> is.</param>
/// <remarks>
/// Counts and instants, and no message is named. The progress a caller watches is how much of the mailbox has been
/// read, which the counts state without saying anything about what was in it.
/// </remarks>
internal sealed record MailRuleRunResponse(
    DateTimeOffset RequestedAt,
    string Trigger,
    string? Revision,
    int EvaluatedEmailCount,
    int MatchedEmailCount,
    int SkippedEmailCount,
    DateTimeOffset? EndedAt,
    string? Ending)
{
    /// <summary>Describes one run for the wire.</summary>
    /// <param name="run">The run as it stands.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="run" /> is <see langword="null" />.</exception>
    internal static MailRuleRunResponse For(MailRuleEvaluationRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new MailRuleRunResponse(
            run.RequestedAt,
            run.Trigger.ToString(),
            run.Revision.IsSpecified ? run.Revision.Value : null,
            run.EvaluatedEmailCount,
            run.MatchedEmailCount,
            run.SkippedEmailCount,
            run.EndedAt,
            run.Ending?.ToString());
    }
}

/// <summary>One page of an account's rule history, as the administrative endpoint serves it.</summary>
/// <param name="Executions">The executions, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the history.</param>
internal sealed record MailRuleHistoryPageResponse(
    IReadOnlyList<MailRuleExecutionResponse> Executions,
    string? NextCursor)
{
    /// <summary>Describes one page for the wire.</summary>
    /// <param name="page">The page read from the history.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    internal static MailRuleHistoryPageResponse For(MailRuleExecutionPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new MailRuleHistoryPageResponse(
            [.. page.Executions.Select(MailRuleExecutionResponse.For)],
            page.NextCursor?.Encode());
    }
}

/// <summary>One recorded rule execution, as the administrative endpoint serves it.</summary>
/// <param name="Id">What addresses this execution.</param>
/// <param name="Email">The stable local identity of the message, which is the same one every other read names it by.</param>
/// <param name="Rule">The rule, which is MailFathom's own configured name for it.</param>
/// <param name="Revision">The rule set revision the pass was bound to, which the condition is retrievable from.</param>
/// <param name="Trigger">Which of the pass's two walks reached the message.</param>
/// <param name="Outcome">What the condition concluded.</param>
/// <param name="ConditionFailure">Why it produced no answer, absent for every execution that produced one.</param>
/// <param name="ReadFacts">The facts the condition read, by name.</param>
/// <param name="Actions">The changes the rule declared and what became of each.</param>
/// <param name="EvaluatedAt">When the message was evaluated.</param>
/// <param name="Duration">How long the condition took to answer.</param>
/// <remarks>
/// <strong>The facts are names and never values.</strong> What the condition compared is retrievable from the recorded
/// revision, which identifies the configuration the expression was read from — so the reasoning is reconstructible
/// without a sender address, a subject, or a span of extracted text ever leaving the mailbox.
/// </remarks>
internal sealed record MailRuleExecutionResponse(
    Guid Id,
    Guid Email,
    string Rule,
    string Revision,
    string Trigger,
    string Outcome,
    string? ConditionFailure,
    IReadOnlyList<string> ReadFacts,
    IReadOnlyList<MailRuleExecutedActionResponse> Actions,
    DateTimeOffset EvaluatedAt,
    TimeSpan Duration)
{
    /// <summary>Describes one execution for the wire.</summary>
    /// <param name="execution">The execution read from the history.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="execution" /> is <see langword="null" />.</exception>
    internal static MailRuleExecutionResponse For(MailRuleExecution execution)
    {
        ArgumentNullException.ThrowIfNull(execution);

        return new MailRuleExecutionResponse(
            execution.Id.Value,
            execution.StoredEmailId.Value,
            execution.RuleName,
            execution.Revision.Value,
            execution.Trigger.ToString(),
            execution.Outcome.ToString(),
            execution.ConditionFailure?.ToString(),
            [.. execution.ReadFacts.Select(fact => fact.Name)],
            [.. execution.Actions.Select(MailRuleExecutedActionResponse.For)],
            execution.EvaluatedAt,
            execution.Duration);
    }
}

/// <summary>One change a matching rule declared, as the administrative endpoint serves it.</summary>
/// <param name="Position">Where the change sits in the order the rule declares its changes, counted from zero.</param>
/// <param name="Mutation">The change asked for.</param>
/// <param name="Outcome">What became of it.</param>
/// <param name="Destination">The folder the change named, absent for a change naming none.</param>
/// <param name="FailureReason">Why nothing was recorded, present exactly for a change the recorder refused.</param>
/// <param name="MutationRecord">The record carrying the request, present exactly for a change that opened one.</param>
/// <remarks>
/// The record identifier is where what happened on the server is answered from. This says what the rule asked for and
/// nothing about how the mailbox took it, which is the mutation trail's to state and is not restated here.
/// </remarks>
internal sealed record MailRuleExecutedActionResponse(
    int Position,
    string Mutation,
    string Outcome,
    string? Destination,
    string? FailureReason,
    Guid? MutationRecord)
{
    /// <summary>Describes one recorded change for the wire.</summary>
    /// <param name="action">The change the execution recorded.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="action" /> is <see langword="null" />.</exception>
    internal static MailRuleExecutedActionResponse For(MailRuleExecutedAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        return new MailRuleExecutedActionResponse(
            action.Position,
            action.Mutation.Name,
            action.Outcome.ToString(),
            action.Destination,
            action.FailureReason?.ToString(),
            action.MutationRecordId?.Value);
    }
}
