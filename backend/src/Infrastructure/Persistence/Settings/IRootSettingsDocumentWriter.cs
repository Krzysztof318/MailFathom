// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Settings;

/// <summary>Commits a persisted configuration document over the version it was authored against.</summary>
/// <remarks>
/// The contract is the writing half of <see cref="IRootSettingsDocumentReader" /> and holds nothing about what a
/// document may contain: which paths are writable, whether the configuration it produces binds, and whether it carries
/// a secret as material are decisions taken before a candidate reaches here. What this decides is the one thing only
/// the database can decide — whether the document it is replacing is still the document the caller read.
/// </remarks>
public interface IRootSettingsDocumentWriter
{
    /// <summary>Replaces the persisted configuration document, if it still stands at the expected version.</summary>
    /// <param name="json">The candidate document, as the JSON object the row will hold.</param>
    /// <param name="expectedVersion">The version the candidate was composed over.</param>
    /// <param name="cancellationToken">Cancels the commit.</param>
    /// <returns>The version the commit produced, or <see langword="null" /> when the document had already moved past <paramref name="expectedVersion" />.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="json" /> is <see langword="null" />, empty, white space, or past <see cref="RootSettingsDocument.MaximumOctets" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="expectedVersion" /> is negative.</exception>
    /// <exception cref="RootSettingsUnwritableException">Thrown when the database refused the statement, in which case the persisted document is unchanged.</exception>
    Task<long?> CommitAsync(string json, long expectedVersion, CancellationToken cancellationToken);
}
