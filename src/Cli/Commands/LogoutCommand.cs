// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands;

/// <summary>Forgets one stored profile.</summary>
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

        Command command = new("logout", "Forget a stored profile.")
        {
            endpointOption,
        };

        command.SetAction(result =>
        {
            // Resolved rather than removed by the typed spelling, so that logging out of the profile in use takes no
            // argument, and so that naming an address removes the profile serving it rather than reporting that no
            // profile carries that name.
            var (name, credential) = context.Store.Locate(
                CliOptions.RequestedDeployment(result.GetValue(endpointOption)));

            context.Store.Remove(name);

            context.Console.WriteLine(
                $"Forgot profile '{name}' ({credential.Endpoint}). The credential stays valid until the deployment stops accepting it.");

            return CliExitCode.Success;
        });

        return command;
    }
}
