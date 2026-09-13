// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>Lists the mail accounts a deployment holds, and who each is assigned to.</summary>
internal static class ListMailAccountsCommand
{
    /// <summary>Builds the <c>account list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("list", "List the mail accounts this deployment holds, and who each is assigned to.")
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
        var deployment = new AdminApiClient(transport, context.Console);

        var listing = await deployment.ReadMailAccountsAsync(profile.Token, cancellationToken);

        if (listing.Accounts is not { Count: > 0 } accounts)
        {
            context.Console.WriteLine("This deployment holds no mail accounts. Create one with 'mfctl account add'.");

            return CliExitCode.Success;
        }

        foreach (var account in accounts)
        {
            MailAccountOutput.WriteHeading(context.Console, account);
        }

        if (listing.Truncated)
        {
            context.Console.WriteLine(
                $"This deployment holds more than {accounts.Count} mail accounts; only the first {accounts.Count} are listed.");
        }

        return CliExitCode.Success;
    }
}
