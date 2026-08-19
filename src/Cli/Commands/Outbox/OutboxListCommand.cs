// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Outbox;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Outbox;

/// <summary>Lists what a deployment has been asked to send, newest first.</summary>
/// <remarks>
/// <para>
/// It is where an operator learns about a delivery problem from their own instance rather than from a recipient. What
/// it prints is what the two decisions beside it are taken from: the identifier they name, the stage the send stands
/// at, how many attempts it has had, and the codes that say what the last one ended in.
/// </para>
/// <para>
/// It names no recipient and no subject. A page of an outbox is a page of who this owner writes to and when, and a
/// terminal is exactly the place such a page would end up in a screenshot; who one particular message was for is read
/// with <c>outbox show</c>, which answers about one send somebody already has in front of them.
/// </para>
/// </remarks>
internal static class OutboxListCommand
{
    /// <summary>Builds the <c>outbox list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = OutboxOptions.Account();

        Option<string?> stageOption = new("--stage")
        {
            Description = "Report only the sends standing at this stage, by the stage's own name.",
        };

        Option<int?> pageSizeOption = new("--page-size")
        {
            Description = "How many messages to read. Defaults to what the deployment serves.",
        };

        Option<string?> cursorOption = new("--cursor")
        {
            Description = "Continue from where a previous page ended, using the cursor it printed.",
        };

        Command command = new("list", "Report what this deployment has been asked to send, newest first.")
        {
            accountOption,
            stageOption,
            pageSizeOption,
            cursorOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            new OutboxQuery(
                result.GetValue(accountOption),
                result.GetValue(stageOption),
                result.GetValue(pageSizeOption),
                result.GetValue(cursorOption)),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        OutboxQuery query,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var page = await new AdminApiClient(transport, context.Console)
            .ReadOutboxAsync(profile.Token, query, cancellationToken);

        if (page.Sends is not { Count: > 0 } sends)
        {
            context.Console.WriteLine(DescribeEmptyReading(query));

            return CliExitCode.Success;
        }

        CliTable listing = new("Recorded", "Message", "Account", "Stage", "Attempts", "Failed", "Due");

        foreach (var send in sends)
        {
            listing.AddRow(
                $"{send.RecordedAt:u}",
                $"{send.OutgoingEmail:D}",
                send.Account ?? "an unnamed account",
                send.Stage ?? "an unnamed stage",
                send.AttemptCount.ToString(CultureInfo.InvariantCulture),
                send.DescribeFailure(),
                $"{send.AvailableAt:u}");
        }

        context.Console.Write(listing);
        context.Console.WriteLine(string.Empty);

        if (sends.Any(send => send.HasUnknownOutcome))
        {
            context.Console.WriteLine(
                $"A message at {OutboxEntryReading.UnknownOutcomeStage} went out and its server never answered, so whether its recipients received it is unknown. Nothing transmits it again on its own; read it with '{CliRootCommand.CommandName} outbox show --message <id>' and decide.");
        }

        context.Console.WriteLine(
            $"Withdraw one that has not left with '{CliRootCommand.CommandName} outbox cancel --message <id>', or offer one again with '{CliRootCommand.CommandName} outbox requeue --message <id>'.");

        if (page.NextCursor is { Length: > 0 } cursor)
        {
            context.Console.WriteLine($"More messages follow. Continue with --cursor {cursor}");
        }

        return CliExitCode.Success;
    }

    /// <summary>States that nothing was found for these filters, and what each absence usually means.</summary>
    private static string DescribeEmptyReading(OutboxQuery query) => query switch
    {
        { Stage: { Length: > 0 } stage, Account: { Length: > 0 } account } =>
            $"No message stands at {stage} for {account}.",
        { Stage: { Length: > 0 } stage } => $"No message stands at {stage}.",
        { Account: { Length: > 0 } account } => $"This deployment has been asked to send nothing for {account}.",
        _ => "This deployment has been asked to send nothing. Nothing is queued, and nothing has been sent from it.",
    };
}
