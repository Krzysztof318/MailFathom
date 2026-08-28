// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;
using MailFathom.Cli.Administration.Owners;
using MailFathom.Domain.Access;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Provisions a credential one owner's clients present to reach that owner's own mail.</summary>
/// <remarks>
/// <para>
/// The only way to create one: there is no self-service and no default. Whoever administers the deployment provisions
/// the credential and hands it over, which is what keeps an owner from minting a way into anybody's mail, their own
/// included.
/// </para>
/// <para>
/// What the method decides is what has to be supplied and what comes back. A password is asked for at the prompt and
/// never echoed; a key is drawn by the deployment and printed once, because that is the only moment it exists; a public
/// key is read from a file and answered with the fingerprint the client's assertions must name; a mapped subject is
/// two values the operator copies out of their authorization server.
/// </para>
/// </remarks>
internal static class CreateOwnerCredentialCommand
{
    /// <summary>Builds the <c>credential create</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var ownerOption = OwnerOptions.Owner();
        var methodOption = OwnerCredentialOptions.Method();
        var usernameOption = OwnerCredentialOptions.Username();
        var publicKeyFileOption = OwnerCredentialOptions.PublicKeyFile();
        var issuerOption = OwnerCredentialOptions.Issuer();
        var subjectOption = OwnerCredentialOptions.Subject();
        var permissionOption = OwnerCredentialOptions.Permission();
        var noPermissionsOption = OwnerCredentialOptions.NoPermissions();

        Command command = new("create", "Provision a credential for one owner. A password is asked for, never passed; a key is printed once.")
        {
            methodOption,
            usernameOption,
            publicKeyFileOption,
            issuerOption,
            subjectOption,
            permissionOption,
            noPermissionsOption,
            ownerOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            new RequestedCredential(
                result.GetValue(methodOption),
                result.GetValue(usernameOption),
                result.GetValue(publicKeyFileOption),
                result.GetValue(issuerOption),
                result.GetValue(subjectOption),
                OwnerCredentialOptions.ResolveGrant(
                    result.GetValue(permissionOption),
                    result.GetValue(noPermissionsOption))),
            result.GetValue(ownerOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        RequestedCredential requested,
        Guid? requestedOwner,
        string? requestedDeployment,
        CancellationToken cancellationToken)
    {
        var method = OwnerCredentialOptions.ResolveMethod(requested.Method);

        var profile = await context.Deployment().ReachAsync(requestedDeployment, cancellationToken);

        using var transport = context.OpenTransport(profile.Endpoint, profile.Trust);
        var deployment = new AdminApiClient(transport, context.Console);

        var owner = await OwnerOptions.ResolveOwnerAsync(
            deployment,
            profile.Token,
            requestedOwner,
            cancellationToken);

        var request = Compose(context, method, requested);

        var provisioned = await deployment.ProvisionOwnerCredentialAsync(
            profile.Token,
            owner,
            request,
            cancellationToken);

        OwnerCredentialOutput.WriteProvisioned(context.Console, method, owner, provisioned);

        return CliExitCode.Success;
    }

    /// <summary>Composes the request the named method asks for, reading whatever only this side can supply.</summary>
    /// <remarks>The password is read here rather than in the action, so it is asked for after the deployment has been reached and the owner settled — a person typing one should not have it discarded because the endpoint was unreachable.</remarks>
    private static OwnerCredentialProvisioningRequest Compose(
        CliContext context,
        OwnerCredentialMethod method,
        RequestedCredential requested)
    {
        if (method == OwnerCredentialMethod.Password)
        {
            var username = requested.Username is { Length: > 0 } written
                ? written
                : throw new CliFailure("Provisioning a password needs the name the owner will sign in with. Pass '--username'.");

            return new OwnerCredentialProvisioningRequest(
                method.Name,
                username,
                OwnerCredentialOutput.ReadPassword(context, $"Password for '{username}': "),
                PublicKey: null,
                Issuer: null,
                Subject: null,
                requested.Permissions);
        }

        if (method == OwnerCredentialMethod.PublicKey)
        {
            return new OwnerCredentialProvisioningRequest(
                method.Name,
                Username: null,
                Password: null,
                OwnerCredentialOptions.ReadPublicKey(requested.PublicKeyFile),
                Issuer: null,
                Subject: null,
                requested.Permissions);
        }

        if (method == OwnerCredentialMethod.OAuthSubject)
        {
            return new OwnerCredentialProvisioningRequest(
                method.Name,
                Username: null,
                Password: null,
                PublicKey: null,
                RequiredValue(requested.Issuer, "--issuer"),
                RequiredValue(requested.Subject, "--subject"),
                requested.Permissions);
        }

        return new OwnerCredentialProvisioningRequest(
            method.Name,
            Username: null,
            Password: null,
            PublicKey: null,
            Issuer: null,
            Subject: null,
            requested.Permissions);
    }

    private static string RequiredValue(string? written, string optionName) => written is { Length: > 0 } value
        ? value
        : throw new CliFailure($"Mapping an authorization server's subject onto an owner needs '{optionName}'.");

    /// <summary>What the invocation asked for, before the method has decided which of it is read.</summary>
    /// <remarks>A record rather than six parameters threaded through the action, so the argument list stays readable and so nothing here is positional enough to be swapped by accident.</remarks>
    private sealed record RequestedCredential(
        string? Method,
        string? Username,
        FileInfo? PublicKeyFile,
        string? Issuer,
        string? Subject,
        IReadOnlyList<string>? Permissions);
}
