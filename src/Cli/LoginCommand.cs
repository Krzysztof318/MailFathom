// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli;

/// <summary>Signs in to a deployment's administrative endpoint and remembers the credential under a name.</summary>
/// <remarks>
/// <para>
/// The credential is verified before it is stored. A deployment that refuses it, an address that serves no
/// administrative endpoint, and a host that answers with something else all fail here rather than at the next command,
/// which is the difference between signing in and writing a file.
/// </para>
/// <para>
/// The credential is read from standard input rather than taken as an argument, because an argument reaches the shell
/// history, the process list, and any log of either. Reading it from there is unconditional: a terminal prompts for
/// it without echoing, and a script pipes it in.
/// </para>
/// <para>
/// Signing in makes the new profile the default, because it is the deployment the operator just chose to work with.
/// <c>switch</c> is how that changes without signing in again.
/// </para>
/// </remarks>
internal static class LoginCommand
{
    /// <summary>Builds the <c>login</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();
        var nameOption = CliOptions.ProfileName();

        Command command = new("login", "Sign in to a deployment's administrative endpoint.")
        {
            endpointOption,
            nameOption,
        };

        command.SetAction((result, cancellationToken) => RunAsync(
            context,
            CliOptions.RequestedDeployment(result.GetValue(endpointOption)),
            result.GetValue(nameOption),
            cancellationToken));

        return command;
    }

    private static async Task<int> RunAsync(
        CliContext context,
        string? requestedDeployment,
        string? requestedName,
        CancellationToken cancellationToken)
    {
        var (endpoint, profileName) = ResolveTarget(context, requestedDeployment, requestedName);

        var token = context.Console.ReadSecret(
            "Administrative credential (an API key, or an access token from the configured authorization server): ");

        if (token.Length == 0)
        {
            throw new CliFailure("No credential was supplied, so there is nothing to sign in with.");
        }

        using var transport = context.OpenTransport(endpoint);
        var session = await new AdminApiClient(transport).ReadSessionAsync(token, cancellationToken);
        var credentialName = session.Credential ?? "unnamed";

        context.Store.Save(profileName, endpoint, token, credentialName);

        context.Console.WriteLine(
            $"Signed in to {endpoint.GetLeftPart(UriPartial.Authority)} as '{credentialName}' (MailFathom {session.Version}), saved as profile '{profileName}' and selected.");

        return CliExitCode.Success;
    }

    /// <summary>Settles which address to sign in to and which name to remember it under.</summary>
    /// <remarks>
    /// An existing profile name is accepted as well as an address, which is how a credential is replaced when the
    /// deployment issues a new one: the operator names the profile they already have rather than retyping its address.
    /// </remarks>
    private static (Uri Endpoint, string ProfileName) ResolveTarget(
        CliContext context,
        string? requestedDeployment,
        string? requestedName)
    {
        if (requestedDeployment is null)
        {
            throw new CliFailure(
                $"No deployment was named. Pass --endpoint https://host:port, or set ${CliOptions.EndpointVariable}.");
        }

        if (CliOptions.TryReadAddress(requestedDeployment, out var address))
        {
            return (address, ValidProfileName(requestedName ?? address.Host));
        }

        var stored = context.Store.Read();

        if (!stored.Profiles.TryGetValue(requestedDeployment, out var existing))
        {
            throw new CliFailure(
                $"'{requestedDeployment}' is neither a stored profile nor an endpoint address. Pass --endpoint https://host:port to sign in to a deployment for the first time.");
        }

        return (
            new Uri(existing.Endpoint, UriKind.Absolute),
            ValidProfileName(requestedName ?? requestedDeployment));
    }

    /// <summary>Refuses a name that could not later be told apart from an address.</summary>
    /// <remarks><c>--endpoint</c> accepts both, and it separates them by whether the value is an absolute URI, so a profile named like one would be a profile nothing could select.</remarks>
    private static string ValidProfileName(string candidate)
    {
        var name = candidate.Trim();

        if (name.Length == 0)
        {
            throw new CliFailure("A profile name cannot be blank. Pass --name with the name to remember this deployment under.");
        }

        if (CliOptions.TryReadAddress(name, out _))
        {
            throw new CliFailure(
                $"'{name}' cannot be a profile name, because --endpoint reads an absolute address as an address rather than as a name. Choose a name such as 'production'.");
        }

        return name;
    }
}
