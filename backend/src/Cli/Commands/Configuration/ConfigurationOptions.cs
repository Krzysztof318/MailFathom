// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands.Configuration;

/// <summary>The options the configuration commands share.</summary>
/// <remarks>
/// One of them, and it is here rather than repeated because the sentence it carries is the whole of what an operator
/// learns about the refusal it answers: three sources outrank the persisted layer, and a write beneath one of them
/// commits without changing anything the deployment reads. It is worded as the act rather than as a persist, because
/// the four commands it is attached to do four different things to the document — <c>set</c> writes a value,
/// <c>unset</c> takes one out, <c>edit</c> carries a whole saved buffer, and <c>adopt</c> copies many at once — and a
/// description written from any one of them is wrong in the other three's help.
/// </remarks>
internal static class ConfigurationOptions
{
    /// <summary>Builds the option that commits a write to a setting an outranking source already supplies.</summary>
    /// <returns>The option.</returns>
    internal static Option<bool> EvenIfShadowed() => new("--even-if-shadowed")
    {
        Description = "Commit the change even where a command-line argument, an environment variable, or User Secrets supplies the setting — which the deployment refuses by default, because what it would write changes nothing it reads.",
    };
}
