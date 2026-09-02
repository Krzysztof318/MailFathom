// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Content;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Content;

/// <summary>Reports where the move of stored content has got to, and how much the database still holds.</summary>
/// <remarks>
/// Where a move started with <c>content move</c> is watched from. It answers on a deployment that has never been asked
/// for one as well, because the backlog is what an operator weighs before selecting the object backend at all: how many
/// payloads are in the database, and how many bytes of backup they are costing.
/// </remarks>
internal static class ContentMoveStatusCommand
{
    /// <summary>Builds the <c>content move-status</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new(
            "move-status",
            "Report where the move of stored content has got to, and what the database still holds.")
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
        var deployment = new AdminApiClient(transport, context.Console);

        var report = await deployment.ReadContentMoveAsync(profile.Token, cancellationToken);

        // Two requests, because the copies a finished move left behind are a different act under a different grant and
        // therefore a route of their own. They are read here all the same: what an operator asks this command is where
        // their mail is held, and a deployment holding all of it twice has not finished answering that with the backlog.
        var retained = await deployment.ReadContentReleaseAsync(profile.Token, cancellationToken);

        CliDetails details = new();
        details.Add("In the database", report.DescribeBacklog());
        details.Add("Copied and still duplicated", retained.DescribeRetained());

        if (report.Run is { } run)
        {
            details.Add("Move", run.DescribeState());
            details.Add("Requested", $"{run.RequestedAt:u}");
            details.Add("Progress", run.DescribeProgress());
        }

        context.Console.Write(details);

        WriteWhatToDoNext(context, report);

        if (retained.PayloadsRemain && report.RemainingPayloadCount == 0)
        {
            context.Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"The database still holds a copy of {retained.RetainedPayloadCount:N0} of the payloads the move carried, which is what a read falls back to while the object backend is being trusted. Free them with '{CliRootCommand.CommandName} content release' once you are satisfied it can be — that step cannot be undone."));
        }

        return CliExitCode.Success;
    }

    /// <summary>Says what the operator's next act is, which differs for each of the four answers this can give.</summary>
    /// <remarks>
    /// The refused payloads are named rather than left in a count, because they are the one part of a finished move that
    /// still asks something of somebody: each is a message still held in the database that a copy could not be vouched
    /// for, and a further move is what reaches them once the reason has been repaired.
    /// </remarks>
    private static void WriteWhatToDoNext(CliContext context, ContentMoveReport report)
    {
        if (!report.Available)
        {
            context.Console.WriteLine(
                "This deployment names no object-storage endpoint, so its content stays in the database until one is configured and selected.");

            return;
        }

        if (report.Run is not { } run)
        {
            context.Console.WriteLine(
                $"No move has been asked for. Start one with '{CliRootCommand.CommandName} content move'.");

            return;
        }

        if (run.FailedPayloadCount > 0)
        {
            context.Console.WriteLine(
                "Payloads a copy could not be verified against their own row were left in the database; the deployment's logs and the move's refusal counters say which reason each was left for.");
        }

        if (run.State is ContentMoveRun.PausedName)
        {
            context.Console.WriteLine(
                $"It is stopped. Set it going again with '{CliRootCommand.CommandName} content move-resume'.");
        }
        else if (run.State is ContentMoveRun.CompletedName && report.RemainingPayloadCount > 0)
        {
            context.Console.WriteLine(
                $"The move finished with content still in the database. Asking for another with '{CliRootCommand.CommandName} content move' walks what it left behind.");
        }
    }
}
