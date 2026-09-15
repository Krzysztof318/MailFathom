// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.CommandLine;

namespace MailFathom.Cli.Commands.Exports;

/// <summary>The options the four commands acting on one export take.</summary>
/// <remarks>
/// Declared once because reading, downloading, cancelling, and deleting all name the same thing the same way, and a
/// description that drifted between them would be the first hint an operator has that the commands take different
/// identifiers. It lives here rather than in <see cref="CliOptions" /> because nothing outside this group names an
/// export.
/// </remarks>
internal static class ExportOptions
{
    /// <summary>Builds the option naming which export an act is about.</summary>
    /// <returns>The option.</returns>
    internal static Option<Guid> Export() => new("--export")
    {
        Description = "The export to act on, by the identifier 'export status' reports for it.",
        Required = true,
    };

    /// <summary>Builds the option narrowing the listing to one export.</summary>
    /// <returns>The option.</returns>
    /// <remarks>Optional where <see cref="Export" /> is required, and that is the whole difference: omitting it lists what the account has rather than naming an export nobody chose.</remarks>
    internal static Option<Guid?> NarrowedExport() => new("--export")
    {
        Description = "Report one export rather than the account's listing, by the identifier the listing reports.",
    };
}
