// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands.Owners;

/// <summary>Lets one credential authenticate requests again, with the material it already had.</summary>
/// <remarks>What undoes a suspicion acted on in a hurry. The record kept what it is presented as while it was off, so nothing has to be reissued and nobody has to be told a new one.</remarks>
internal static class EnableOwnerCredentialCommand
{
    /// <summary>Builds the <c>credential enable</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context) => OwnerCredentialEnablement.Build(context, enabled: true);
}
