// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>Asks for an account's mailbox to be held by MailFathom, or mirrored from its source again.</summary>
/// <remarks>
/// <para>
/// The one command here that ends with a mail server no longer holding a copy of somebody's mailbox. It is asked for
/// once and then happens over hours: the account's own runs empty the source, message by message, and only ever of
/// what MailFathom has stored, read back, and matched against what it recorded.
/// </para>
/// <para>
/// So it is confirmed rather than taken at once, and the sentence it asks names the consequence rather than the
/// setting. Nothing undoes a drained message, and switching back does not put one on the source again.
/// </para>
/// </remarks>
internal static class SwitchMailAccountCustodyCommand
{
    /// <summary>Builds the <c>account custody switch</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();
        var confirmedOption = CliOptions.Confirmed("custody switch");

        Option<string> toOption = new("--to")
        {
            Description = $"The custody to ask for: {MailAccountCustodies.HoldMailbox} or {MailAccountCustodies.MirrorSource}.",
            Required = true,
        };

        Command command = new(
            "switch",
            "Ask for an account's mailbox to be held by MailFathom, or mirrored from its source again.")
        {
            accountOption,
            toOption,
            confirmedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => SwitchAsync(
            context,
            result.GetValue(accountOption) ?? string.Empty,
            result.GetValue(toOption) ?? string.Empty,
            result.GetValue(confirmedOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> SwitchAsync(
        CliContext context,
        string account,
        string custody,
        bool confirmedUpFront,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        if (MailAccountCustodies.Normalize(custody) is not { } requested)
        {
            context.Console.WriteError(
                $"'{ConsoleSafeText.Sanitize(custody)}' is not a custody. Name {MailAccountCustodies.HoldMailbox} or {MailAccountCustodies.MirrorSource}.");

            return CliExitCode.Failure;
        }

        if (!CliConfirmation.Agreed(
            context,
            confirmedUpFront,
            "Switching an account's custody was not confirmed, and nothing was changed.",
            QuestionFor(requested, account)))
        {
            return CliExitCode.Failure;
        }

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var outcome = await deployment.SwitchMailAccountCustodyAsync(
            profile.Token,
            account,
            requested,
            cancellationToken);

        if (!outcome.WasAccepted)
        {
            context.Console.WriteError("The deployment refused the switch, and nothing was changed:");

            foreach (var refusal in outcome.Refusals ?? [])
            {
                context.Console.WriteError($"  {ConsoleSafeText.Sanitize(refusal)}");
            }

            return CliExitCode.Failure;
        }

        context.Console.WriteLine(requested == MailAccountCustodies.HoldMailbox
            ? $"{ConsoleSafeText.Sanitize(outcome.Account ?? account)} is now asked to hold its mailbox. Its own runs empty the source of what MailFathom has stored and verified, a bounded batch at a time; 'account custody show' reports how far that has got."
            : $"{ConsoleSafeText.Sanitize(outcome.Account ?? account)} is now asked to mirror its source again. The drain stops and the account moves to Restoring; what the source no longer holds stays with MailFathom.");

        return CliExitCode.Success;
    }

    /// <summary>Asks the question the answer is actually about, which is what happens to the mail rather than to a field.</summary>
    private static string QuestionFor(string requested, string account) =>
        requested == MailAccountCustodies.HoldMailbox
            ? $"Hold the mailbox of '{ConsoleSafeText.Sanitize(account)}'? Its source server will be emptied of every message MailFathom has stored and verified, and nothing puts those copies back."
            : $"Mirror the source of '{ConsoleSafeText.Sanitize(account)}' again? The drain stops, but nothing the drain already removed returns to the source server.";
}
