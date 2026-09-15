// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Exports;

/// <summary>Reports the exports one account has, or where one of them stands.</summary>
/// <remarks>
/// One command rather than a listing and a reading, because they are the same question asked at two widths: an
/// operator who started an export looks it up by the identity they were given, and one who has lost it lists what the
/// account has and finds it there. Naming an export narrows the answer to that export and nothing else changes.
/// </remarks>
internal static class ExportStatusCommand
{
    /// <summary>Builds the <c>export status</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var exportOption = ExportOptions.NarrowedExport();

        Command command = new("status", "Report the exports an account has, or where one of them stands.")
        {
            endpointOption,
            accountOption,
            exportOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(accountOption)!,
            result.GetValue(exportOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        string account,
        Guid? exportId,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        if (exportId is { } named)
        {
            var export = await deployment.ReadMailboxExportAsync(profile.Token, account, named, cancellationToken);

            context.Console.Write(ExportOutput.Describe(export));

            return CliExitCode.Success;
        }

        var listing = await deployment.ListMailboxExportsAsync(profile.Token, account, cancellationToken);

        if (listing.Exports is not { Count: > 0 } exports)
        {
            context.Console.WriteLine(
                $"This account has no exports. Ask for one with '{CliRootCommand.CommandName} export start'.");

            return CliExitCode.Success;
        }

        context.Console.Write(ExportOutput.Tabulate(exports));

        return CliExitCode.Success;
    }
}
