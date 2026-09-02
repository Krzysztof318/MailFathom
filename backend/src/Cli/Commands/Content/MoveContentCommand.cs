// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Content;

/// <summary>Asks the deployment to carry the mail content its database still holds into the object backend.</summary>
/// <remarks>
/// <para>
/// Selecting the object backend decides where the next message is written and leaves everything already stored where it
/// was, which for a mailbox synchronized over a year is all of it. This is what moves that: bounded background passes
/// that copy each payload, verify it against what the row records, and only then point the row at the object.
/// </para>
/// <para>
/// The command returns as soon as the move is written down. The deployment carries it, so closing the terminal stops
/// nothing, and <c>content move-status</c> is where it is watched.
/// </para>
/// </remarks>
internal static class MoveContentCommand
{
    /// <summary>Builds the <c>content move</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var confirmedOption = CliOptions.Confirmed("move");

        Command command = new(
            "move",
            "Start carrying the mail content held in the database into the configured object backend.")
        {
            endpointOption,
            confirmedOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        bool confirmedUpFront,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var report = await deployment.ReadContentMoveAsync(profile.Token, cancellationToken);

        CliDetails backlog = new();
        backlog.Add("To move", report.DescribeBacklog());

        context.Console.Write(backlog);

        // Reported on standard error and with a failing code, which is what every other command does when it did not do
        // what it was asked. A caller that redirected the output reads an empty result and a reason rather than a
        // sentence about nothing happening mixed into what it captured.
        if (!Agreed(context, confirmedUpFront))
        {
            context.Console.WriteError("Nothing was moved.");

            return CliExitCode.Failure;
        }

        var run = await deployment.StartContentMoveAsync(profile.Token, cancellationToken);

        CliDetails details = new();
        details.Add("Move", run.DescribeState());
        details.Add("Progress", run.DescribeProgress());

        context.Console.Write(details);
        context.Console.WriteLine(
            $"Watch it with '{CliRootCommand.CommandName} content move-status', and stop it with '{CliRootCommand.CommandName} content move-pause'.");

        return CliExitCode.Success;
    }

    /// <summary>Reports whether the person running this agreed to the move, refusing to guess where nobody can answer.</summary>
    /// <remarks>
    /// Asked even where the backlog is empty, and for the reason the rewind asks about an empty scope: the figure
    /// informs the question rather than answering it, and a deployment that stores everything in the bucket already is
    /// one where the move costs nothing and the agreement costs a keystroke.
    /// </remarks>
    private static bool Agreed(CliContext context, bool confirmedUpFront) => CliConfirmation.Agreed(
        context,
        confirmedUpFront,
        "There is nobody at the terminal to agree to this, and moving rewrites where the deployment holds its mail. Pass --yes to move without being asked.",
        "Move that content into the object backend? [y/N] ");
}
