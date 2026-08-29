// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Secrets;
using MailFathom.Infrastructure.Secrets.Database;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Secrets;

/// <summary>Opens material stored behind the <c>database:</c> secret-reference scheme.</summary>
/// <remarks>
/// The lookup is one bounded query per use and caches nothing. A database copy therefore holds only ciphertext, a
/// key rotation is observed on the next write, and the plaintext lives only for the operation that requested it.
/// The pool and encryptor are read lazily because both depend on the composite resolver this adapter joins: the pool
/// may resolve its password through it, and the encryptor's key ring resolves its deployment-provisioned key through it.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class DatabaseSecretReferenceResolver(
    Func<NpgsqlDataSource> readDataSource,
    Func<FieldEncryptor> readFieldEncryptor,
    Func<DatabaseCommandTimeout> readCommandTimeout) : ISecretSchemeResolver
{
    private const string SelectStoredSecret =
        """
        SELECT
            "OwnerId",
            "Name",
            "DataEncryptionKeyId",
            octet_length("SealedMaterial") AS "Length",
            CASE WHEN octet_length("SealedMaterial") <= @maximumOctets THEN "SealedMaterial" END AS "SealedMaterial"
        FROM stored_secrets
        WHERE "Id" = @id;
        """;

    /// <inheritdoc />
    public SecretReferenceScheme Scheme => DatabaseSecretReference.Scheme;

    /// <inheritdoc />
    public async Task<SecretResolutionResult> ResolveAsync(
        SecretReference reference,
        CancellationToken cancellationToken)
    {
        if (!DatabaseSecretReference.TryCreate(reference, out var databaseReference))
        {
            return SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound);
        }

        try
        {
            MailOwnerId owner;
            SecretName name;
            SealedValue sealedValue;

            {
                var dataSource = readDataSource();
                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
                await using var command = new NpgsqlCommand(SelectStoredSecret, connection)
                {
                    CommandTimeout = (int)readCommandTimeout().Value.TotalSeconds,
                };
                command.Parameters.AddWithValue("id", databaseReference.Id);
                command.Parameters.AddWithValue("maximumOctets", StoredSecretEntity.MaximumSealedMaterialByteCount);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return SecretResolutionResult.Failed(SecretResolutionFailure.MaterialNotFound);
                }

                var sealedLength = reader.GetInt32(3);
                if (sealedLength < StoredSecretEntity.MinimumSealedMaterialByteCount)
                {
                    return SecretResolutionResult.Failed(SecretResolutionFailure.MaterialEmpty);
                }

                if (sealedLength > StoredSecretEntity.MaximumSealedMaterialByteCount)
                {
                    return SecretResolutionResult.Failed(SecretResolutionFailure.MaterialTooLarge);
                }

                if (!SecretName.TryCreate(reader.GetString(1), out name))
                {
                    return SecretResolutionResult.Failed(SecretResolutionFailure.ProtectedMaterialUnavailable);
                }

                var ownerId = reader.GetGuid(0);
                if (ownerId == Guid.Empty)
                {
                    return SecretResolutionResult.Failed(SecretResolutionFailure.ProtectedMaterialUnavailable);
                }

                owner = MailOwnerId.Create(ownerId);
                sealedValue = new SealedValue(reader.GetString(2), reader.GetFieldValue<byte[]>(4));
            }

            return await this.OpenAsync(owner, databaseReference, name, sealedValue, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            return SecretResolutionResult.Failed(ClassifyProviderFailure(exception));
        }
    }

    internal static SecretResolutionFailure ClassifyProviderFailure(NpgsqlException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.InnerException is TimeoutException
            ? SecretResolutionFailure.RetrievalTimedOut
            : SecretResolutionFailure.ProviderUnavailable;
    }

    private async Task<SecretResolutionResult> OpenAsync(
        MailOwnerId owner,
        DatabaseSecretReference reference,
        SecretName name,
        SealedValue sealedValue,
        CancellationToken cancellationToken)
    {
        byte[] plaintext;
        try
        {
            plaintext = await readFieldEncryptor().OpenAsync(
                StoredSecretBinding.Create(owner, reference, name),
                sealedValue,
                cancellationToken);
        }
        catch (Exception exception) when (exception is CryptographicException or InvalidOperationException)
        {
            return SecretResolutionResult.Failed(SecretResolutionFailure.ProtectedMaterialUnavailable);
        }

        try
        {
            if (plaintext.Length == 0)
            {
                return SecretResolutionResult.Failed(SecretResolutionFailure.MaterialEmpty);
            }

            if (plaintext.Length > SecretMaterialLimits.MaximumMaterialByteCount)
            {
                return SecretResolutionResult.Failed(SecretResolutionFailure.MaterialTooLarge);
            }

            return SecretResolutionResult.Resolved(
                ResolvedSecret.FromBytes(plaintext),
                SecretMaterialSource.SchemeAdapter);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}
