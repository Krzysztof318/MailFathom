// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Secrets;

/// <summary>Stages database-backed secret writes in the transaction holding their references.</summary>
[RequiresIntegrationCoverage]
internal sealed class StoredSecretStore(
    MailFathomDbContext readContext,
    DataEncryptionKeyRing keyRing,
    TimeProvider timeProvider) : IStoredSecretStore
{
    /// <inheritdoc />
    public bool CanStore => keyRing.IsConfigured;

    /// <inheritdoc />
    public async Task<DatabaseSecretReference> StoreAsync(
        IPersistenceSession session,
        DatabaseSecretReference reference,
        MailOwnerId owner,
        SecretName name,
        ResolvedSecret material,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(material);

        if (!this.CanStore)
        {
            throw new InvalidOperationException(
                "DataEncryption configures no key ring, so MailFathom cannot store secret material in the database.");
        }

        var materialLength = material.RevealBytes().Length;
        if (materialLength == 0)
        {
            throw new ArgumentException("Stored secret material cannot be empty.", nameof(material));
        }

        if (materialLength > SecretMaterialLimits.MaximumMaterialByteCount)
        {
            throw new ArgumentException("Stored secret material exceeds the configured material bound.", nameof(material));
        }

        using var key = await keyRing.ResolveActiveKeyAsync(cancellationToken);
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var stored = await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            writeContext.StoredSecrets,
            writeContext.StoredSecrets,
            secret => secret.OwnerId == owner.Value && secret.Name == name.Value,
            cancellationToken);
        if (stored is null)
        {
            stored = await writeContext.StoredSecrets.FindAsync([reference.Id], cancellationToken);
        }

        if (stored is not null && stored.OwnerId != owner.Value)
        {
            throw new InvalidOperationException(
                "The database secret reference already belongs to another owner and cannot be moved.");
        }

        var effectiveReference = stored is null
            ? reference
            : DatabaseSecretReference.Create(stored.Id);
        var plaintext = material.RevealBytes().ToArray();
        SealedValue sealedValue;
        try
        {
            sealedValue = key.Seal(StoredSecretBinding.Create(owner, effectiveReference, name), plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var now = timeProvider.GetUtcNow();

        if (stored is null)
        {
            stored = new StoredSecretEntity
            {
                Id = reference.Id,
                OwnerId = owner.Value,
                Name = name.Value!,
                SealedMaterial = sealedValue.Ciphertext.ToArray(),
                DataEncryptionKeyId = sealedValue.KeyId,
                CreatedAt = now,
                UpdatedAt = now,
            };
            writeContext.StoredSecrets.Add(stored);

            return effectiveReference;
        }

        stored.Name = name.Value!;
        stored.SealedMaterial = sealedValue.Ciphertext.ToArray();
        stored.DataEncryptionKeyId = sealedValue.KeyId;
        stored.UpdatedAt = now;

        return effectiveReference;
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        IPersistenceSession session,
        DatabaseSecretReference reference,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (!reference.IsSpecified)
        {
            throw new ArgumentException("A stored secret removal requires a database reference.", nameof(reference));
        }

        if (!owner.IsSpecified)
        {
            throw new ArgumentException("A stored secret removal requires an owner.", nameof(owner));
        }

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var stored = await writeContext.StoredSecrets.FindAsync([reference.Id], cancellationToken);
        if (stored is null || stored.OwnerId != owner.Value)
        {
            return false;
        }

        writeContext.StoredSecrets.Remove(stored);

        return true;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredSecretKeyReference>> ReadReferencesSealedByKeyAsync(
        string keyId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, IStoredSecretStore.MaximumKeyReferenceCount);

        var stored = await readContext.StoredSecrets
            .AsNoTracking()
            .Where(secret => secret.DataEncryptionKeyId == keyId)
            .OrderBy(secret => secret.Id)
            .Take(limit)
            .Select(secret => new { secret.Id, secret.OwnerId, secret.Name })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. stored.Select(secret => new StoredSecretKeyReference(
                DatabaseSecretReference.Create(secret.Id),
                MailOwnerId.Create(secret.OwnerId),
                SecretName.TryCreate(secret.Name, out var name)
                    ? name
                    : throw new InvalidOperationException("A stored secret carries a name the current schema refuses."))),
        ];
    }
}
