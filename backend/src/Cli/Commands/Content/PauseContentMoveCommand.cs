// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Content;

/// <summary>Stops the move of stored content where it is, leaving everything it has already carried alone.</summary>
/// <remarks>
/// The reason it exists is that a move runs for hours against a live deployment, and an operator watching a busy
/// afternoon needs the deployment back rather than a move that finishes sooner. Nothing is cancelled and nothing is
/// undone: the pass that is running finishes the one payload it holds, and <c>content move-resume</c> continues from
/// exactly there.
/// </remarks>
internal static class PauseContentMoveCommand
{
    /// <summary>Builds the <c>content move-pause</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("move-pause", "Stop the move of stored content where it is.") { endpointOption };

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
        var run = await new AdminApiClient(transport, context.Console)
            .PauseContentMoveAsync(profile.Token, cancellationToken);

        CliDetails details = new();
        details.Add("Move", run.DescribeState());
        details.Add("Progress", run.DescribeProgress());

        context.Console.Write(details);
        context.Console.WriteLine(
            $"Set it going again with '{CliRootCommand.CommandName} content move-resume'.");

        return CliExitCode.Success;
    }
}
