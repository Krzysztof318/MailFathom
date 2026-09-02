// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Jobs;

/// <summary>Records that one dead letter will never be run.</summary>
/// <remarks>
/// <para>
/// It removes nothing. The row stays, keeping the identity it was enqueued under and the failure that ended it, so
/// what was dropped and why it stopped are both still readable afterwards — and the identity goes on stopping the same
/// trigger from enqueuing the same work again.
/// </para>
/// <para>
/// The decision is the operator's rather than the queue's, which is the whole reason it exists: a dead letter that
/// nothing can be done about would otherwise sit in the reading forever, and a queue whose list of stopped work never
/// shortens is one nobody reads.
/// </para>
/// </remarks>
internal static class DropJobCommand
{
    /// <summary>Builds the <c>jobs drop</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var jobOption = JobOptions.Job();

        Command command = new("drop", "Record that one dead letter will never be run, keeping the record of it.")
        {
            jobOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(jobOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid job,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var recovery = await new AdminApiClient(transport, context.Console)
            .DropDeadLetteredJobAsync(profile.Token, job, cancellationToken);

        if (!recovery.WasAccepted)
        {
            context.Console.WriteError(recovery.DescribeRefusal());

            return CliExitCode.Failure;
        }

        context.Console.WriteLine(
            $"Job {job:D} is recorded as dropped. Nothing will run it, the row keeps the failure that ended it, and the identity it was enqueued under still stops the same trigger enqueuing the work again.");

        return CliExitCode.Success;
    }
}
