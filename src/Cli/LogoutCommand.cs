// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli;

/// <summary>Forgets the credential stored for one deployment.</summary>
/// <remarks>
/// It removes the local copy and nothing else. A credential the deployment issued stays valid until the deployment
/// stops accepting it, so signing out of a workstation is not the same as revoking a key — and the message says so
/// rather than letting an operator believe otherwise.
/// </remarks>
internal static class LogoutCommand
{
    /// <summary>Builds the <c>logout</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var endpointOption = CliOptions.Endpoint();

        Command command = new("logout", "Forget the credential stored for a deployment.")
        {
            endpointOption,
        };

        command.SetAction(result =>
        {
            var endpoint = CliOptions.ResolveEndpoint(result.GetValue(endpointOption));
            var authority = endpoint.GetLeftPart(UriPartial.Authority);

            context.Console.WriteLine(context.Store.Remove(endpoint)
                ? $"Forgot the credential stored for {authority}. It stays valid until the deployment stops accepting it."
                : $"No credential was stored for {authority}.");

            return CliExitCode.Success;
        });

        return command;
    }
}
