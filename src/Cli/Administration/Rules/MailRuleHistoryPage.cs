// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Rules;

/// <summary>One page of what a deployment's rules concluded about the mail they were run over.</summary>
/// <param name="Executions">The executions, newest first.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or <see langword="null" /> at the end of the history.</param>
/// <remarks>
/// The absent cursor is the end of the walk rather than a page that happened to be short, so the command stops when the
/// cursor stops instead of comparing the count against the size it asked for.
/// </remarks>
internal sealed record MailRuleHistoryPage(
    [property: JsonPropertyName("executions")] IReadOnlyList<MailRuleExecution>? Executions,
    [property: JsonPropertyName("nextCursor")] string? NextCursor);

/// <summary>What one rule concluded about one message on one pass.</summary>
/// <remarks>
/// The facts are names and never values. What the condition compared is retrievable from the revision recorded beside
/// them, which identifies the configuration the expression was read from — so the reasoning is reconstructible without
/// the message being copied anywhere.
/// </remarks>
/// <param name="Id">What addresses this execution.</param>
/// <param name="Email">The stable local identity of the message.</param>
/// <param name="Rule">The rule, as the deployment's configuration names it.</param>
/// <param name="Revision">The rule set revision the pass was bound to.</param>
/// <param name="Trigger">Which of the pass's two walks reached the message.</param>
/// <param name="Outcome">What the condition concluded.</param>
/// <param name="ConditionFailure">Why it produced no answer, absent for every execution that produced one.</param>
/// <param name="ReadFacts">The facts the condition read, by name.</param>
/// <param name="Actions">The changes the rule declared and what became of each.</param>
/// <param name="EvaluatedAt">When the message was evaluated.</param>
/// <param name="Duration">How long the condition took to answer.</param>
internal sealed record MailRuleExecution(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("email")] Guid Email,
    [property: JsonPropertyName("rule")] string? Rule,
    [property: JsonPropertyName("revision")] string? Revision,
    [property: JsonPropertyName("trigger")] string? Trigger,
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("conditionFailure")] string? ConditionFailure,
    [property: JsonPropertyName("readFacts")] IReadOnlyList<string>? ReadFacts,
    [property: JsonPropertyName("actions")] IReadOnlyList<MailRuleExecutedAction>? Actions,
    [property: JsonPropertyName("evaluatedAt")] DateTimeOffset EvaluatedAt,
    [property: JsonPropertyName("duration")] TimeSpan Duration)
{
    /// <summary>Describes what the rule concluded, and why it concluded nothing where it did.</summary>
    /// <returns>The outcome, with the failure's reason appended where there is one.</returns>
    internal string DescribeOutcome() => this.ConditionFailure is { Length: > 0 } failure
        ? $"{this.Outcome} ({failure})"
        : this.Outcome ?? "an unreported outcome";

    /// <summary>Describes the facts the condition read, by name.</summary>
    /// <returns>The names, or that the condition compared none.</returns>
    internal string DescribeReadFacts() => this.ReadFacts is { Count: > 0 } facts
        ? string.Join(", ", facts)
        : "none";

    /// <summary>Describes what the rule asked the mailbox for and what became of it.</summary>
    /// <returns>One phrase per change, or that the rule asked for nothing.</returns>
    internal string DescribeActions() => this.Actions is { Count: > 0 } actions
        ? string.Join("; ", actions.Select(action => action.Describe()))
        : "no change";

    /// <summary>Describes how long the condition took, in milliseconds.</summary>
    /// <returns>The figure, grouped invariantly for the reason every other figure this tool prints is.</returns>
    internal string DescribeDuration() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.Duration.TotalMilliseconds:F1} ms");
}

/// <summary>One change a matching rule declared, and what became of it.</summary>
/// <param name="Position">Where the change sits in the order the rule declares its changes, counted from zero.</param>
/// <param name="Mutation">The change asked for.</param>
/// <param name="Outcome">What became of it.</param>
/// <param name="DestinationAlias">The folder the change named, absent for a change naming none.</param>
/// <param name="FailureReason">Why nothing was recorded, present exactly for a change the deployment refused.</param>
/// <param name="MutationRecord">The record carrying the request, present exactly for a change that opened one.</param>
internal sealed record MailRuleExecutedAction(
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("mutation")] string? Mutation,
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("destinationAlias")] string? DestinationAlias,
    [property: JsonPropertyName("failureReason")] string? FailureReason,
    [property: JsonPropertyName("mutationRecord")] Guid? MutationRecord)
{
    /// <summary>Describes the change, where it was aimed, and what became of it.</summary>
    /// <returns>One phrase, such as <c>Relocate → archive: Requested</c>.</returns>
    /// <remarks>
    /// The mutation record is not printed. It is the identity of the change on the server rather than anything an
    /// operator reads at a glance, and what became of it there is the mutation trail's answer rather than this one's.
    /// </remarks>
    internal string Describe()
    {
        var target = this.DestinationAlias is { Length: > 0 } alias ? $" → {alias}" : string.Empty;
        var reason = this.FailureReason is { Length: > 0 } failure ? $" ({failure})" : string.Empty;

        return $"{this.Mutation}{target}: {this.Outcome}{reason}";
    }
}
