// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Exports;

/// <summary>Fetches a finished archive and writes it to a file.</summary>
/// <remarks>
/// <para>
/// The archive arrives as it is written to disk and is never held in memory, so a mailbox of any size is fetched the
/// same way. The file must not already exist: an archive is the only copy of a mailbox somebody is taking with them,
/// and overwriting one silently is the way that copy is lost.
/// </para>
/// <para>
/// Nothing is deleted afterwards. The archive stays on the deployment until its retention period ends or
/// <c>export delete</c> removes it, which is what lets a download that failed be repeated.
/// </para>
/// </remarks>
internal static class DownloadExportCommand
{
    /// <summary>Builds the <c>export download</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var exportOption = ExportOptions.Export();

        Option<string?> outputOption = new("--output", "-o")
        {
            Description =
                "The file to write. Defaults to 'mailfathom-export-<export>.zip' in the current directory, and must not already exist.",
        };

        Command command = new("download", "Fetch a finished export's archive and write it to a file.")
        {
            endpointOption,
            accountOption,
            exportOption,
            outputOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(accountOption)!,
            result.GetValue(exportOption),
            result.GetValue(outputOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        string account,
        Guid exportId,
        string? requestedOutput,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var destination = Path.GetFullPath(requestedOutput ?? DefaultFileNameFor(exportId));

        var byteCount = await deployment.DownloadMailboxExportArchiveAsync(
            profile.Token,
            account,
            exportId,
            destination,
            cancellationToken);

        CliDetails details = new();
        details.Add("Written", ConsoleSafeText.Sanitize(destination) ?? destination);
        details.Add("Size", string.Create(CultureInfo.InvariantCulture, $"{byteCount:N0} bytes"));

        context.Console.Write(details);
        context.Console.WriteLine(
            $"The deployment still holds the archive. Free that storage with '{CliRootCommand.CommandName} export delete'.");

        return CliExitCode.Success;
    }

    /// <summary>Names the file an archive is written to when the operator named none.</summary>
    /// <remarks>
    /// The export's own identity rather than the account or the folder, which is the name the deployment offers as
    /// well: a file name travels into a downloads directory, a shell history, and a backup index, and neither of those
    /// two is something an export has any reason to write there.
    /// </remarks>
    private static string DefaultFileNameFor(Guid exportId) =>
        $"mailfathom-export-{exportId.ToString("N", CultureInfo.InvariantCulture)}.zip";
}
