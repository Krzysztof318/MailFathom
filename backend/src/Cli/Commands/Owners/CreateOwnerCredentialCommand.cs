// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Provisions a username and password one owner signs in to their own mail with.</summary>
/// <remarks>
/// <para>
/// The one credential in MailFathom that belongs to a person rather than to a deployment, and the only way to create
/// one: there is no self-service and no default. Whoever administers the deployment provisions it and tells the owner
/// what it is, which is what keeps an owner from minting a way into anybody's mail, their own included.
/// </para>
/// <para>
/// The password is asked for rather than passed. It is read without echo where somebody is at the terminal and as one
/// line where the input is a pipe, so it stays out of the shell history, the process table, and whatever a supervisor
/// logged about the command it started.
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
        var ownerOption = OwnerCredentialOptions.Owner();
        var usernameOption = OwnerCredentialOptions.Username();

        Command command = new("create", "Provision a username and password for one owner. The password is asked for, never passed.")
        {
            usernameOption,
            ownerOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(usernameOption)!,
            result.GetValue(ownerOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string username,
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

        var password = OwnerCredentialOutput.ReadPassword(context, $"Password for '{username}': ");

        var provisioned = await deployment.ProvisionOwnerCredentialAsync(
            profile.Token,
            owner,
            username,
            password,
            cancellationToken);

        context.Console.WriteLine(
            $"Provisioned credential {provisioned.CredentialId:D} for owner {owner:D}. The owner signs in with '{username}' "
            + "and the password you typed, which nothing here or in the deployment can report back.");

        return CliExitCode.Success;
    }
}
