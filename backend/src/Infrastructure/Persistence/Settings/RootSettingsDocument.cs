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
public sealed record RootSettingsDocument(string Json, long Version);
