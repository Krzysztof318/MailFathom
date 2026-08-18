// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Erases every contact the deployment collected from arriving mail, leaving the ones you wrote down.</summary>
/// <remarks>
/// <para>
/// The way out for an owner who changed their mind about collection. Everything collection produced is a contact of its
/// own origin, so taking that origin out takes the whole of what an instance inferred and nothing of what its owner
/// entered.
/// </para>
/// <para>
/// It cannot be undone, which is why it asks first like every other irreversible command here. Switching collection off
/// afterwards is a separate act in configuration, and one worth making: with it still on, the book fills again from the
/// mail that arrives next.
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

        Option<bool> confirmedOption = new("--yes", "-y")
        {
            Description = "Agree to the erasure without being asked, which is what a scripted erasure needs.",
        };

        Command command = new(
            "delete-collected",
            "Erase every contact the deployment collected from arriving mail, keeping the ones you entered. This cannot be undone.")
        {
            confirmedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        bool confirmedUpFront,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        if (!Agreed(context, confirmedUpFront))
        {
            context.Console.WriteError("Nothing was erased.");

            return CliExitCode.Failure;
        }

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var erasure = await deployment.EraseCollectedContactsAsync(profile.Token, cancellationToken);

        context.Console.WriteLine(erasure.ContactsErased == 0
            ? "The deployment had collected nobody, so nothing was erased."
            : $"Erased {Describe(erasure.ContactsErased, "contact", "contacts")} the deployment had collected, and {Describe(erasure.AddressesErased, "address", "addresses")}. Nothing in MailFathom can put them back.");

        return CliExitCode.Success;
    }

    /// <summary>Reports whether the person running this agreed to the erasure, refusing to guess where nobody can answer.</summary>
    /// <remarks>
    /// Asked before the book is read rather than after, unlike the erasure of one person: there is no record to show
    /// first, and counting what would go would mean reading a page of people the operator is about to dispose of.
    /// </remarks>
    private static bool Agreed(CliContext context, bool confirmedUpFront)
    {
        if (confirmedUpFront)
        {
            return true;
        }

        if (!context.Console.CanConfirm)
        {
            throw new CliFailure(
                "There is nobody at the terminal to agree to this, and erasing what was collected cannot be undone. Pass --yes to erase without being asked.");
        }

        return context.Console.Confirm(
            "Erase every contact this deployment collected from arriving mail? The ones you entered are kept. [y/N] ");
    }

    /// <summary>Counts one kind of thing invariantly, for the reason every other figure this tool prints is.</summary>
    private static string Describe(int count, string singular, string plural) => string.Create(
        CultureInfo.InvariantCulture,
        $"{count} {(count == 1 ? singular : plural)}");
}
