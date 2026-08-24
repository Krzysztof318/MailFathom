// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Content;

/// <summary>Sets a stopped move of stored content going again, from the position it stopped at.</summary>
/// <remarks>
/// It asks for no confirmation, because it resumes a move an operator already agreed to rather than starting one: what
/// it changes is when the remaining payloads are carried, never which payloads those are.
/// </remarks>
internal static class ResumeContentMoveCommand
{
    /// <summary>Builds the <c>content move-resume</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new(
            "move-resume",
            "Set a stopped move of stored content going again from where it stopped.")
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
        var run = await new AdminApiClient(transport, context.Console)
            .ResumeContentMoveAsync(profile.Token, cancellationToken);

        CliDetails details = new();
        details.Add("Move", run.DescribeState());
        details.Add("Progress", run.DescribeProgress());

        context.Console.Write(details);

        return CliExitCode.Success;
    }
}
