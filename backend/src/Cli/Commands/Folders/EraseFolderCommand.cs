// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Folders;

namespace MailFathom.Cli.Commands.Folders;

/// <summary>Takes away the mail a deployment stored for a folder it has stopped mirroring.</summary>
/// <remarks>
/// <para>
/// The only thing in MailFathom that erases a folder's local copy, and it exists precisely so that no configuration
/// value has to. Switching a folder's synchronization off keeps what it stored and removing its mapping leaves the rows
/// where they are, both so that editing a file cannot dispose of somebody's mail — which leaves an operator who means
/// it with nothing to ask until this.
/// </para>
/// <para>
/// It refuses a folder the account still mirrors, because the next run would refill what the erasure removed. An alias
/// no mapping names is accepted rather than refused: a folder whose mapping was withdrawn is the case that most needs
/// erasing, and it is the one case no configuration value can express.
/// </para>
/// <para>
/// A folder is erased in bounded passes, and the command sends as many as the folder needs. Interrupting it stops it
/// between passes rather than part way through one: what a pass committed stays gone and the rest waits for the next
/// invocation, so there is no state this command can leave a folder in that running it again does not finish.
/// </para>
/// </remarks>
internal static class EraseFolderCommand
{
    /// <summary>Builds the <c>folder erase</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var folderOption = CliOptions.MailFolder();

        Command command = new("erase", "Erase the mail a deployment stored for a folder it no longer mirrors.")
        {
            accountOption,
            folderOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption) ?? string.Empty,
            result.GetValue(folderOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string account,
        string folder,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var erasedCount = 0;
        MailFolderErasure? pass = null;

        try
        {
            do
            {
                pass = await deployment.EraseFolderMirrorAsync(
                    profile.Token,
                    account,
                    folder,
                    cancellationToken);

                erasedCount += pass.ErasedEmailCount;

                // A pass that removed nothing and reports more to come would repeat forever, because nothing about the
                // next request would differ from this one. The deployment's own eraser cannot answer that — a pass
                // saying mail remains has filled its bound — so this is a deployment answering something else, and
                // asking it again is the one thing that could not help.
                if (pass is { ErasedEmailCount: 0, EmailsRemain: true })
                {
                    throw new CliFailure(
                        "The deployment reported that mail remains but erased none of it, so asking again would not make progress. Nothing further was erased.");
                }

                if (pass.EmailsRemain)
                {
                    context.Console.WriteLine(Describe(erasedCount, "erased so far"));
                }
            }
            while (pass.EmailsRemain);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            context.Console.WriteError(
                $"{Describe(erasedCount, "erased")} before the erasure was interrupted. The rest is still there; run the command again to continue.");

            return CliExitCode.Failure;
        }

        var erased = Name(pass.Folder, folder);
        var mailbox = Name(pass.Account, account);

        context.Console.WriteLine(erasedCount == 0
            ? $"The deployment stored nothing for {erased} under {mailbox}, so nothing was erased."
            : $"{Describe(erasedCount, "erased")} from {erased} under {mailbox}. The folder holds none, and its checkpoint went with them, so mirroring it again starts from the beginning rather than resuming.");

        return CliExitCode.Success;
    }

    /// <summary>Names a thing the way the deployment did, falling back to the way the operator wrote it.</summary>
    /// <remarks>
    /// The deployment normalizes an alias, so echoing its answer is what confirms that what was erased is what was
    /// meant. A deployment that named neither back is answered for with the operator's own text rather than with a
    /// blank, because a sentence reporting erased mail has to say which folder it was about.
    /// </remarks>
    private static string Name(string? reported, string requested) =>
        reported is { Length: > 0 } named ? named : requested;

    /// <summary>Describes a count of erased mail, grouped invariantly for the reason every other figure this tool prints is.</summary>
    private static string Describe(int erasedCount, string state) => string.Create(
        CultureInfo.InvariantCulture,
        $"{erasedCount} stored {(erasedCount == 1 ? "email" : "emails")} {state}");
}
