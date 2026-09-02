// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Rules;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Reports which mail rules a deployment is running, and whether its rule file was accepted.</summary>
/// <remarks>
/// The command an operator runs after editing rules. A reload whose rules did not validate is refused and leaves the
/// previous set in force, which is the right behavior and a silent one — the refusal reaches the log and nothing else,
/// so a file that was edited and a deployment that is unchanged look identical until this is asked.
/// </remarks>
internal static class ListRulesCommand
{
    /// <summary>Builds the <c>rules list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("list", "Report the mail rules the deployment has loaded, in the order they run.")
        {
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var ruleSet = await new AdminApiClient(transport, context.Console)
            .ReadRulesAsync(profile.Token, cancellationToken);

        WriteRuleSet(context, profile.Name, ruleSet);

        return CliExitCode.Success;
    }

    /// <summary>Writes the set as an operator reads it: what is in force, then the rules in the order they run.</summary>
    internal static void WriteRuleSet(CliContext context, string profileName, LoadedRuleSet ruleSet)
    {
        context.Console.WriteLine($"{profileName} — rule set {ruleSet.Revision ?? "unreported"}");
        context.Console.WriteLine(DescribeAcceptance(ruleSet));

        if (ruleSet.Rules is not { Count: > 0 } rules)
        {
            context.Console.WriteLine("This deployment declares no rules, so nothing is applied to its mail.");

            return;
        }

        context.Console.WriteLine(string.Empty);

        CliTable listing = new("Rule", "Applies to", "Runs on", "A match");

        foreach (var rule in rules)
        {
            listing.AddRow(
                rule.Name ?? "an unnamed rule",
                rule.DescribeScope(),
                rule.DescribeTriggers(),
                rule.DescribeActions());
        }

        context.Console.Write(listing);
    }

    /// <summary>States whether the configuration on disk is the one the running set was read from.</summary>
    /// <remarks>
    /// The refusal names a count rather than the messages. What was wrong is in the deployment's log, and the messages
    /// quote what the operator wrote — a condition among it — which is not something to echo back over the network to
    /// somebody who can already read the file.
    /// </remarks>
    private static string DescribeAcceptance(LoadedRuleSet ruleSet) => ruleSet.ConfigurationAccepted
        ? "Configuration: accepted. What is running is what the file says."
        : $"Configuration: REFUSED — {ruleSet.RefusedSettingCount} setting(s) were rejected and the rules above are the last set that validated. The deployment's log says which.";
}
