// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Commands.Accounts;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Erases everything one mail account collected from the mail that arrived on it.</summary>
/// <remarks>
/// <para>
/// The way out for a user who changed their mind about collection. A collected book is one mail account's, so this
/// names the account: everything it picked up goes, what another account picked up stays, and nothing anybody wrote
/// down is in this book to go with it.
/// </para>
/// <para>
/// The book it empties is read by every user assigned that account, so this is an act over a mailbox rather than over
/// one person's records. It cannot be undone, which is why it asks first like every other irreversible command here.
/// Switching collection off for the account afterwards is a separate act in configuration, and one worth making: with
/// it still on, the book fills again from the mail that arrives next.
/// </para>
/// </remarks>
internal static class DeleteCollectedContactsCommand
{
    /// <summary>Builds the <c>contact delete-collected</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var accountOption = MailAccountOptions.Account();

        var confirmedOption = CliOptions.Confirmed("erasure");

        Command command = new(
            "delete-collected",
            "Erase everything one mail account collected from the mail that arrived on it. This cannot be undone.")
        {
            accountOption,
            confirmedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(accountOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid accountId,
        string? requestedDeployment,
        bool confirmedUpFront,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        // Asked before the book is read rather than after, unlike the erasure of one person: there is no record to show
        // first, and counting what would go would mean reading a page of people the operator is about to dispose of.
        if (!CliConfirmation.Agreed(
                context,
                confirmedUpFront,
                "There is nobody at the terminal to agree to this, and erasing what was collected cannot be undone. Pass --yes to erase without being asked.",
                "Erase everything that mail account collected? Every user assigned it reads that book, and what anybody wrote down is kept. [y/N] "))
        {
            context.Console.WriteError("Nothing was erased.");

            return CliExitCode.Failure;
        }

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var erasure = await deployment.EraseCollectedContactsAsync(
            profile.Token,
            accountId.ToString("D", CultureInfo.InvariantCulture),
            cancellationToken);

        context.Console.WriteLine(erasure.ContactsErased == 0
            ? "That mail account had collected nobody, so nothing was erased."
            : $"Erased {Describe(erasure.ContactsErased, "contact", "contacts")} that mail account had collected, and {Describe(erasure.AddressesErased, "address", "addresses")}. Nothing in MailFathom can put them back.");

        return CliExitCode.Success;
    }

    /// <summary>Counts one kind of thing invariantly, for the reason every other figure this tool prints is.</summary>
    private static string Describe(int count, string singular, string plural) => string.Create(
        CultureInfo.InvariantCulture,
        $"{count} {(count == 1 ? singular : plural)}");
}
