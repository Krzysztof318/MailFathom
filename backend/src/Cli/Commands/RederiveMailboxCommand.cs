// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Mailboxes;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands;

/// <summary>Asks a deployment to re-read the MIME it already stores into the properties a newer release records.</summary>
/// <remarks>
/// <para>
/// The cheap way to fill in properties a newer release added, and the one to reach for first. Everything the stored
/// payload itself carries — the sender identity the receiving server authenticated is today's example — is already on
/// the deployment's own disk, so filling in a column costs a local read and a parse rather than a mailbox over IMAP.
/// <c>mfctl mailbox rewind</c> is the answer for a property only the mail server knows.
/// </para>
/// <para>
/// It opens no mailbox session at all, so it cannot touch a remote <c>\Seen</c> flag however long it runs, and it
/// re-chunks and re-embeds nothing: passages and vectors are derived from text that the same bytes read by the same
/// reader produce unchanged, and spending a provider bill to arrive back there is not something a metadata refresh
/// does.
/// </para>
/// <para>
/// It returns as soon as the deployment has written the request down, and never waits for the walk. The re-derivation
/// is carried by the deployment's own durable background work, so this terminal is not what keeps it alive and closing
/// it cannot cancel one; <c>mailbox rederive-status</c> is where the run is watched from.
/// </para>
/// </remarks>
internal static class RederiveMailboxCommand
{
    /// <summary>Builds the <c>mailbox rederive</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var folderOption = CliOptions.NarrowedMailFolder();

        Command command = new(
            "rederive",
            "Ask the deployment to re-read the MIME already stored into the properties a newer version records from it.")
        {
            accountOption,
            folderOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption) ?? string.Empty,
            result.GetValue(folderOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    /// <summary>Names the scope the way the operator wrote it.</summary>
    internal static string Scope(string account, string? folder) => folder is { Length: > 0 } narrowed
        ? $"{narrowed} under {account}"
        : $"every folder under {account}";

    private static async Task<int> RunAsync(
        CliContext context,
        string account,
        string? folder,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var started = await new AdminApiClient(transport, context.Console)
            .RederiveMailboxAsync(profile.Token, account, folder, cancellationToken);

        context.Console.WriteLine(started.Started
            ? $"A re-derivation of {Scope(account, folder)} has been asked for."
            : $"A re-derivation of {Scope(account, folder)} was already under way, so nothing new was started.");

        if (started.Run is { } run)
        {
            CliDetails details = new();
            details.Add("Requested", $"{run.RequestedAt:u}");
            details.Add("Progress", run.DescribeProgress());

            context.Console.Write(details);
        }

        // A queue refusing the work is backpressure rather than a failure: the run is recorded and nothing was lost, and
        // the same command is what puts it in motion once the deployment has drained what is in front of it. Anything
        // else that leaves nothing carrying the run is a segment the deployment will not attempt again on its own,
        // which the queue's own commands are what reverse — so the two endings send the operator to different places.
        if (!string.Equals(started.Carriage, MailboxRederivationStart.CarriedName, StringComparison.Ordinal))
        {
            context.Console.WriteError(string.Equals(
                started.Carriage,
                MailboxRederivationStart.QueueAtCapacityName,
                StringComparison.Ordinal)
                ? "The deployment's queue is full, so nothing is carrying the run yet. Run this command again once it has drained."
                : $"Nothing is carrying the run: the work that would advance it stopped. Find it with '{CliRootCommand.CommandName} jobs dead-letters' and return it with '{CliRootCommand.CommandName} jobs retry'.");

            return CliExitCode.Failure;
        }

        context.Console.WriteLine(
            $"The deployment carries the run in the background. Watch it with '{CliRootCommand.CommandName} mailbox rederive-status --account {account}{FolderArgument(folder)}'.");

        return CliExitCode.Success;
    }

    /// <summary>Repeats the folder the operator narrowed to, so the suggested command watches the run they started.</summary>
    /// <remarks>Two scopes are two runs, so a suggestion that dropped the folder would point at a walk of the whole account that nobody asked for.</remarks>
    internal static string FolderArgument(string? folder) =>
        folder is { Length: > 0 } narrowed ? $" --folder {narrowed}" : string.Empty;
}
