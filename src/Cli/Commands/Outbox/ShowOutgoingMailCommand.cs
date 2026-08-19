// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Outbox;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Outbox;

/// <summary>Shows one queued message, and what each of the people it is addressed to was told.</summary>
/// <remarks>
/// <para>
/// It is the one outbox reading that names people, and it is bounded to one message for that reason: an operator
/// deciding whether to send a message again has to know who may already have received it, and nobody deciding that
/// needs a page of everybody this deployment has ever written to.
/// </para>
/// <para>
/// It shows no subject and no body. What the message says is not what the decision turns on, and MailFathom does not
/// serve the stored MIME of an outgoing message on any surface at all.
/// </para>
/// </remarks>
internal static class ShowOutgoingMailCommand
{
    /// <summary>Builds the <c>outbox show</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var messageOption = OutboxOptions.Message();

        Command command = new("show", "Show one queued message, its recipients, and what each of them was told.")
        {
            messageOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(messageOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
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
        var send = await new AdminApiClient(transport, context.Console)
            .ReadOutboxSendAsync(profile.Token, message, cancellationToken);

        CliDetails details = new();
        details.Add("Message", $"{send.OutgoingEmail:D}");
        details.Add("Account", send.Account ?? "an unnamed account");
        details.Add("Stage", send.Stage ?? "an unnamed stage");
        details.Add("Asked by", $"{send.Origin ?? "an unnamed origin"} {send.Requester ?? "with no identity"}");
        details.Add("Attempts", send.AttemptCount.ToString(CultureInfo.InvariantCulture));
        details.Add("Size", $"{send.MimeByteLength.ToString(CultureInfo.InvariantCulture)} byte(s)");
        details.Add("Recorded", $"{send.RecordedAt:u}");
        details.Add("Stage since", $"{send.StageChangedAt:u}");
        details.Add("Due", $"{send.AvailableAt:u}");
        details.Add("Failed", DescribeFailure(send));
        details.Add("Recipients", DescribeRecipients(send));

        context.Console.Write(details);

        if (string.Equals(send.Stage, OutboxEntryReading.UnknownOutcomeStage, StringComparison.Ordinal))
        {
            context.Console.WriteLine(string.Empty);
            context.Console.WriteLine(
                $"This message went out and its submission server never answered, so whether its recipients received it is unknown. Nothing transmits it again on its own: offering it again with '{CliRootCommand.CommandName} outbox requeue --message {send.OutgoingEmail:D}' may put a second copy in a mailbox that already has one.");
        }

        return CliExitCode.Success;
    }

    /// <summary>Describes what the last attempt ended in, as the codes an operator looks up.</summary>
    private static string DescribeFailure(OutboxSend send) => (send.LastFailureCode, send.LastReplyCode) switch
    {
        (null, null) => "none recorded",
        ({ } failure, null) => string.Create(CultureInfo.InvariantCulture, $"failure {failure}"),
        (null, { } reply) => string.Create(CultureInfo.InvariantCulture, $"reply {reply}"),
        ({ } failure, { } reply) => string.Create(
            CultureInfo.InvariantCulture,
            $"failure {failure}, reply {reply}"),
    };

    /// <summary>Writes one line per recipient: the address, its role, and what the server said about it.</summary>
    /// <remarks>
    /// The status leads the reply code because it is what decides the operator's next move: an accepted address is one
    /// a later attempt never offers again, so a message offered again reaches only the people still outstanding.
    /// </remarks>
    private static IReadOnlyList<string> DescribeRecipients(OutboxSend send) =>
        send.Recipients is not { Count: > 0 } recipients
            ? ["none recorded"]
            : [.. recipients.Select(DescribeRecipient)];

    private static string DescribeRecipient(OutboxRecipientReading recipient)
    {
        var reply = recipient.LastReplyCode is { } replyCode
            ? string.Create(CultureInfo.InvariantCulture, $", reply {replyCode}")
            : string.Empty;

        return $"{recipient.Address ?? "an unnamed address"} [{recipient.Role ?? "unnamed role"}, {recipient.Status ?? "unreported"}{reply}]";
    }
}
