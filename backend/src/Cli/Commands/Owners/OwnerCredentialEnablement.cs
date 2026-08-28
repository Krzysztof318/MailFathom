// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Builds whichever of the two commands turns a credential on or off.</summary>
/// <remarks>
/// <para>
/// The reversible half of revoking. A disabled credential stops authenticating immediately and keeps what it is
/// presented as, so a suspicion acted on in a hurry costs nothing to undo and holds that value against anything else
/// being provisioned under it in the meantime. Removing the credential is what frees the name, and that is
/// <c>credential delete</c>.
/// </para>
/// <para>
/// Two commands built from one description rather than one command carrying a flag, because turning a way into
/// somebody's mail on and turning it off are opposite decisions and a mistyped value should not be the difference
/// between them. The description is written once here so the two cannot drift apart, and each of them is a class with a
/// factory of its own — which is what puts both into the assembly's own enumeration of the commands it publishes, and
/// so what makes either one being detached from the tree a failure rather than a silent absence.
/// </para>
/// </remarks>
internal static class OwnerCredentialEnablement
{
    /// <summary>Builds the <c>credential enable</c> or <c>credential disable</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <param name="enabled">Which of the two this builds.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Build(CliContext context, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var ownerOption = OwnerOptions.Owner();
        var credentialOption = OwnerCredentialOptions.Credential();

        Command command = new(
            enabled ? "enable" : "disable",
            enabled
                ? "Let one credential authenticate requests again. What it is presented as is unchanged."
                : "Stop one credential authenticating requests, keeping what it is presented as.")
        {
            credentialOption,
            ownerOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(credentialOption),
            result.GetValue(ownerOption),
            enabled,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid credentialId,
        Guid? requestedOwner,
        bool enabled,
        string? requestedDeployment,
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

        await deployment.SetOwnerCredentialEnabledAsync(
            profile.Token,
            owner,
            credentialId,
            enabled,
            cancellationToken);

        context.Console.WriteLine(enabled
            ? $"Credential {credentialId:D} authenticates requests again, with the material it already had."
            : $"Credential {credentialId:D} no longer authenticates anything. What it is resolved by stays claimed, so nothing else can be provisioned under it.");

        return CliExitCode.Success;
    }
}
