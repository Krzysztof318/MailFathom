// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Owners;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Records a person this deployment did not hold.</summary>
/// <remarks>
/// <para>
/// The identifier is the deployment's to mint, so this states a label and nothing else. What comes back is the handle
/// every later act names the owner by, which is worth capturing: it is the one thing a script cannot reconstruct from
/// what it typed.
/// </para>
/// <para>
/// A new owner's mail accounts are their own record's from the first moment, so nothing about them is in a
/// configuration file and <c>owner account add</c> is what puts a mailbox there. That is why recording somebody is a
/// small act and adopting an existing owner is not: this one moves no decision out of a file.
/// </para>
/// </remarks>
internal static class AddOwnerCommand
{
    /// <summary>Builds the <c>owner add</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Option<string> displayNameOption = new("--display-name")
        {
            Description =
                "The label this owner is told apart by, unique across the deployment. It may change later and is never the identity.",
            Required = true,
        };

        Command command = new("add", "Record an owner this deployment does not hold.")
        {
            displayNameOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(displayNameOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string displayName,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var recorded = await deployment.ProvisionOwnerAsync(
            profile.Token,
            new OwnerProvisioningRequest(displayName),
            cancellationToken);

        context.Console.WriteLine($"Recorded {displayName} as {recorded.Id:D}.");
        context.Console.WriteNotice(
            "Their mail accounts are read from their own record; no configuration source reaches them. Declare one with "
            + "'mfctl owner account add', and provision a way for them to sign in with 'mfctl credential create'. The "
            + "replica this request reached serves this owner now; other replicas pick up the change after their next "
            + "owner write or restart.");

        return CliExitCode.Success;
    }
}
