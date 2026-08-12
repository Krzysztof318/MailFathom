// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Reports where one account's whole-mailbox rule run has got to, or how the last one ended.</summary>
/// <remarks>
/// Where a run started with <c>rules run</c> is watched from. The run outlives the command that asked for it and is
/// carried by the account's synchronization runs, so how far it has come is a question asked repeatedly rather than
/// waited on — and an account that has never been asked for one is an answer rather than an error.
/// </remarks>
internal static class RuleRunStatusCommand
{
    /// <summary>Builds the <c>rules run-status</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();

        Command command = new("run-status", "Report where an account's whole-mailbox rule run has got to.")
        {
            accountOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string account,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var state = await new AdminApiClient(transport, context.Console)
            .ReadRuleRunAsync(profile.Token, account, cancellationToken);

        if (state.Run is not { } run)
        {
            context.Console.WriteLine(
                $"No rule run has ever been asked for over {account}. Start one with '{CliRootCommand.CommandName} rules run --account {account}'.");

            return CliExitCode.Success;
        }

        context.Console.WriteLine($"{account} — {run.DescribeState()}");
        context.Console.WriteLine($"Requested: {run.RequestedAt:u}");
        context.Console.WriteLine($"Rule set:  {run.Revision ?? "not yet bound; no pass has picked the run up"}");
        context.Console.WriteLine($"Progress:  {run.DescribeProgress()}");

        return CliExitCode.Success;
    }
}
