// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Jobs;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Jobs;

/// <summary>Reports the background work a deployment will not attempt again.</summary>
/// <remarks>
/// <para>
/// It is the only place a dead letter becomes visible without a database client, and what it prints is what the two
/// decisions beside it are taken from: the identifier a retry or a drop names, the kind of work, the identity it runs
/// under, and the classification and reason it stopped on.
/// </para>
/// <para>
/// The reading is deployment-wide by default, because "what has stopped" is one question about the instance rather
/// than one per configured mailbox. Narrowing to an account is offered for the case that produces most of them, which
/// is a credential or a server that went away for one mailbox and not the others.
/// </para>
/// <para>
/// Nothing here prints what a job points at. The payload names a message, an operator deciding whether to run the work
/// again does not need to be told which one, and a terminal is exactly the place a subject would end up in a screenshot.
/// </para>
/// </remarks>
internal static class DeadLettersCommand
{
    /// <summary>Builds the <c>jobs dead-letters</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Option<string?> typeOption = new("--type")
        {
            Description = "Report only work of this kind, by the job type's own name.",
        };

        Option<string?> accountOption = new("--account")
        {
            Description = "Report only work belonging to this account, as the deployment's configuration names it.",
        };

        Option<int?> pageSizeOption = new("--page-size")
        {
            Description = "How many jobs to read. Defaults to what the deployment serves.",
        };

        Option<string?> cursorOption = new("--cursor")
        {
            Description = "Continue from where a previous page ended, using the cursor it printed.",
        };

        Command command = new(
            "dead-letters",
            "Report the background work this deployment will not attempt again, newest first.")
        {
            typeOption,
            accountOption,
            pageSizeOption,
            cursorOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            new DeadLetteredJobQuery(
                result.GetValue(typeOption),
                result.GetValue(accountOption),
                result.GetValue(pageSizeOption),
                result.GetValue(cursorOption)),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        DeadLetteredJobQuery query,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var page = await new AdminApiClient(transport, context.Console)
            .ReadDeadLetteredJobsAsync(profile.Token, query, cancellationToken);

        if (page.Jobs is not { Count: > 0 } jobs)
        {
            context.Console.WriteLine(DescribeEmptyReading(query));

            return CliExitCode.Success;
        }

        CliTable listing = new("Stopped", "Job", "Kind", "Failed", "Work", "Queued");

        foreach (var job in jobs)
        {
            listing.AddRow(
                $"{job.DeadLetteredAt:u}",
                $"{job.Job:D}",
                job.Type ?? "an unnamed kind",
                job.DescribeFailure(),
                $"{job.Key ?? "unnamed"}{DescribeAccount(job)}",
                $"{job.EnqueuedAt:u}");
        }

        context.Console.Write(listing);
        context.Console.WriteLine(string.Empty);
        context.Console.WriteLine(
            $"Run one again with '{CliRootCommand.CommandName} jobs retry --job <id>', or write it off with '{CliRootCommand.CommandName} jobs drop --job <id>'.");

        if (page.NextCursor is { Length: > 0 } cursor)
        {
            context.Console.WriteLine($"More dead letters follow. Continue with --cursor {cursor}");
        }

        return CliExitCode.Success;
    }

    private static string DescribeAccount(DeadLetteredJobReading job) =>
        job.Account is { Length: > 0 } account ? $" for {account}" : ", belonging to no account";

    /// <summary>States that nothing was found for these filters, and what each absence usually means.</summary>
    private static string DescribeEmptyReading(DeadLetteredJobQuery query) => query switch
    {
        { Type: { Length: > 0 } type, Account: { Length: > 0 } account } =>
            $"No {type} job has stopped for {account}.",
        { Type: { Length: > 0 } type } => $"No {type} job has stopped.",
        { Account: { Length: > 0 } account } => $"No background work has stopped for {account}.",
        _ => "Nothing has dead-lettered. Every job this deployment has been given either finished or is still on its way.",
    };
}
