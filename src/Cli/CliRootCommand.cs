// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Versioning;

namespace MailFathom.Cli;

/// <summary>The commands <c>mailfathom</c> publishes.</summary>
/// <remarks>
/// Built here rather than in the entry point so a test can parse an argument list against the real command tree. What
/// the command accepts is part of its contract, and a contract nothing can exercise is one that drifts.
/// </remarks>
internal static class CliRootCommand
{
    /// <summary>Builds the command tree.</summary>
    /// <param name="context">What the commands need from their surroundings.</param>
    /// <returns>The root command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static RootCommand Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var version = StampedAssemblyVersion.ReadFrom(typeof(CliRootCommand).Assembly);

        return new RootCommand($"MailFathom administration tool ({version.Version}).")
        {
            LoginCommand.Create(context),
            LogoutCommand.Create(context),
        };
    }
}
