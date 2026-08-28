// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using System.Net;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Owners;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Removes one credential and frees what it was resolved by.</summary>
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
/// <para>
/// The listing it shows first is for the operator to read, and it is never what decides the outcome. A credential the
/// listing does not carry is still one this command sends the removal for, because the listing is bounded and a
/// credential past that bound is absent from it while remaining in the deployment and going on authenticating — so
/// deciding from the listing would report a removal that never happened. What the operator is told is what the
/// deployment answered.
/// </para>
/// <para>
/// A listing the deployment refuses <em>for want of the read grant</em> is the same case one step further, and it ends
/// the same way. Reading and removing are separately granted, so a token holding the write grant and not the read one is
/// an ordinary arrangement rather than a broken one, and letting that refusal end the command would make a credential
/// unremovable through the tool that exists to remove it. It is therefore shown and stepped over, exactly as an absent
/// credential is, and the operator is still asked before anything is sent.
/// </para>
/// <para>
/// <strong>Every other failure ends the command instead.</strong> A mistyped port, an unreachable deployment, and a
/// credential the endpoint refused outright all reach this command as the same exception type, and stepping over them
/// would put an irreversible confirmation in front of an operator on the strength of a deployment nothing had
/// contacted. Only <c>403</c> is stepped over, because only <c>403</c> says the token may remove what it may not list.
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
        var ownerOption = OwnerOptions.Owner();
        var credentialOption = OwnerCredentialOptions.Credential();
        var confirmedOption = CliOptions.Confirmed("removal");

        Command command = new("delete", "Remove one credential and free what it was resolved by. This cannot be undone.")
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

        var owner = await OwnerOptions.ResolveOwnerAsync(
            deployment,
            profile.Token,
            requestedOwner,
            cancellationToken);

        var held = await ReadForTheOperatorAsync(context, deployment, profile.Token, owner, credentialId, cancellationToken);

        if (held is null)
        {
            context.Console.WriteLine(
                $"Owner {owner:D} lists no credential {credentialId:D}. The removal is sent anyway, and the deployment "
                + "answers for what it actually holds.");
        }
        else
        {
            OwnerCredentialOutput.WriteListing(context.Console, [held]);
        }

        if (!CliConfirmation.Agreed(
                context,
                confirmedUpFront,
                "There is nobody at the terminal to agree to this, and removing a credential cannot be undone. Pass --yes to remove without being asked.",
                "Remove that credential and free what it is resolved by? [y/N] "))
        {
            context.Console.WriteError("Nothing was removed.");

            return CliExitCode.Failure;
        }

        await deployment.DeleteOwnerCredentialAsync(profile.Token, owner, credentialId, cancellationToken);

        context.Console.WriteLine(
            $"Removed credential {credentialId:D}. What it was resolved by is free, and nothing in MailFathom can "
            + "put the record back.");

        return CliExitCode.Success;
    }

    /// <summary>Reads the credential to show, answering with nothing where the deployment withheld the listing.</summary>
    /// <remarks>
    /// Only the read grant's own refusal is caught; anything else is rethrown and ends the command before the
    /// confirmation is asked for. The one that is caught is written where the operator sees it rather than swallowed,
    /// because a listing that was refused and one that came back empty look identical afterwards and only one of them
    /// says something about the token.
    /// </remarks>
    private static async Task<OwnerCredential?> ReadForTheOperatorAsync(
        CliContext context,
        AdminApiClient deployment,
        string token,
        Guid owner,
        Guid credentialId,
        CancellationToken cancellationToken)
    {
        try
        {
            var listing = await deployment.ReadOwnerCredentialsAsync(token, owner, cancellationToken);

            return listing.Credentials?.FirstOrDefault(credential => credential.Id == credentialId);
        }
        catch (CliFailure refusal) when (refusal.Status is HttpStatusCode.Forbidden)
        {
            context.Console.WriteError($"The credentials could not be listed: {refusal.Message}");

            return null;
        }
    }
}
