// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;
using MailFathom.Versioning;

namespace MailFathom.Cli;

/// <summary>The commands <c>mfctl</c> publishes.</summary>
/// <remarks>
/// Built here rather than in the entry point so a test can parse an argument list against the real command tree. What
/// the command accepts is part of its contract, and a contract nothing can exercise is one that drifts.
/// </remarks>
internal static class CliRootCommand
{
    /// <summary>The name the published binary carries, as an operator types it.</summary>
    /// <remarks>
    /// Written here rather than read from the running process, because it appears in guidance a failing command prints
    /// — "run <c>mfctl login</c>" — and that has to name the command as it is distributed even when the file has been
    /// renamed on the way to somebody's <c>PATH</c>. The assembly name in <c>Cli.csproj</c> is the other half.
    /// </remarks>
    internal const string CommandName = "mfctl";

    /// <summary>Builds the command tree.</summary>
    /// <param name="context">What the commands need from their surroundings.</param>
    /// <returns>The root command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static RootCommand Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var version = StampedAssemblyVersion.ReadFrom(typeof(CliRootCommand).Assembly);

        Command mailboxCommand = new("mailbox", "Administer a configured mailbox account.")
        {
            AuthorizeMailboxCommand.Create(context),
        };

        return new RootCommand($"MailFathom administration tool ({version.Version}).")
        {
            LoginCommand.Create(context),
            LogoutCommand.Create(context),
            SwitchCommand.Create(context),
            ProfilesCommand.Create(context),
            StatusCommand.Create(context),
            mailboxCommand,
        };
    }
}
