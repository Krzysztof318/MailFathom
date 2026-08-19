// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Outbox;
using MailFathom.Cli.Output;

namespace MailFathom.Cli.Commands.Outbox;

/// <summary>Reports how much a deployment has standing at each stage of its outbox.</summary>
/// <remarks>
/// <para>
/// It is the first question asked of an outbox and the cheapest: one figure per stage says whether mail is leaving,
/// whether anything is piling up, and whether anything is stuck in the one state that waits for a person. What to do
/// about any of it is read with the listing beside this.
/// </para>
/// <para>
/// Nothing here names a message, a recipient, or a subject. A count is a fact about the deployment, which is what makes
/// this the one outbox reading that is safe to leave on a screen.
/// </para>
/// </remarks>
internal static class OutboxStatusCommand
{
    /// <summary>Builds the <c>outbox status</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = OutboxOptions.Account();

        Command command = new("status", "Report how many queued messages stand at each stage of the outbox.")
        {
            accountOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? account,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var status = await new AdminApiClient(transport, context.Console)
            .ReadOutboxStatusAsync(profile.Token, account, cancellationToken);

        CliTable listing = new("Stage", "Messages");

        foreach (var stage in status.Stages ?? [])
        {
            listing.AddRow(
                stage.Stage ?? "an unnamed stage",
                stage.Count.ToString(CultureInfo.InvariantCulture));
        }

        context.Console.Write(listing);
        context.Console.WriteLine(string.Empty);
        context.Console.WriteLine(DescribeOutstanding(status));

        return CliExitCode.Success;
    }

    /// <summary>Says what the depth means, and names the command that shows which messages it is.</summary>
    /// <remarks>
    /// An empty outbox is the ordinary state of a healthy instance and says so, rather than leaving an operator to read
    /// a table of zeros and work it out.
    /// </remarks>
    private static string DescribeOutstanding(OutboxStatus status) => status.OutstandingCount == 0
        ? "Nothing is waiting. Every message this deployment was asked to send has either left or been settled."
        : $"{status.OutstandingCount} message(s) are still waiting. See which with '{CliRootCommand.CommandName} outbox list'.";
}
