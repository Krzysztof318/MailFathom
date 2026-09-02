// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Mailboxes;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands;

/// <summary>Answers whether a deployment is keeping its mailboxes up to date, and where it has stopped if it is not.</summary>
/// <remarks>
/// The one command an operator runs when mail is not arriving. It exists because that question has answers that look
/// nothing alike from outside — synchronization switched off, an account backing off a server that is refusing it, an
/// alias naming no advertised folder, a folder still working through a backfill, and a folder repeating one batch it
/// cannot get past — and every one of them presents as a mailbox that looks empty.
/// </remarks>
internal static class MailboxStatusCommand
{
    /// <summary>Builds the <c>mailbox status</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("status", "Report what mail synchronization is doing, account by account and folder by folder.")
        {
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var status = await new AdminApiClient(transport, context.Console)
            .ReadMailboxSynchronizationStatusAsync(profile.Token, cancellationToken);

        var accounts = status.Accounts ?? [];

        CliDetails deployment = new();
        deployment.Add("Deployment", $"{profile.Name} ({profile.Endpoint.GetLeftPart(UriPartial.Authority)})");
        deployment.Add("Synchronization", DescribeSwitch(status.SynchronizationEnabled));

        if (accounts.Count == 0)
        {
            deployment.Add("Accounts", "none configured, so this deployment fetches no mail at all.");
            context.Console.Write(deployment);

            return CliExitCode.Success;
        }

        context.Console.Write(deployment);

        foreach (var account in accounts)
        {
            WriteAccount(context, account);
        }

        return CliExitCode.Success;
    }

    /// <summary>States the operator's own switch, because every count below it is still while it is off.</summary>
    /// <remarks>
    /// It is the first line rather than a footnote for that reason. A deployment with synchronization off reports
    /// accounts, folders, and stored progress exactly as one that is running would, and the difference between the two
    /// readings is this one word.
    /// </remarks>
    private static string DescribeSwitch(bool enabled) => enabled
        ? "on"
        : "off — this deployment fetches no mail, and everything below is what it stored before that.";

    /// <summary>Writes one account as what it is doing, then the folders it is doing it to.</summary>
    /// <remarks>
    /// The account's own readings are a record and its folders are a listing, which is why the two are drawn as
    /// different shapes: an operator reads the account's phase and backoff once, and scans the folders for the one that
    /// has stopped.
    /// </remarks>
    private static void WriteAccount(CliContext context, MailboxAccountSynchronization account)
    {
        context.Console.WriteLine(string.Empty);

        CliDetails details = new();
        details.Add("Account", account.Account ?? "an unnamed account");
        details.Add("Phase", account.DescribePhase());
        details.Add("Backoff", account.DescribeBackoff());
        details.Add("Last run", account.LastRun?.Describe() ?? "none finished since this deployment started");

        var folders = account.Folders ?? [];

        if (folders.Count == 0)
        {
            details.Add("Folders", "none mapped, so no folder of this account is synchronized.");
            context.Console.Write(details);

            return;
        }

        context.Console.Write(details);
        context.Console.WriteLine(string.Empty);
        WriteFolders(context, folders);
    }

    /// <summary>Writes the account's folders as the two readings that only mean something together.</summary>
    /// <remarks>
    /// The progress column says how far the folder is and when it last moved; the last-run column says what happened the
    /// last time a run tried. A folder whose progress stopped a day ago and whose last turn succeeded has nothing left
    /// to fetch; one whose progress stopped a day ago and whose turns keep failing is stuck, and only the pair says
    /// which — which is why they are columns of one listing rather than two readings printed apart.
    /// </remarks>
    private static void WriteFolders(CliContext context, IReadOnlyList<MailboxFolderSynchronization> folders)
    {
        CliTable listing = new("Folder", "Progress", "Last run");

        foreach (var folder in folders)
        {
            var mirrored = folder.Mirrored ? string.Empty : " (not mirrored, so no run schedules it)";

            listing.AddRow(
                $"{folder.Alias ?? "an unnamed folder"}{mirrored}",
                folder.DescribeProgress(),
                folder.LastRun?.Describe() ?? "none since this deployment started");
        }

        context.Console.Write(listing);
    }
}
