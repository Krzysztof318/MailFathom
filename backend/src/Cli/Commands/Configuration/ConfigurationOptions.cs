// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands.Configuration;

/// <summary>The options the configuration commands share.</summary>
/// <remarks>
/// One of them, and it is here rather than repeated because the sentence it carries is the whole of what an operator
/// learns about the refusal it answers: three sources outrank the persisted layer, and a write beneath one of them
/// commits without changing anything the deployment reads.
/// </remarks>
internal static class ConfigurationOptions
{
    /// <summary>Builds the option that commits a write to a setting an outranking source already supplies.</summary>
    /// <returns>The option.</returns>
    internal static Option<bool> EvenIfShadowed() => new("--even-if-shadowed")
    {
        Description = "Persist the setting even where a command-line argument, an environment variable, or User Secrets already supplies it — which the deployment refuses by default, because the value it persists would change nothing it reads.",
    };
}
