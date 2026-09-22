// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Accounts;

/// <summary>Records what an operator found in the folder an unanswered restore append was issued against.</summary>
/// <remarks>
/// <para>
/// The one act of the restore that only a person can perform. Putting a held mailbox back is an <c>APPEND</c> per
/// message, and an <c>APPEND</c> whose answer never came back leaves a folder that may hold the copy and may not —
/// nothing the folder shows afterwards tells a copy MailFathom appended apart from one somebody else put there. So
/// MailFathom refuses to guess: it records the append, never issues it again, and holds the account in
/// <c>Restoring</c> until this command says what is actually in the folder.
/// </para>
/// <para>
/// <c>account custody show</c> names each record and the folder it was issued against. Open that folder in a mail
/// client, look for the message, and answer with what is there: <c>--found</c> where the copy is in the folder, and
/// <c>--missing</c> where it is not, which puts the message back on the next run.
/// </para>
/// </remarks>
internal static class SettleMailAccountRestoreAppendCommand
{
    /// <summary>Builds the <c>account custody settle</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = CliOptions.MailAccount();

        Option<Guid> recordOption = new("--record")
        {
            Description = "The append record to settle, as 'account custody show' names it.",
            Required = true,
        };

        Option<bool> foundOption = new("--found")
        {
            Description = "The folder holds the copy the append may have put there.",
        };

        Option<bool> missingOption = new("--missing")
        {
            Description = "The folder does not hold it, so the message is appended again on the next run.",
        };

        Command command = new(
            "settle",
            "Say whether the folder holds the copy an unanswered restore append may have put there.")
        {
            accountOption,
            recordOption,
            foundOption,
            missingOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => SettleAsync(
            context,
            result.GetValue(accountOption) ?? string.Empty,
            result.GetValue(recordOption),
            result.GetValue(foundOption),
            result.GetValue(missingOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> SettleAsync(
        CliContext context,
        string account,
        Guid record,
        bool found,
        bool missing,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        // Two switches rather than one with a value, and neither defaulting, because the two verdicts do opposite
        // things to somebody's mailbox: one leaves a copy where it is and the other puts a second message into the
        // folder if the first was wrong. A command that guessed either would be the guess this whole record exists to
        // refuse.
        if (found == missing)
        {
            context.Console.WriteError(found
                ? "Name either --found or --missing, not both. They are opposite answers to what the folder holds."
                : "Name whether the folder holds the copy: --found, or --missing to have the message appended again.");

            return CliExitCode.Failure;
        }

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var outcome = await deployment.SettleMailAccountRestoreAppendAsync(
            profile.Token,
            account,
            record,
            found,
            cancellationToken);

        if (!outcome.WasSettled)
        {
            context.Console.WriteError(
                "No append of that account is standing under that record. It may have been settled already, or the source may have answered the append after the reading that named it.");

            return CliExitCode.Failure;
        }

        context.Console.WriteLine(found
            ? $"Recorded that the source of {ConsoleSafeText.Sanitize(outcome.Account ?? account)} holds that copy. The message keeps no occurrence, and the restore will not append it again."
            : $"Recorded that the source of {ConsoleSafeText.Sanitize(outcome.Account ?? account)} does not hold that copy. The message is appended again on the account's next run.");

        return CliExitCode.Success;
    }
}
