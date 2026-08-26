// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Settings;

/// <summary>The persisted configuration document, as one snapshot with the version it was read at.</summary>
/// <param name="Json">The sparse settings document, as the JSON object the row holds.</param>
/// <param name="Version">The version the document was read at, which a writer states and is refused against.</param>
/// <remarks>
/// The whole document travels together because the configuration layer built from it is replaced whole: a reader that
/// answered per key would query the database once per configuration property, and a reload that merged into the
/// previous snapshot could not express a key the new document no longer carries.
/// </remarks>
public sealed record RootSettingsDocument(string Json, long Version)
{
    /// <summary>The largest document this build composes settings from, and therefore the largest one it will persist.</summary>
    /// <remarks>
    /// <para>
    /// <c>jsonb</c> holds up to a gigabyte, and this document is expanded three times over on its way to a snapshot —
    /// the string the driver materializes, the UTF-8 bytes the parser is handed, and the flattened dictionary — while
    /// the host composes its configuration with no endpoint open. A ceiling that a configuration document could
    /// plausibly reach would be the wrong ceiling; this one is far past any settings a deployment writes and far below
    /// anything that costs the composition a thought, so a row past it is a row something went wrong with.
    /// </para>
    /// <para>
    /// One bound rather than two, because the two directions are the same decision: a write permitted past what the
    /// read composes from would persist a row the next start refuses, and the deployment would be stopped by a change
    /// that had been accepted.
    /// </para>
    /// </remarks>
    public const int MaximumOctets = 1024 * 1024;
}
