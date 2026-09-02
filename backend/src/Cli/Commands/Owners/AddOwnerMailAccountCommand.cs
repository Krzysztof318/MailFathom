// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Owners;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Declares one more mailbox in an owner's record.</summary>
/// <remarks>
/// <para>
/// The settings are read from a file rather than composed out of options, and deliberately: a mail account carries a
/// server, a port, a transport-security choice, an authentication mechanism, a credential reference, and a folder
/// selection, and a command that spelled each of those as a flag would be a second vocabulary for the settings the
/// deployment already documents. What goes in the file is the JSON object a configuration source would have carried, so
/// what an operator writes here is what they would have written there.
/// </para>
/// <para>
/// It carries a reference to a credential rather than a credential. A password or a client secret written into this
/// file would reach the deployment as a value it refuses, which is the same refusal a configuration file gets and for
/// the same reason: what a record keeps is the reference, and the material stays where the deployment's secret scheme
/// put it.
/// </para>
/// <para>
/// The record is read first so the write is composed over the version it was read at. That is what makes two
/// administrators declaring a mailbox at once produce a refusal rather than one of them silently dropping the other's.
/// </para>
/// </remarks>
internal static class AddOwnerMailAccountCommand
{
    /// <summary>Builds the <c>owner account add</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var ownerOption = OwnerOptions.Owner();

        Option<FileInfo> fileOption = new("--from-file", "-f")
        {
            Description =
                "The JSON object declaring the mail account, in the shape a configuration source states one: an identifier, a display name, the server settings, and a reference to the credential.",
            Required = true,
        };

        Command command = new("add", "Declare one more mailbox in an owner's record.")
        {
            ownerOption,
            fileOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(ownerOption),
            result.GetValue(fileOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedOwner,
        FileInfo? file,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        if (file is null || !file.Exists)
        {
            throw new CliFailure(
                $"There is no file at {file?.FullName ?? "the path given"} to read the mail account from.");
        }

        var declaration = await ReadAsync(file, cancellationToken);

        if (string.IsNullOrWhiteSpace(declaration))
        {
            throw new CliFailure($"{file.FullName} is empty, so it declares no mail account.");
        }

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var owner = await OwnerOptions.ResolveOwnerAsync(
            deployment,
            profile.Token,
            requestedOwner,
            cancellationToken);

        var record = await deployment.ReadOwnerRecordAsync(profile.Token, owner, cancellationToken);

        var answer = await deployment.AddOwnerMailAccountAsync(
            profile.Token,
            owner,
            new OwnerMailAccountRequest(record.Version, declaration),
            cancellationToken);

        return OwnerOutput.ReportWrite(context, answer);
    }

    /// <summary>Reads the declaration the invocation named.</summary>
    /// <exception cref="CliFailure">Thrown when the path exists and its contents cannot be read.</exception>
    /// <remarks>
    /// A path the operator gave that is a directory, is not readable by this account, or is on a filesystem that failed
    /// mid-read is something they can act on, so it is reported as a sentence naming the path rather than reaching
    /// <c>CliRunner</c> as a stack trace — the same answer the editing buffer gives for the same situation.
    /// </remarks>
    private static async Task<string> ReadAsync(FileInfo file, CancellationToken cancellationToken)
    {
        try
        {
            return await File.ReadAllTextAsync(file.FullName, cancellationToken);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new CliFailure($"{file.FullName} could not be read, so nothing was written.", failure);
        }
    }
}
