// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Mailboxes;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands;

/// <summary>Reports where one scope's re-derivation has got to, or how the last one ended.</summary>
/// <remarks>
/// Where a re-derivation started with <c>mailbox rederive</c> is watched from. The run outlives the command that asked
/// for it and is carried by the deployment's own background work, so how far it has come is a question asked repeatedly
/// rather than waited on — and a scope nobody has ever asked about is an answer rather than an error.
/// </remarks>
internal static class RederivationStatusCommand
{
    /// <summary>Builds the <c>mailbox rederive-status</c> command.</summary>
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
            "rederive-status",
            "Report where a re-derivation of the deployment's stored mail has got to.")
        {
            accountOption,
            folderOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption) ?? string.Empty,
            result.GetValue(folderOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string account,
        string? folder,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var state = await new AdminApiClient(transport, context.Console)
            .ReadMailboxRederivationAsync(profile.Token, account, folder, cancellationToken);

        var scope = RederiveMailboxCommand.Scope(account, folder);

        if (state.Run is not { } run)
        {
            context.Console.WriteLine(
                $"No re-derivation has ever been asked for over {scope}. Start one with '{CliRootCommand.CommandName} mailbox rederive --account {account}{RederiveMailboxCommand.FolderArgument(folder)}'.");

            return CliExitCode.Success;
        }

        CliDetails details = new();
        details.Add("Scope", $"{scope} — {run.DescribeState()}");
        details.Add("Requested", $"{run.RequestedAt:u}");
        details.Add("Progress", run.DescribeProgress());

        context.Console.Write(details);

        WriteSteppedOver(context, run);

        // A run that is still outstanding while nothing carries it is what a dead-lettered segment looks like from
        // here: the deployment will not attempt it again on its own, and the queue is where that decision is read and
        // reversed.
        if (run.IsOutstanding)
        {
            context.Console.WriteLine(
                $"If it stops moving, look for the work that stopped with '{CliRootCommand.CommandName} jobs dead-letters'.");
        }

        return CliExitCode.Success;
    }

    /// <summary>States what the run could not re-read, which is a fact about the mailbox rather than a failure.</summary>
    /// <remarks>
    /// The two counts stay apart because they ask the operator different questions: one is a message nobody can parse,
    /// which keeps whatever an earlier release read from it, and the other a row whose raw MIME is no longer stored,
    /// which only a fetch could bring back.
    /// </remarks>
    private static void WriteSteppedOver(CliContext context, MailboxRederivationRun run)
    {
        if (run.UnreadableEmailCount > 0)
        {
            context.Console.WriteLine(
                "Mail carrying MIME no reader could parse kept what was already recorded for it.");
        }

        if (run.MissingContentEmailCount > 0)
        {
            context.Console.WriteLine(
                $"Mail whose stored MIME is gone was stepped over, so only '{CliRootCommand.CommandName} mailbox rewind' would reach it.");
        }
    }
}
