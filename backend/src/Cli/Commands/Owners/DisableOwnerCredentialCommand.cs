// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Stops one credential authenticating requests, keeping what it is presented as.</summary>
/// <remarks>The command to reach for when a way into somebody's mail has to be closed now and the decision may have to be undone. Removing the record instead is <c>credential delete</c>, and nothing puts that back.</remarks>
internal static class DisableOwnerCredentialCommand
{
    /// <summary>Builds the <c>credential disable</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context) => OwnerCredentialEnablement.Build(context, enabled: false);
}
