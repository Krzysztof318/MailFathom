// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Cli.Administration;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Replaces one credential's password, which stops the previous one working at that instant.</summary>
/// <remarks>
/// <para>
/// The command an owner's password is changed with, whether because it is due or because it was disclosed. The
/// deployment writes the new password in one statement, so there is no moment at which both work and no moment at which
/// neither does — what a request presenting the old one meets is a refusal, not a half-written record.
/// </para>
/// <para>
/// The credential keeps its identifier and its username, so nothing an operator wrote down about it goes stale. What
/// changes is the secret and the instant the deployment records for it, which is what a listing reports afterwards.
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
        var ownerOption = OwnerCredentialOptions.Owner();
        var credentialOption = OwnerCredentialOptions.Credential();

        Command command = new("rotate", "Replace one credential's password. The previous one stops working at that instant.")
        {
            credentialOption,
            ownerOption,
            endpointOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            result.GetValue(credentialOption),
            result.GetValue(ownerOption),
            CliOptions.RequestedDeployment(result.GetValue(endpointOption), context.Variable(CliOptions.EndpointVariable)),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        Guid credentialId,
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

        var password = OwnerCredentialOutput.ReadPassword(context, $"New password for credential {credentialId:D}: ");

        await deployment.RotateOwnerCredentialPasswordAsync(
            profile.Token,
            owner,
            credentialId,
            password,
            cancellationToken);

        context.Console.WriteLine(
            $"Replaced the password on credential {credentialId:D}. Anything still presenting the previous one is "
            + "refused from now on.");

        return CliExitCode.Success;
    }
}
