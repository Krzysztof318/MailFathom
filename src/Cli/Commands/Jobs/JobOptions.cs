// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands.Jobs;

/// <summary>The option both decisions about a dead letter are taken with.</summary>
/// <remarks>
/// Declared once because retrying and dropping name the same thing the same way, and a description that drifted
/// between them would be the first hint an operator has that the two commands take different identifiers. It lives
/// here rather than in <see cref="CliOptions" /> because nothing outside this group names a job.
/// </remarks>
internal static class JobOptions
{
    /// <summary>Builds the option naming which dead letter a decision is about.</summary>
    /// <returns>The option.</returns>
    /// <remarks>
    /// Required and without a default, for the reason every destination-naming option here is: both decisions are acts
    /// on one specific piece of somebody's work, and there is no job it would be reasonable to guess.
    /// </remarks>
    internal static Option<Guid> Job() => new("--job")
    {
        Description = "The job to decide about, by the identifier the dead-letter reading reports for it.",
        Required = true,
    };
}
