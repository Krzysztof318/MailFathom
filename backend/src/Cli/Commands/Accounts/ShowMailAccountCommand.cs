// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Editing;

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>Reads one mail account's declaration as the deployment holds it.</summary>
/// <remarks>The declaration arrives with every secret-bearing value replaced by the redaction marker; the address, the mail server, and the mailbox user name are printed as the account holds them. It is printed as the deployment holds it, or as YAML where the operator asked for that view, so that reading an account and editing one agree.</remarks>
internal static class ShowMailAccountCommand
{
    /// <summary>Builds the <c>account show</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var formatOption = CliOptions.DocumentFormat();
        var accountOption = MailAccountOptions.Account();

        Command command = new("show", "Read one mail account's declaration as this deployment holds it.")
        {
            accountOption,
            formatOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption),
            result.GetValue(formatOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid accountId,
        DocumentView view,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var account = await deployment.ReadMailAccountAsync(profile.Token, accountId, cancellationToken);

        MailAccountOutput.WriteHeading(context.Console, account);
        context.Console.WriteLine(EditorDrivenDocument.Show(account.Declaration ?? "{}", view));

        return CliExitCode.Success;
    }
}
