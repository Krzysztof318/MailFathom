// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Spam;

/// <summary>Reports where one account's whole-mailbox classification run has got to, or how the last one ended.</summary>
/// <remarks>
/// Where a run started with <c>spam run</c> is watched from, and where the answer to "what would it do" is read. The run
/// outlives the command that asked for it and is carried by the account's synchronization runs, so how far it has come
/// is a question asked repeatedly rather than waited on — and an account that has never been asked for one is an answer
/// rather than an error.
/// </remarks>
internal static class ClassificationRunStatusCommand
{
    /// <summary>Builds the <c>spam run-status</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();

        Command command = new("run-status", "Report where an account's whole-mailbox classification run has got to.")
        {
            accountOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
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
            .ReadSpamClassificationRunAsync(profile.Token, account, cancellationToken);

        if (state.Run is not { } run)
        {
            context.Console.WriteLine(
                $"No classification run has ever been asked for over {account}. Start one with '{CliRootCommand.CommandName} spam run --account {account}'.");

            return CliExitCode.Success;
        }

        CliDetails details = new();
        details.Add("Account", $"{account} — {run.DescribeState()}");
        details.Add("Requested", $"{run.RequestedAt:u}");
        details.Add("Folders", string.Join(", ", run.Folders));
        details.Add("Acting", run.IsDryRun ? "no — dry run" : "yes");
        details.Add("Rescoring", run.Rescores ? "yes" : "no");
        details.Add("Profile", run.Profile ?? "not yet bound; no pass has picked the run up");
        details.Add("Progress", run.DescribeProgress());
        details.Add("Found", run.DescribeOutcome());

        context.Console.Write(details);

        if (run.IsDryRun && run.ActedEmailCount > 0)
        {
            context.Console.WriteLine(
                $"Nothing has been changed on the mail server. Ask again with --apply to carry it out, and read what was decided with '{CliRootCommand.CommandName} spam classifications --account {account} --verdict Spam'.");
        }

        return CliExitCode.Success;
    }
}
