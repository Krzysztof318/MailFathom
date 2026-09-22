// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Accounts;

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

        if (state.Restore is { } restore)
        {
            context.Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Awaiting append:         {restore.AwaitingAppend}"));
            context.Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Awaiting state write:    {restore.AwaitingStateWrite}"));
            context.Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Unanswered appends:      {restore.UnansweredAppends}"));
            context.Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"Awaiting confirmation:   {restore.AwaitingConfirmation}"));

            WriteUnansweredAppends(context, state.Account ?? account, restore.Unanswered ?? []);
        }

        return CliExitCode.Success;
    }

    /// <summary>Names each append an operator has still to settle, and what settling one takes.</summary>
    /// <remarks>
    /// Listed rather than counted, unlike everything else here, because each one is an act somebody has to perform and
    /// a count alone names nothing to act on. What is printed is a record identity, a folder alias, and an instant —
    /// MailFathom's own words for things, and nothing derived from the message.
    /// </remarks>
    private static void WriteUnansweredAppends(
        CliContext context,
        string account,
        IReadOnlyList<MailAccountUnansweredAppend> unanswered)
    {
        if (unanswered.Count == 0)
        {
            return;
        }

        context.Console.WriteLine(string.Empty);
        context.Console.WriteLine(
            "These appends were issued and never answered. The account stays in Restoring until each is settled;");
        context.Console.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"open the folder, look for the message, and run 'account custody settle --account {ConsoleSafeText.Sanitize(account)} --record <id> --found|--missing'."));

        foreach (var append in unanswered)
        {
            context.Console.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  {append.Record}  {ConsoleSafeText.Sanitize(append.Folder ?? "unknown")}  issued {append.IssuedAt:u}"));
        }
    }
}
