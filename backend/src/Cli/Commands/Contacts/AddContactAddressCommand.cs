// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration.Contacts;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Adds one more address to a person the deployment's book already holds.</summary>
/// <remarks>
/// A convenience over the amendment beneath it rather than an operation of its own: the book takes the whole record, so
/// this reads the contact, appends the address, and sends the result. An address a different contact already holds is
/// refused by the deployment naming which contact holds it, because one address belongs to one person across the book.
/// </remarks>
internal static class AddContactAddressCommand
{
    /// <summary>Builds the <c>contact add-address</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var identityOption = ContactOptions.Identity();
        var addressOption = ContactOptions.Address("The address to add to the contact.");

        Option<string?> preferredOption = new("--preferred")
        {
            Description =
                "The address to use by default afterwards, which is the one being added or one the contact already holds.",
        };

        Command command = new("add-address", "Add one address to a contact the book already holds.")
        {
            identityOption,
            addressOption,
            preferredOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(identityOption),
            result.GetValue(addressOption) ?? string.Empty,
            result.GetValue(preferredOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static Task<int> RunAsync(
        CliContext context,
        Guid contactId,
        string address,
        string? preferred,
        string? requestedDeployment,
        CancellationToken cancellationToken) =>
        ContactRecordEdit.AmendAsync(
            context,
            contactId,
            requestedDeployment,
            held => Extend(held, address, preferred),
            "Added an address to",
            cancellationToken);

    /// <summary>Produces the record the contact is to have with the address added.</summary>
    /// <exception cref="CliFailure">Thrown when the amendment would leave the record exactly as it already is.</exception>
    /// <remarks>
    /// An amendment that changes nothing is refused here rather than sent, because the book would merge the two
    /// spellings and answer that the write succeeded — which would tell an operator that something was added when
    /// nothing was. Naming a different preferred address is still a change, so that case is sent.
    /// </remarks>
    private static ContactRecordRequest Extend(ContactRecord held, string address, string? preferred)
    {
        var addresses = ContactRecordEdit.AddressesOf(held);
        var alreadyHeld = ContactRecordEdit.Holds(addresses, address);
        var preferredAfterwards = preferred ?? held.PreferredAddress ?? address;

        if (alreadyHeld && ContactRecordEdit.Holds([held.PreferredAddress ?? address], preferredAfterwards))
        {
            throw new CliFailure(
                $"Contact {held.Id:D} already holds that address and already uses that one by default, so there was nothing to change. Pass --preferred to change which address it uses by default.");
        }

        IReadOnlyList<string> extended = alreadyHeld ? addresses : [.. addresses, address];

        return new ContactRecordRequest(held.DisplayName ?? string.Empty, extended, preferredAfterwards, held.Note);
    }
}
