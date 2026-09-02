// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Settings;

/// <summary>Reads the deployment's persisted configuration document.</summary>
/// <remarks>
/// One read returns the whole document, which is what keeps the configuration layer built from it a snapshot rather
/// than a per-key query. The contract is deliberately read-only: what may be written into the row, and how a write
/// commits against its version, belong to the writing side rather than to the layer that reads it.
/// </remarks>
public interface IRootSettingsDocumentReader
{
    /// <summary>Reads the persisted configuration document.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The document and the version it was read at.</returns>
    /// <exception cref="RootSettingsUnreadableException">Thrown when the persisted configuration cannot be read at all.</exception>
    Task<RootSettingsDocument> ReadAsync(CancellationToken cancellationToken);
}
