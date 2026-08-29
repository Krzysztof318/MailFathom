// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;

namespace MailFathom.Infrastructure.Secrets.Database;

/// <summary>Writes and inventories material held behind a database secret reference.</summary>
/// <remarks>
/// Reads of material deliberately do not appear here. Every consumer resolves the reference through
/// <see cref="References.ISecretReferenceResolver" />, so no application service gains general secret-reading access.
/// Writes join the caller's persistence session so a future document writer can commit the reference and its material
/// together or roll both back.
/// </remarks>
public interface IStoredSecretStore
{
    /// <summary>The largest material one database-backed secret write accepts.</summary>
    const int MaximumMaterialByteCount = SecretMaterialLimits.MaximumMaterialByteCount;

    /// <summary>The largest key-retirement inventory one read returns.</summary>
    const int MaximumKeyReferenceCount = 1000;

    /// <summary>Gets whether the deployment configures the key ring required to seal material.</summary>
    bool CanStore { get; }

    /// <summary>Seals and stages one stored secret, creating its owner-and-name identity or replacing its material.</summary>
    /// <param name="session">The transaction the referencing document joins.</param>
    /// <param name="reference">The stable reference the document carries.</param>
    /// <param name="owner">The subject whose deletion removes the secret.</param>
    /// <param name="name">The safe declared name used for binding, rotation, and audit.</param>
    /// <param name="material">The caller-owned material, left usable and never retained.</param>
    /// <param name="cancellationToken">Cancels sealing or joining the transaction.</param>
    /// <returns>The stable reference for the owner-and-name identity once the write is staged.</returns>
    /// <remarks>
    /// The supplied reference is used only when no row exists for <paramref name="owner" /> and <paramref name="name" />.
    /// A retry after a concurrent insert returns the winner's reference and replaces its material, so submitting the same
    /// owner and name twice rotates one secret rather than creating two.
    /// </remarks>
    Task<DatabaseSecretReference> StoreAsync(
        IPersistenceSession session,
        DatabaseSecretReference reference,
        MailOwnerId owner,
        SecretName name,
        ResolvedSecret material,
        CancellationToken cancellationToken);

    /// <summary>Stages removal of one stored secret only when it belongs to the stated owner.</summary>
    /// <returns><see langword="true" /> when the session held the matching row; <see langword="false" /> when the reference was absent or belonged to another owner.</returns>
    Task<bool> RemoveAsync(
        IPersistenceSession session,
        DatabaseSecretReference reference,
        MailOwnerId owner,
        CancellationToken cancellationToken);

    /// <summary>Reads a bounded inventory of stored secrets still sealed under one key.</summary>
    /// <param name="keyId">The configured key identifier stored beside each matching value.</param>
    /// <param name="limit">The maximum rows to return, from one through <see cref="MaximumKeyReferenceCount" />; values outside that range are rejected rather than clamped.</param>
    /// <param name="cancellationToken">Cancels the database read.</param>
    /// <returns>Up to <paramref name="limit" /> references still naming the key, ordered by their stable identifier.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="keyId" /> is <see langword="null" />, empty, or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is outside one through <see cref="MaximumKeyReferenceCount" />.</exception>
    Task<IReadOnlyList<StoredSecretKeyReference>> ReadReferencesSealedByKeyAsync(
        string keyId,
        int limit,
        CancellationToken cancellationToken);
}
