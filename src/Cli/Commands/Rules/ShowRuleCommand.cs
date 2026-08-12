// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Rules;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Reports one loaded rule in full: what it applies to, what it reads, and what a match does.</summary>
/// <remarks>
/// <para>
/// It reads the whole set and chooses from it rather than asking the deployment for one rule. A rule means what it does
/// partly because of its position — which rule reaches a message first is a property of the set — so the deployment
/// publishes the set and choosing is this command's half of the work. It also means a rule named after one of the
/// deployment's own routes is reachable like any other.
/// </para>
/// <para>
/// The condition an operator wrote is not shown, because the deployment does not have it: a compiled rule carries no
/// text, which is what keeps an address somebody typed into a condition out of every record naming the rule. What is
/// shown of the condition is the facts it can reach.
/// </para>
/// </remarks>
internal static class ShowRuleCommand
{
    /// <summary>Builds the <c>rules show</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Argument<string> nameArgument = new("name")
        {
            Description = "The name of the rule to show, as the deployment's configuration declares it.",
        };

        Command command = new("show", "Report one loaded rule: what it applies to, what it reads, and what a match does.")
        {
            nameArgument,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(nameArgument) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string name,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var ruleSet = await new AdminApiClient(transport, context.Console)
            .ReadRulesAsync(profile.Token, cancellationToken);

        // Names are compared without regard to case for the reason the rule set refuses two of them that way: a
        // deployment cannot declare two rules an operator would read as one, so neither can a lookup find two.
        var rule = ruleSet.Rules?.FirstOrDefault(
            candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));

        if (rule is null)
        {
            throw new CliFailure(
                $"This deployment has loaded no rule named '{name}'. Run '{CliRootCommand.CommandName} rules list' to see the ones it has.");
        }

        WriteRule(context, ruleSet, rule);

        return CliExitCode.Success;
    }

    private static void WriteRule(CliContext context, LoadedRuleSet ruleSet, LoadedRule rule)
    {
        context.Console.WriteLine($"{rule.Name}");
        context.Console.WriteLine($"Rule set:    {ruleSet.Revision ?? "unreported"}");
        context.Console.WriteLine($"Applies to:  {rule.DescribeScope()}");
        context.Console.WriteLine($"Runs on:     {rule.DescribeTriggers()}");
        context.Console.WriteLine($"Reads facts: {DescribeReadableFacts(rule)}");
        context.Console.WriteLine($"A match:     {rule.DescribeActions()}");
        context.Console.WriteLine(
            $"The condition is in this deployment's configuration under '{MailRuleConfigurationLocation.SectionName}', which is the only place a rule is written or changed.");
    }

    private static string DescribeReadableFacts(LoadedRule rule) => rule.ReadableFacts is { Count: > 0 } facts
        ? string.Join(", ", facts)
        : "none";
}
