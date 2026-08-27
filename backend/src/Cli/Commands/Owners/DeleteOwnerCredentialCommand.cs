// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Removes one credential and frees the username it held.</summary>
/// <remarks>
/// <para>
/// The irreversible half of revoking, and it cannot be undone: the record is gone from the database and nothing in
/// MailFathom can put it back. What replaces it is a new credential with a new identifier, which is why an operator who
/// only wants a way in closed should reach for <c>credential disable</c> instead — that keeps the record, keeps the
/// name claimed, and is one command away from being reversed.
/// </para>
/// <para>
/// Which is why it shows the credential and then asks. The confirmation is the default and <c>--yes</c> is the
/// exception, exactly as it is for the other irreversible commands here, and an invocation with nobody at the terminal
/// is told to pass the flag rather than having an agreement read out of whatever was piped in.
/// </para>
/// </remarks>
internal static class DeleteOwnerCredentialCommand
{
    /// <summary>Builds the <c>credential delete</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var ownerOption = OwnerCredentialOptions.Owner();
        var credentialOption = OwnerCredentialOptions.Credential();
        var confirmedOption = CliOptions.Confirmed("removal");

        Command command = new("delete", "Remove one credential and free the username it held. This cannot be undone.")
        {
            credentialOption,
            ownerOption,
            confirmedOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(credentialOption),
            result.GetValue(ownerOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            result.GetValue(confirmedOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid credentialId,
        Guid? requestedOwner,
        string? requestedDeployment,
        bool confirmedUpFront,
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
        var held = listing.Credentials?.FirstOrDefault(credential => credential.Id == credentialId);

        if (held is null)
        {
            context.Console.WriteLine(
                $"Owner {owner:D} holds no credential {credentialId:D}, so nothing was removed.");

            return CliExitCode.Success;
        }

        OwnerCredentialOutput.WriteListing(context.Console, [held]);

        if (!CliConfirmation.Agreed(
                context,
                confirmedUpFront,
                "There is nobody at the terminal to agree to this, and removing a credential cannot be undone. Pass --yes to remove without being asked.",
                "Remove that credential and free the username it holds? [y/N] "))
        {
            context.Console.WriteError("Nothing was removed.");

            return CliExitCode.Failure;
        }

        await deployment.DeleteOwnerCredentialAsync(profile.Token, owner, credentialId, cancellationToken);

        context.Console.WriteLine(
            $"Removed credential {credentialId:D}. The username it held is free, and nothing in MailFathom can put the "
            + "record back.");

        return CliExitCode.Success;
    }
}
