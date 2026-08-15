// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Mailboxes;

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
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
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

        context.Console.WriteLine($"{profile.Name} ({profile.Endpoint.GetLeftPart(UriPartial.Authority)})");
        context.Console.WriteLine($"Synchronization: {DescribeSwitch(status.SynchronizationEnabled)}");

        var accounts = status.Accounts ?? [];

        if (accounts.Count == 0)
        {
            context.Console.WriteLine("Accounts:        none configured, so this deployment fetches no mail at all.");

            return CliExitCode.Success;
        }

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

    private static void WriteAccount(CliContext context, MailboxAccountSynchronization account)
    {
        context.Console.WriteLine(string.Empty);
        context.Console.WriteLine($"{account.Account ?? "an unnamed account"}");
        context.Console.WriteLine($"  Phase:    {account.DescribePhase()}");
        context.Console.WriteLine($"  Backoff:  {account.DescribeBackoff()}");
        context.Console.WriteLine($"  Last run: {account.LastRun?.Describe() ?? "none finished since this deployment started"}");

        var folders = account.Folders ?? [];

        if (folders.Count == 0)
        {
            context.Console.WriteLine("  Folders:  none mapped, so no folder of this account is synchronized.");

            return;
        }

        context.Console.WriteLine("  Folders:");

        foreach (var folder in folders)
        {
            WriteFolder(context, folder);
        }
    }

    /// <summary>Writes one folder as the two readings that only mean something together.</summary>
    /// <remarks>
    /// The progress line says how far the folder is and when it last moved; the turn line says what happened the last
    /// time a run tried. A folder whose progress stopped a day ago and whose last turn succeeded has nothing left to
    /// fetch; one whose progress stopped a day ago and whose turns keep failing is stuck, and only the pair says which.
    /// </remarks>
    private static void WriteFolder(CliContext context, MailboxFolderSynchronization folder)
    {
        var alias = folder.Alias ?? "an unnamed folder";
        var mirrored = folder.Mirrored ? string.Empty : " (not mirrored, so no run schedules it)";

        context.Console.WriteLine($"    {alias}{mirrored}");
        context.Console.WriteLine($"      Progress: {folder.DescribeProgress()}");
        context.Console.WriteLine($"      Last run: {folder.LastRun?.Describe() ?? "none since this deployment started"}");
    }
}
