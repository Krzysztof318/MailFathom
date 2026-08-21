// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Rules;

/// <summary>What a deployment is asked when a whole-mailbox rule run is wanted.</summary>
/// <param name="Account">The account to run the rules over, as the deployment's configuration names it.</param>
internal sealed record MailRuleRunRequest(
    [property: JsonPropertyName("account")] string Account);

/// <summary>What asking for a run did.</summary>
/// <param name="Started">Whether this request is what put the run in front of the account.</param>
/// <param name="Run">The run the account now has outstanding, which is the one already going when nothing started.</param>
/// <remarks>
/// Asking twice is asking once, so a request that started nothing is an answer rather than a refusal: the mail is going
/// to be re-evaluated either way, and what this reports is whether the walk began now or was already under way.
/// </remarks>
internal sealed record MailRuleRunStart(
    [property: JsonPropertyName("started")] bool Started,
    [property: JsonPropertyName("run")] MailRuleRun? Run);

/// <summary>Where an account's run stands.</summary>
/// <param name="Account">The account the answer is about.</param>
/// <param name="Run">The run, or <see langword="null" /> where the account has never been asked for one.</param>
internal sealed record MailRuleRunState(
    [property: JsonPropertyName("account")] string? Account,
    [property: JsonPropertyName("run")] MailRuleRun? Run);

/// <summary>One whole-mailbox rule run, and how far the account's synchronization runs have carried it.</summary>
/// <param name="RequestedAt">When the run was asked for.</param>
/// <param name="Trigger">What started the run, which is what says whether anybody asked for it.</param>
/// <param name="Revision">The rule set the run is bound to, absent until the first pass picks it up.</param>
/// <param name="EvaluatedEmailCount">How many of the account's emails the run has evaluated.</param>
/// <param name="MatchedEmailCount">How many of those at least one rule matched.</param>
/// <param name="SkippedEmailCount">How many were stepped over because their body text had not been extracted yet.</param>
/// <param name="EndedAt">When the run stopped being outstanding, absent while it still is.</param>
/// <param name="Ending">How it ended, absent for exactly as long as <paramref name="EndedAt" /> is.</param>
internal sealed record MailRuleRun(
    [property: JsonPropertyName("requestedAt")] DateTimeOffset RequestedAt,
    [property: JsonPropertyName("trigger")] string? Trigger,
    [property: JsonPropertyName("revision")] string? Revision,
    [property: JsonPropertyName("evaluatedEmailCount")] int EvaluatedEmailCount,
    [property: JsonPropertyName("matchedEmailCount")] int MatchedEmailCount,
    [property: JsonPropertyName("skippedEmailCount")] int SkippedEmailCount,
    [property: JsonPropertyName("endedAt")] DateTimeOffset? EndedAt,
    [property: JsonPropertyName("ending")] string? Ending)
{
    /// <summary>Describes where the run stands in one line an operator reads.</summary>
    /// <returns>Whether it is still going or how it ended, and when.</returns>
    /// <remarks>
    /// A run in flight reports no estimate of what is left. The pass takes as much of the mailbox as one account run's
    /// budget reaches and the rest waits for the next run, so how far there is to go depends on how often that account
    /// synchronizes — a figure invented here would be a prediction rather than a reading.
    /// </remarks>
    internal string DescribeState() => this switch
    {
        { EndedAt: null } when this.Trigger is { Length: > 0 } trigger => $"under way, started by {trigger}",
        { EndedAt: { } endedAt, Ending: { Length: > 0 } ending } =>
            $"{ending} at {endedAt.ToString("u", CultureInfo.InvariantCulture)}",
        { EndedAt: { } endedAt } => $"ended at {endedAt.ToString("u", CultureInfo.InvariantCulture)}",
        _ => "under way",
    };

    /// <summary>Describes what the run has read so far, in counts and nothing derived from a message.</summary>
    /// <returns>The three counts, grouped invariantly for the reason every other figure this tool prints is.</returns>
    internal string DescribeProgress() => string.Create(
        CultureInfo.InvariantCulture,
        $"{this.EvaluatedEmailCount} evaluated, {this.MatchedEmailCount} matched, {this.SkippedEmailCount} skipped");
}
