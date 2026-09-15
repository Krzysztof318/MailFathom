// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Exports;

/// <summary>Asks the deployment to write an archive of a mailbox, or of one folder of it.</summary>
/// <remarks>
/// <para>
/// The measurement comes first and is shown before anything is asked for, which is what the confirmation is about: an
/// export is a second full copy of a mailbox, kept on the deployment until somebody deletes it or its retention period
/// ends, so how large it is belongs in front of the person agreeing to it.
/// </para>
/// <para>
/// The command returns as soon as the export is written down. The deployment writes the archive, so closing the
/// terminal stops nothing, and <c>export status</c> is where it is watched.
/// </para>
/// </remarks>
internal static class StartExportCommand
{
    /// <summary>Builds the <c>export start</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var folderOption = CliOptions.NarrowedMailFolder();
        var confirmedOption = CliOptions.Confirmed("export");

        Command command = new("start", "Ask the deployment to write an archive of a mailbox, or of one folder of it.")
        {
            endpointOption,
            accountOption,
            folderOption,
            confirmedOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(accountOption)!,
            result.GetValue(folderOption),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        string account,
        string? folder,
        bool confirmedUpFront,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        // Measured first and shown before the question, so the figure the operator agrees to is the deployment's own
        // rather than one this command guessed. It is measured again on the other side when the export is asked for,
        // which is what keeps a refusal over the size limit a refusal rather than a job that fails later.
        var measurement = await deployment.MeasureMailboxExportAsync(
            profile.Token,
            account,
            folder,
            cancellationToken);

        context.Console.Write(ExportOutput.Describe(measurement));

        // Reported on standard error and with a failing code, which is what every other command does when it did not do
        // what it was asked. A caller that redirected the output reads an empty result and a reason rather than a
        // sentence about nothing happening mixed into what it captured.
        if (!Agreed(context, confirmedUpFront))
        {
            context.Console.WriteError("Nothing was exported.");

            return CliExitCode.Failure;
        }

        var started = await deployment.StartMailboxExportAsync(profile.Token, account, folder, cancellationToken);

        if (started.Export is not { } export)
        {
            context.Console.WriteError("The deployment answered without an export, which no version of it sends.");

            return CliExitCode.Failure;
        }

        context.Console.Write(ExportOutput.Describe(export));

        if (started.WasAlreadyRunning)
        {
            context.Console.WriteLine(
                "This export was already being written, so nothing was started over and its progress was kept.");
        }

        context.Console.WriteLine(
            $"Watch it with '{CliRootCommand.CommandName} export status', and fetch it with '{CliRootCommand.CommandName} export download' once it is finished.");

        return CliExitCode.Success;
    }

    /// <summary>Reports whether the person running this agreed to the export, refusing to guess where nobody can answer.</summary>
    /// <remarks>
    /// Asked whatever the measurement says, on the rule every irreversible or costly act here follows: the figure
    /// informs the question rather than answering it, and an archive of an empty mailbox is still a copy the deployment
    /// keeps and a step in somebody taking their mail away.
    /// </remarks>
    private static bool Agreed(CliContext context, bool confirmedUpFront) => CliConfirmation.Agreed(
        context,
        confirmedUpFront,
        "There is nobody at the terminal to agree to this, and an export is a second full copy of the mailbox until it is deleted. Pass --yes to export without being asked.",
        "Export that mailbox? [y/N] ");
}
