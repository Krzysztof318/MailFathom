// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration.Contacts;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Takes one address off a person the deployment's book holds.</summary>
/// <remarks>
/// The address is removed from the record rather than marked, which is also what frees it for another contact to claim.
/// A contact holds at least one address, so the last one cannot be removed: what removes a person from the book is
/// <c>contact delete</c>, and it is a different act with a different answer.
/// </remarks>
internal static class RemoveContactAddressCommand
{
    /// <summary>Builds the <c>contact remove-address</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var identityOption = ContactOptions.Identity();
        var addressOption = ContactOptions.Address("The address to take off the contact.");

        Option<string?> preferredOption = new("--preferred")
        {
            Description =
                "The address to use by default afterwards. Required when the one being removed is the preferred one.",
        };

        Command command = new("remove-address", "Take one address off a contact the book holds.")
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
            held => Without(held, address, preferred),
            "Took an address off",
            cancellationToken);

    /// <summary>Produces the record the contact is to have with the address gone.</summary>
    /// <exception cref="CliFailure">Thrown when the contact does not hold that address, when it is the only one, or when it is the preferred one and no replacement was named.</exception>
    private static ContactRecordRequest Without(ContactRecord held, string address, string? preferred)
    {
        var addresses = ContactRecordEdit.AddressesOf(held);

        if (!ContactRecordEdit.Holds(addresses, address))
        {
            throw new CliFailure(
                $"Contact {held.Id:D} does not hold that address, so there was nothing to remove.");
        }

        if (addresses.Count == 1)
        {
            throw new CliFailure(
                $"That is the only address contact {held.Id:D} holds, and a contact holds at least one. Erase the contact with 'mfctl contact delete' if that is what you meant.");
        }

        IReadOnlyList<string> remaining =
            [.. addresses.Where(kept => !string.Equals(kept, address, StringComparison.OrdinalIgnoreCase))];

        return new ContactRecordRequest(
            held.DisplayName ?? string.Empty,
            remaining,
            ChoosePreferred(held, remaining, preferred),
            held.Note);
    }

    /// <summary>Reports which address the record is to prefer once the removal is applied.</summary>
    /// <remarks>
    /// Removing an address the contact does not prefer leaves the preference where it was. Removing the preferred one
    /// leaves the record without a default, and picking the next address in the list would decide on the operator's
    /// behalf which address a message to that person goes to — so it is named or the removal is refused.
    /// </remarks>
    private static string ChoosePreferred(ContactRecord held, IReadOnlyList<string> remaining, string? preferred)
    {
        if (preferred is { Length: > 0 } named)
        {
            return named;
        }

        if (held.PreferredAddress is { Length: > 0 } kept && ContactRecordEdit.Holds(remaining, kept))
        {
            return kept;
        }

        throw new CliFailure(
            $"That is the address contact {held.Id:D} uses by default, so removing it has to say which of the rest to use instead. Pass --preferred naming one of them.");
    }
}
