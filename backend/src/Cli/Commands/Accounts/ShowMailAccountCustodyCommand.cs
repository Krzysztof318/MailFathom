// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>Reports which copy of an account's mailbox is the truth, and how far a switch under way has got.</summary>
/// <remarks>
/// The only view of the drain there is, which is why it prints counts rather than a progress bar: a drained message
/// looks exactly like one that was never on the source, so what an operator can be told is how much is still to go and
/// what the gate is holding back.
/// </remarks>
internal static class ShowMailAccountCustodyCommand
{
    /// <summary>Builds the <c>account custody show</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();

        Command command = new(
            "show",
            "Report which copy of an account's mailbox is the truth, and how far a switch has got.")
        {
            accountOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => ShowAsync(
            context,
            result.GetValue(accountOption) ?? string.Empty,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> ShowAsync(
        CliContext context,
        string account,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var state = await deployment.ReadMailAccountCustodyAsync(profile.Token, account, cancellationToken);

        context.Console.WriteLine($"Account:   {ConsoleSafeText.Sanitize(state.Account ?? account)}");
        context.Console.WriteLine($"Requested: {ConsoleSafeText.Sanitize(state.Requested ?? "unknown")}");
        context.Console.WriteLine($"Phase:     {ConsoleSafeText.Sanitize(state.Phase ?? "unknown")}");

        if (state.IsSwitchPending)
        {
            context.Console.WriteLine("A switch is under way; the account's own runs are carrying it.");
        }

        if (state.Drain is { } standing)
        {
            context.Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Awaiting drain:          {standing.AwaitingDrain}"));
            context.Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Held back, above limit:  {standing.HeldBackAboveSizeLimit}"));
            context.Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Held back, no headroom:  {standing.HeldBackAwaitingHeadroom}"));
            context.Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Awaiting source removal: {standing.AwaitingSourceRemoval}"));
        }

        return CliExitCode.Success;
    }
}
