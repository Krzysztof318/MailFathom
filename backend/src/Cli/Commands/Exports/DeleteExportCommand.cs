// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Exports;

/// <summary>Deletes a finished archive from the deployment, typically right after it has been downloaded.</summary>
/// <remarks>
/// <para>
/// An archive is a second full copy of a mailbox for as long as the deployment keeps it, so this is what frees that
/// storage rather than waiting for the retention period. A later download is refused rather than served.
/// </para>
/// <para>
/// Deleting an archive that has already expired or been deleted succeeds, because the caller asked for a state the
/// deployment is already in — which is exactly what an operator repeating a cleanup wants. An export still being
/// written is cancelled instead, since there is no archive to delete yet.
/// </para>
/// </remarks>
internal static class DeleteExportCommand
{
    /// <summary>Builds the <c>export delete</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var exportOption = ExportOptions.Export();
        var confirmedOption = CliOptions.Confirmed("deletion");

        Command command = new("delete", "Delete a finished export's archive from the deployment.")
        {
            endpointOption,
            accountOption,
            exportOption,
            confirmedOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(accountOption)!,
            result.GetValue(exportOption),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        string account,
        Guid exportId,
        bool confirmedUpFront,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        if (!Agreed(context, confirmedUpFront))
        {
            context.Console.WriteError("Nothing was deleted.");

            return CliExitCode.Failure;
        }

        var deleted = await deployment.DeleteMailboxExportAsync(
            profile.Token,
            account,
            exportId,
            cancellationToken);

        context.Console.Write(ExportOutput.Describe(deleted));

        return CliExitCode.Success;
    }

    /// <summary>Reports whether the person running this agreed to the deletion, refusing to guess where nobody can answer.</summary>
    /// <remarks>Asked even though the mailbox itself is untouched, because the archive may be the copy somebody was about to fetch and nothing here can tell whether they already did.</remarks>
    private static bool Agreed(CliContext context, bool confirmedUpFront) => CliConfirmation.Agreed(
        context,
        confirmedUpFront,
        "There is nobody at the terminal to agree to this, and deleting an archive nobody downloaded loses the copy it was. Pass --yes to delete without being asked.",
        "Delete that archive from the deployment? [y/N] ");
}
