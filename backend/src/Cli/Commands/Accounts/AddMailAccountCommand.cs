// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Accounts;
using MailFathom.Cli.Commands.Users;

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>Creates a mail account and assigns it to one user.</summary>
/// <remarks>
/// <para>
/// The settings are read from a file rather than composed out of options: a mail account carries a server, a port, a
/// transport-security choice, an authentication mechanism, a credential reference, and a folder selection, and a command
/// spelling each as a flag would be a second vocabulary for the settings the deployment already documents.
/// </para>
/// <para>
/// The file carries a reference to a credential rather than a credential, and no identifier: the deployment generates
/// one and this command prints it. An address another account already holds is refused, and the answer names the
/// command that assigns that account instead.
/// </para>
/// </remarks>
internal static class AddMailAccountCommand
{
    /// <summary>Builds the <c>account add</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();
        var fileOption = MailAccountOptions.DeclarationFile();

        Command command = new("add", "Create a mail account and assign it to one user.")
        {
            userOption,
            fileOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            result.GetValue(fileOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        FileInfo? file,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var declaration = await MailAccountOptions.ReadDeclarationAsync(file, cancellationToken);
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var user = await UserOptions.ResolveUserAsync(deployment, profile.Token, requestedUser, cancellationToken);

        var answer = await deployment.CreateMailAccountAsync(
            profile.Token,
            new MailAccountCreationRequest(user, declaration),
            cancellationToken);

        return MailAccountOutput.ReportWrite(context, answer);
    }
}
