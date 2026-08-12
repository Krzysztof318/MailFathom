// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Rules;

/// <summary>Asks a deployment to run one account's rules over every message it already holds for that account.</summary>
/// <remarks>
/// <para>
/// The command an operator runs after writing a rule. Rules reach mail as it arrives, so a rule written today does
/// nothing about the mail already in the mailbox until somebody asks — and this is that asking.
/// </para>
/// <para>
/// It returns as soon as the deployment has written the request down, and never waits for the walk. The pass is a step
/// of the account's synchronization run, so this terminal is not what keeps it alive and closing it cannot cancel one;
/// <c>rules run-status</c> is where the run is watched from.
/// </para>
/// <para>
/// Asking twice is asking once. A second request while a run is outstanding is answered with the run already under way
/// rather than starting a second walk of one mailbox, and the command says which of the two happened.
/// </para>
/// </remarks>
internal static class RunRulesCommand
{
    /// <summary>Builds the <c>rules run</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();

        Command command = new("run", "Run an account's rules over every message the deployment already holds for it.")
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
        var started = await new AdminApiClient(transport, context.Console)
            .StartRuleRunAsync(profile.Token, account, cancellationToken);

        context.Console.WriteLine(started.Started
            ? $"A rule run over {account} has been asked for."
            : $"A rule run over {account} was already under way, so nothing new was started.");

        if (started.Run is { } run)
        {
            context.Console.WriteLine($"Progress: {run.DescribeProgress()}");
        }

        context.Console.WriteLine(
            $"The run is carried by the account's synchronization runs. Watch it with '{CliRootCommand.CommandName} rules run-status --account {account}'.");

        return CliExitCode.Success;
    }
}
