// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli;

/// <summary>Lists the deployments the operator has signed in to.</summary>
/// <remarks>
/// It reads the store and reaches no deployment, so it answers "what am I signed in to" on a workstation with no
/// network. No token is opened to produce the list, which is why a listing works even where the sealing key does not.
/// </remarks>
internal static class ProfilesCommand
{
    /// <summary>Builds the <c>profiles</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Command command = new("profiles", "List the deployments signed in to, marking the one in use.");

        command.SetAction(_ =>
        {
            var stored = context.Store.Read();

            if (stored.Profiles.Count == 0)
            {
                context.Console.WriteLine(
                    $"No deployment has been signed in to. Run '{CliRootCommand.CommandName} login --endpoint https://host:port'.");

                return CliExitCode.Success;
            }

            foreach (var profile in stored.Profiles.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
            {
                var inUse = string.Equals(profile.Key, stored.Default, StringComparison.OrdinalIgnoreCase);

                context.Console.WriteLine(
                    $"{(inUse ? "*" : " ")} {profile.Key}  {profile.Value.Endpoint}  {profile.Value.Credential}");
            }

            return CliExitCode.Success;
        });

        return command;
    }
}
