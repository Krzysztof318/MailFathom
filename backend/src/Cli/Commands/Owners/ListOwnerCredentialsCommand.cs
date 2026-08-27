// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Reads the usernames and passwords one owner can sign in with.</summary>
/// <remarks>
/// What an administrator answers "who can reach this mailbox" from, and the listing every other command here takes its
/// identifiers out of. It reports which credentials exist, whether each still works, and how old its password is — each
/// a fact about the record rather than about the secret, so the listing is safe to print, capture, and keep.
/// </remarks>
internal static class ListOwnerCredentialsCommand
{
    /// <summary>Builds the <c>credential list</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var ownerOption = OwnerCredentialOptions.Owner();

        Command command = new("list", "Read the credentials one owner signs in with.")
        {
            ownerOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(ownerOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid? requestedOwner,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var owner = await OwnerCredentialOptions.ResolveOwnerAsync(
            deployment,
            profile.Token,
            requestedOwner,
            cancellationToken);

        var listing = await deployment.ReadOwnerCredentialsAsync(profile.Token, owner, cancellationToken);

        if (listing.Credentials is not { Count: > 0 } credentials)
        {
            context.Console.WriteLine(
                $"Owner {owner:D} holds no username-and-password credentials. Nothing provisions one on its own, so "
                + "there are none until 'credential create' writes one.");

            return CliExitCode.Success;
        }

        OwnerCredentialOutput.WriteListing(context.Console, credentials);

        return CliExitCode.Success;
    }
}
