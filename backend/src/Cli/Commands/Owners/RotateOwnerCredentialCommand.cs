// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Owners;
using MailFathom.Domain.Access;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Replaces what one credential is presented as, which stops the previous material working at that instant.</summary>
/// <remarks>
/// <para>
/// The command a secret is changed with, whether because it is due or because it was disclosed. The deployment writes
/// the new material in one statement, so there is no moment at which both work and no moment at which neither does —
/// what a request presenting the old one meets is a refusal, not a half-written record.
/// </para>
/// <para>
/// The credential keeps its identifier, its owner, and its grant, so nothing an operator wrote down about it goes
/// stale. What changes is what it is presented as and the instant the deployment records for it, which is what a
/// listing reports afterwards.
/// </para>
/// <para>
/// A mapped subject cannot be rotated. There is nothing about it this deployment issued, so pointing an owner at a
/// different subject is a credential to provision rather than a secret to replace, and the deployment says so.
/// </para>
/// </remarks>
internal static class RotateOwnerCredentialCommand
{
    /// <summary>Builds the <c>credential rotate</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var ownerOption = OwnerOptions.Owner();
        var credentialOption = OwnerCredentialOptions.Credential();
        var methodOption = OwnerCredentialOptions.Method();
        var usernameOption = OwnerCredentialOptions.Username();
        var publicKeyFileOption = OwnerCredentialOptions.PublicKeyFile();

        Command command = new("rotate", "Replace what one credential is presented as. The previous material stops working at that instant.")
        {
            credentialOption,
            methodOption,
            usernameOption,
            publicKeyFileOption,
            ownerOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(credentialOption),
            result.GetValue(methodOption),
            result.GetValue(usernameOption),
            result.GetValue(publicKeyFileOption),
            result.GetValue(ownerOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid credentialId,
        string? requestedMethod,
        string? username,
        FileInfo? publicKeyFile,
        Guid? requestedOwner,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var method = OwnerCredentialOptions.ResolveMethod(requestedMethod);

        if (!method.MaterialIsReplaceable)
        {
            throw new CliFailure(
                $"A '{method.Name}' credential is presented as something this deployment did not issue, so there is "
                + "nothing to rotate. Provision the credential the owner should act under and delete this one.");
        }

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var owner = await OwnerOptions.ResolveOwnerAsync(
            deployment,
            profile.Token,
            requestedOwner,
            cancellationToken);

        var request = Compose(context, method, credentialId, username, publicKeyFile);

        var rotated = await deployment.ReplaceOwnerCredentialMaterialAsync(
            profile.Token,
            owner,
            credentialId,
            request,
            cancellationToken);

        OwnerCredentialOutput.WriteRotated(context.Console, method, credentialId, rotated);

        return CliExitCode.Success;
    }

    /// <summary>Composes the request the named method asks for, reading whatever only this side can supply.</summary>
    /// <remarks>The username is stated rather than read back from the deployment, because the write names what the credential is resolved by from now on — and for a password rotation that is the value it already had, so a mistyped one is answered as no such credential rather than as a rename.</remarks>
    private static OwnerCredentialMaterialRequest Compose(
        CliContext context,
        OwnerCredentialMethod method,
        Guid credentialId,
        string? username,
        FileInfo? publicKeyFile)
    {
        if (method == OwnerCredentialMethod.Password)
        {
            var signIn = username is { Length: > 0 } written
                ? written
                : throw new CliFailure(
                    "Replacing a password names the username the credential already signs in as, so a mistyped "
                    + "identifier is refused rather than renaming somebody's sign-in. Pass '--username'.");

            return new OwnerCredentialMaterialRequest(
                method.Name,
                signIn,
                OwnerCredentialOutput.ReadPassword(context, $"New password for credential {credentialId:D}: "),
                PublicKey: null);
        }

        if (method == OwnerCredentialMethod.PublicKey)
        {
            return new OwnerCredentialMaterialRequest(
                method.Name,
                Username: null,
                Password: null,
                OwnerCredentialOptions.ReadPublicKey(publicKeyFile));
        }

        return new OwnerCredentialMaterialRequest(method.Name, Username: null, Password: null, PublicKey: null);
    }
}
