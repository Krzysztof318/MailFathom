// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;
using MailFathom.Cli.Commands;

namespace MailFathom.Cli.Administration.Rules;

/// <summary>What a deployment reports about the mail rules it is running.</summary>
/// <remarks>
/// The set is what the deployment compiled from its configuration, which is not necessarily what the configuration now
/// says: a reload whose rules did not validate is refused and leaves the previous set in force. That is why the
/// acceptance travels with the revision — together they answer "did my edit take effect", which neither answers alone.
/// </remarks>
/// <param name="Revision">The derived identity of the set in force, which every recorded execution names.</param>
/// <param name="ConfigurationAccepted">Whether the configuration as it now stands is the one this set was read from.</param>
/// <param name="RefusedSettingCount">How many settings a refusal named, which is zero whenever the set is current.</param>
/// <param name="Rules">The rules, in the order they are evaluated in.</param>
internal sealed record LoadedRuleSet(
    [property: JsonPropertyName("revision")] string? Revision,
    [property: JsonPropertyName("configurationAccepted")] bool ConfigurationAccepted,
    [property: JsonPropertyName("refusedSettingCount")] int RefusedSettingCount,
    [property: JsonPropertyName("rules")] IReadOnlyList<LoadedRule>? Rules);

/// <summary>One rule a deployment has loaded.</summary>
/// <remarks>
/// The condition an operator wrote is deliberately not here: a compiled rule carries no text, so what a deployment can
/// report of a condition is the facts it reaches. The text is in the operator's own file, where they wrote it.
/// </remarks>
/// <param name="Name">The name the rule is declared and reported under.</param>
/// <param name="Accounts">The accounts it applies to, empty for a rule that applies to every account.</param>
/// <param name="ReadableFacts">The facts its condition names, which is every fact it can cause to be resolved.</param>
/// <param name="Actions">What a match asks the mailbox for, in the order the rule declares the changes.</param>
/// <param name="StopWhenMatched">Whether a match ends the pass rather than continuing to the rules below.</param>
/// <param name="Triggers">The automatic triggers it takes part in, empty for a rule only a requested run applies.</param>
/// <param name="Schedule">The occasions it declares, absent for a rule declaring none.</param>
internal sealed record LoadedRule(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("accounts")] IReadOnlyList<string>? Accounts,
    [property: JsonPropertyName("readableFacts")] IReadOnlyList<string>? ReadableFacts,
    [property: JsonPropertyName("actions")] IReadOnlyList<LoadedRuleAction>? Actions,
    [property: JsonPropertyName("stopWhenMatched")] bool StopWhenMatched,
    [property: JsonPropertyName("triggers")] IReadOnlyList<string>? Triggers,
    [property: JsonPropertyName("schedule")] string? Schedule)
{
    /// <summary>Describes what the rule applies to in one line an operator reads.</summary>
    /// <returns>The accounts it names, or that it names none.</returns>
    internal string DescribeScope() => this.Accounts is { Count: > 0 } accounts
        ? string.Join(", ", accounts)
        : "every account";

    /// <summary>Describes what runs the rule in one line an operator reads.</summary>
    /// <returns>The triggers it takes part in, or that a requested run is the only thing that applies it.</returns>
    /// <remarks>
    /// A rule nothing fires by itself is the one an operator is most likely to be asking about — it looks identical to
    /// a rule that never matched — so the answer says what does run it rather than reporting an empty list.
    /// </remarks>
    internal string DescribeTriggers() => (this.Triggers, this.Schedule) switch
    {
        ({ Count: > 0 } triggers, { Length: > 0 } schedule) => $"{string.Join(", ", triggers)} ({schedule})",
        ({ Count: > 0 } triggers, _) => string.Join(", ", triggers),
        _ => $"nothing automatically; '{CliRootCommand.CommandName} rules run' applies it",
    };

    /// <summary>Describes what a match does in one line an operator reads.</summary>
    /// <returns>The changes in declared order, and the fact that a match ends the pass where it does.</returns>
    internal string DescribeActions()
    {
        var changes = this.Actions is { Count: > 0 } actions
            ? string.Join(", ", actions.Select(action => action.Describe()))
            : "nothing";

        return this.StopWhenMatched ? $"{changes}; ends the pass" : changes;
    }
}

/// <summary>One change a rule declares.</summary>
/// <param name="Position">Where the change sits in the order the rule declares its changes, counted from zero.</param>
/// <param name="Mutation">The change, named as the mutation it will be requested through.</param>
/// <param name="Destination">How a relocation or a copy names its folder — an alias, or a role as <c>role:Junk</c> — absent for every other change.</param>
/// <param name="DesiredSeenState">Which way a <c>\Seen</c> change was asked for, absent for every other change.</param>
internal sealed record LoadedRuleAction(
    [property: JsonPropertyName("position")] int Position,
    [property: JsonPropertyName("mutation")] string? Mutation,
    [property: JsonPropertyName("destination")] string? Destination,
    [property: JsonPropertyName("desiredSeenState")] bool? DesiredSeenState)
{
    /// <summary>Describes the change and the parameter it carries, where it carries one.</summary>
    /// <returns>One phrase, such as <c>Relocate → archive</c>.</returns>
    internal string Describe() => this switch
    {
        { Destination: { Length: > 0 } destination } => $"{this.Mutation} → {destination}",
        { DesiredSeenState: { } isSeen } => $"{this.Mutation} → {(isSeen ? "read" : "unread")}",
        _ => this.Mutation ?? "an unreported change",
    };
}
