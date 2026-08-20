// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Globalization;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Erases one person from the deployment's contact book, and everything the book derived from them.</summary>
/// <remarks>
/// <para>
/// The data-subject erasure path, reached by somebody who means it. It removes rather than marks and it cannot be
/// undone: the record and its addresses are gone from the database, and nothing in MailFathom can put them back.
/// </para>
/// <para>
/// Which is why it shows the record and then asks. The confirmation is the default and <c>--yes</c> is the exception,
/// exactly as it is for the other irreversible commands here, and an invocation with nobody at the terminal is told to
/// pass the flag rather than having an agreement read out of whatever was piped in.
/// </para>
/// <para>
/// A contact the book does not hold is a completed erasure rather than a failure: the state the operator asked for is
/// the state the book is in, and reporting it as an error would only tell them whether somebody had already erased that
/// person.
/// </para>
/// </remarks>
internal static class DeleteContactCommand
{
    /// <summary>Builds the <c>contact delete</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var identityOption = ContactOptions.Identity();

        var confirmedOption = CliOptions.Confirmed("erasure");

        Command command = new("delete", "Erase one person from the deployment's contact book. This cannot be undone.")
        {
            identityOption,
            confirmedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(identityOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid contactId,
        string? requestedDeployment,
        bool confirmedUpFront,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var lookup = await deployment.ReadContactAsync(profile.Token, contactId, cancellationToken);

        if (lookup.Contact is not { } held)
        {
            context.Console.WriteLine(
                $"The deployment's contact book holds no contact {contactId:D}, so nothing was erased.");

            return CliExitCode.Success;
        }

        ContactOutput.WriteContact(context.Console, held);

        if (!CliConfirmation.Agreed(
                context,
                confirmedUpFront,
                "There is nobody at the terminal to agree to this, and erasing a contact cannot be undone. Pass --yes to erase without being asked.",
                "Erase that contact and everything derived from it? [y/N] "))
        {
            context.Console.WriteError("Nothing was erased.");

            return CliExitCode.Failure;
        }

        var erasure = await deployment.EraseContactAsync(profile.Token, contactId, cancellationToken);

        context.Console.WriteLine(erasure.WasHeld
            ? $"Erased contact {erasure.Contact:D} and {DescribeAddresses(erasure.AddressesErased)}. Nothing in MailFathom can put the record back."
            : $"The deployment's contact book held no contact {erasure.Contact:D} by the time the erasure ran, so nothing was erased.");

        return CliExitCode.Success;
    }

    /// <summary>Describes how many addresses went with the person, grouped invariantly for the reason every other figure this tool prints is.</summary>
    private static string DescribeAddresses(int addressesErased) => string.Create(
        CultureInfo.InvariantCulture,
        $"{addressesErased} {(addressesErased == 1 ? "address" : "addresses")}");
}
