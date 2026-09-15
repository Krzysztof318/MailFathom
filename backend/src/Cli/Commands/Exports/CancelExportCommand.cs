// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Exports;

/// <summary>Stops an export still being written, which deletes whatever it had produced.</summary>
/// <remarks>
/// The archive it had written is deleted rather than kept, because a partial archive of a mailbox is the one thing an
/// export must never leave behind: it opens, it lists entries, and it is not the mailbox. An export that has already
/// finished is reported as it stands — <c>export delete</c> is what removes a finished archive.
/// </remarks>
internal static class CancelExportCommand
{
    /// <summary>Builds the <c>export cancel</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var exportOption = ExportOptions.Export();
        var confirmedOption = CliOptions.Confirmed("cancellation");

        Command command = new("cancel", "Stop an export still being written, deleting whatever it had produced.")
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
            context.Console.WriteError("Nothing was cancelled.");

            return CliExitCode.Failure;
        }

        var cancelled = await deployment.CancelMailboxExportAsync(
            profile.Token,
            account,
            exportId,
            cancellationToken);

        context.Console.Write(ExportOutput.Describe(cancelled));

        return CliExitCode.Success;
    }

    /// <summary>Reports whether the person running this agreed to the cancellation, refusing to guess where nobody can answer.</summary>
    private static bool Agreed(CliContext context, bool confirmedUpFront) => CliConfirmation.Agreed(
        context,
        confirmedUpFront,
        "There is nobody at the terminal to agree to this, and cancelling deletes what the export had already written. Pass --yes to cancel without being asked.",
        "Cancel that export and delete what it has written? [y/N] ");
}
