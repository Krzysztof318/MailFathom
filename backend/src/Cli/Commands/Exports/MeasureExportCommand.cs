// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Exports;

/// <summary>Reports what an export of a mailbox, or of one folder of it, would carry — without starting one.</summary>
/// <remarks>
/// It reads the recorded length of each stored message and never a payload, so it answers in seconds whatever the
/// mailbox holds. Nothing is written and no job exists afterwards, which is what makes it the thing to run before
/// deciding whether to export a whole mailbox or one folder at a time.
/// </remarks>
internal static class MeasureExportCommand
{
    /// <summary>Builds the <c>export measure</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var folderOption = CliOptions.NarrowedMailFolder();

        Command command = new("measure", "Report what exporting a mailbox, or one folder of it, would carry.")
        {
            endpointOption,
            accountOption,
            folderOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(accountOption)!,
            result.GetValue(folderOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        string account,
        string? folder,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var measurement = await deployment.MeasureMailboxExportAsync(
            profile.Token,
            account,
            folder,
            cancellationToken);

        context.Console.Write(ExportOutput.Describe(measurement));

        if (measurement.Folders is { Count: > 0 } folders)
        {
            context.Console.Write(ExportOutput.Tabulate(folders));
        }

        context.Console.WriteLine(
            $"Nothing was written. Start the export with '{CliRootCommand.CommandName} export start'.");

        return CliExitCode.Success;
    }
}
