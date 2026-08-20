// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Outbox;

/// <summary>Offers one queued message again, which is the decision the deployment deliberately will not take on its own.</summary>
/// <remarks>
/// <para>
/// One message and never a set. A send whose submission server never answered may already be in a mailbox, so offering
/// it again may put a second copy there — that is a judgement about that one message, and a command that could re-queue
/// a filtered selection would be a way to send an unknown number of duplicates in one act.
/// </para>
/// <para>
/// A permanently refused message needs the refusal restated. What the record says is that a server will not take it,
/// and offering it again is a decision to disbelieve that rather than a retry, so the deployment refuses until the
/// operator says the word.
/// </para>
/// <para>
/// The command returns as soon as the deployment has written the decision down. Whichever delivery pass claims the
/// message next is what transmits it, so closing the terminal cannot stop the send and the command is never what
/// carries it out.
/// </para>
/// </remarks>
internal static class RequeueOutgoingMailCommand
{
    /// <summary>Builds the <c>outbox requeue</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var messageOption = OutboxOptions.Message();

        Option<bool> despiteRefusalOption = new(OutboxOptions.RefusalRestatedFlag)
        {
            Description =
                "Offer the message again even though it was permanently refused, restating that refusal deliberately.",
        };

        Command command = new(
            "requeue",
            "Offer one queued message again, whether its outcome is unknown or it failed and is waiting.")
        {
            messageOption,
            despiteRefusalOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(messageOption),
            result.GetValue(despiteRefusalOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid message,
        bool refusalRestated,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var decision = await new AdminApiClient(transport, context.Console)
            .RequeueOutboxSendAsync(profile.Token, message, refusalRestated, cancellationToken);

        if (!decision.WasAccepted)
        {
            context.Console.WriteError(decision.DescribeRefusal(OutboxOptions.RefusalRestatedFlag));

            return CliExitCode.Failure;
        }

        context.Console.WriteLine(
            $"Message {message:D} is queued again with its attempts given back. The next delivery pass transmits it, to the addresses still outstanding and to no others, so nobody the server already accepted it for receives it twice.");

        return CliExitCode.Success;
    }
}
