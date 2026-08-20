// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Jobs;

/// <summary>Returns one dead letter to the queue, to be run again after its cause was fixed.</summary>
/// <remarks>
/// <para>
/// It repeats the execution rather than enqueuing a second one: the row is the same row, so the work runs under the
/// identity it already carried. That is what makes the decision safe to take on work whose effect reaches a mailbox —
/// a handler is registered on the promise that running it twice with one payload is the same as running it once.
/// </para>
/// <para>
/// The command returns as soon as the deployment has written the decision down. Whichever worker claims the job next is
/// what runs it, so closing the terminal cannot stop the work and the command is never what carries it out.
/// </para>
/// <para>
/// A job that has stopped being dead-lettered is reported rather than treated as a failure. Two operators, or one
/// operator and a list a few minutes old, reach that ordinarily, and it means what they intended has already happened.
/// </para>
/// </remarks>
internal static class RetryJobCommand
{
    /// <summary>Builds the <c>jobs retry</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var jobOption = JobOptions.Job();

        Command command = new("retry", "Run one dead letter again, under the identity it already carries.")
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
            .RetryDeadLetteredJobAsync(profile.Token, job, cancellationToken);

        if (!recovery.WasAccepted)
        {
            context.Console.WriteError(recovery.DescribeRefusal());

            return CliExitCode.Failure;
        }

        context.Console.WriteLine(
            $"Job {job:D} is back in the queue with its attempts given back, and runs under the identity it already carried. The next worker to claim it is what runs it, so nothing further is needed here.");

        return CliExitCode.Success;
    }
}
