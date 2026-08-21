// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Outbox;

/// <summary>Withdraws one queued message before it leaves the deployment.</summary>
/// <remarks>
/// <para>
/// It is the only point at which sending is reversible at all. A message whose transmission has begun may already be
/// in somebody's mailbox and cannot be taken back, so the deployment refuses to withdraw one and says so rather than
/// racing the worker that is offering it — which is what makes a success here mean the message reached nobody.
/// </para>
/// <para>
/// The record stays. What is written is the withdrawal, so what was cancelled and when is still readable afterwards,
/// and the identity the send was recorded under still stops the same trigger queuing the same message again.
/// </para>
/// </remarks>
internal static class CancelOutgoingMailCommand
{
    /// <summary>Builds the <c>outbox cancel</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var messageOption = OutboxOptions.Message();

        Command command = new("cancel", "Withdraw one queued message that has not begun transmitting.")
        {
            messageOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(messageOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid message,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var decision = await new AdminApiClient(transport, context.Console)
            .CancelOutboxSendAsync(profile.Token, message, cancellationToken);

        if (!decision.WasAccepted)
        {
            context.Console.WriteError(decision.DescribeRefusal());

            return CliExitCode.Failure;
        }

        context.Console.WriteLine(
            $"Message {message:D} is withdrawn. Nothing transmits it, no recipient of it received anything, and the record of it stays readable in the outbox.");

        return CliExitCode.Success;
    }
}
