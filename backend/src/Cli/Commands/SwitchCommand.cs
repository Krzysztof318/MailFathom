// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands;

/// <summary>Chooses which stored profile later commands act on.</summary>
/// <remarks>
/// It writes the choice and reaches no deployment. That is deliberate: switching to a profile whose deployment is down
/// has to work, because the next thing an operator does may well be to ask why it is down.
/// </remarks>
internal static class SwitchCommand
{
    /// <summary>Builds the <c>switch</c> command.</summary>
    /// <param name="context">What the command needs from its surroundings.</param>
    /// <returns>The command.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="context" /> is <see langword="null" />.</exception>
    internal static Command Create(CliContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        Argument<string> nameArgument = new("name")
        {
            Description = "The profile to act on from now on.",
        };

        Command command = new("switch", "Choose which stored profile later commands act on.")
        {
            nameArgument,
        };

        command.SetAction(result =>
        {
            var (name, selected) = context.Store.SwitchTo(result.GetValue(nameArgument) ?? string.Empty);

            context.Console.WriteLine(
                $"Now acting on '{name}' ({selected.Endpoint}) as '{selected.Credential}'.");

            return CliExitCode.Success;
        });

        return command;
    }
}
