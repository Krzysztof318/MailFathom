// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Commands.Users;

namespace MailFathom.Cli.Commands.Contacts;

/// <summary>Shows everything the deployment's book holds about one person.</summary>
/// <remarks>
/// The two ways an operator arrives at a contact. By identity is how every other command names one; by address is the
/// question "who is this from", which is answered with one person rather than with a match: an address is unique within
/// one book, and where two of the books a user reads hold it, the precedence between them settles which record answers.
/// </remarks>
internal static class ShowContactCommand
{
    /// <summary>Builds the <c>contact show</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var userOption = UserOptions.User();

        Option<Guid?> identityOption = new("--id")
        {
            Description = "The contact to show, by the identifier the deployment's book gave it.",
        };

        Option<string?> addressOption = new("--address")
        {
            Description = "Show whoever uses this address, in whichever casing the book recorded it.",
        };

        Command command = new("show", "Show one contact, by its identifier or by an address it holds.")
        {
            userOption,
            identityOption,
            addressOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(userOption),
            result.GetValue(identityOption),
            result.GetValue(addressOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedUser,
        Guid? contactId,
        string? address,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var byIdentity = contactId is not null;
        var byAddress = address is { Length: > 0 };

        if (byIdentity == byAddress)
        {
            throw new CliFailure(
                "Name the contact one way or the other: --id for the identifier the book gave it, or --address for whoever uses an address.");
        }

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var user = await UserOptions.ResolveUserAsync(deployment, profile.Token, requestedUser, cancellationToken);

        var lookup = contactId is { } identity
            ? await deployment.ReadContactAsync(profile.Token, identity, user, cancellationToken)
            : await deployment.ReadContactByAddressAsync(profile.Token, user, address!, cancellationToken);

        if (lookup.Contact is not { } held)
        {
            context.Console.WriteError(DescribeAbsence(contactId));

            return CliExitCode.Failure;
        }

        ContactOutput.WriteContact(context.Console, held);

        return CliExitCode.Success;
    }

    /// <summary>States that the book holds nobody matching what was asked.</summary>
    /// <remarks>
    /// The address the operator asked with is not repeated back. It is somebody's address whether or not the book holds
    /// them, and this sentence goes to standard error, which is where a scripted invocation's output is captured; the
    /// operator has the address in front of them either way.
    /// </remarks>
    private static string DescribeAbsence(Guid? contactId) => contactId is { } identity
        ? $"The books that user reads hold no contact {identity:D}."
        : "The books that user reads hold nobody using that address.";
}
