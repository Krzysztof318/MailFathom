// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Administration;

/// <summary>The persisted configuration document as an editing session receives it.</summary>
/// <param name="Json">The sparse document, with every secret-bearing value replaced by the redaction marker.</param>
/// <param name="Version">The version it was read at, which the commit that follows is accepted against.</param>
/// <remarks>
/// The version travels with the document because the two are one thing to an editing session: what the operator is
/// looking at and what their save is judged against. A buffer opened over one version and committed against whatever
/// the row held at save time is precisely the lost update the version guard exists to refuse.
/// </remarks>
internal sealed record PersistedSettingsDocument(string Json, long Version);
