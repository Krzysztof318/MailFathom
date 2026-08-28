// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Secrets.Resolution;

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
    /// <summary>The largest key-retirement inventory one read returns.</summary>
    const int MaximumKeyReferenceCount = 1000;

    /// <summary>Seals and stages one stored secret, creating or replacing the row its reference identifies.</summary>
    /// <param name="session">The transaction the referencing document joins.</param>
    /// <param name="reference">The stable reference the document carries.</param>
    /// <param name="owner">The subject whose deletion removes the secret.</param>
    /// <param name="name">The safe declared name used for binding, rotation, and audit.</param>
    /// <param name="material">The caller-owned material, left usable and never retained.</param>
    /// <param name="cancellationToken">Cancels sealing or joining the transaction.</param>
    /// <returns>A task that completes once the write is staged.</returns>
    Task StoreAsync(
        IPersistenceSession session,
        DatabaseSecretReference reference,
        MailOwnerId owner,
        SecretName name,
        ResolvedSecret material,
        CancellationToken cancellationToken);

    /// <summary>Stages removal of one stored secret only when it belongs to the stated owner.</summary>
    Task<bool> RemoveAsync(
        IPersistenceSession session,
        DatabaseSecretReference reference,
        MailOwnerId owner,
        CancellationToken cancellationToken);

    /// <summary>Reads a bounded inventory of stored secrets still sealed under one key.</summary>
    Task<IReadOnlyList<StoredSecretKeyReference>> ReadReferencesSealedByKeyAsync(
        string keyId,
        int limit,
        CancellationToken cancellationToken);
}
