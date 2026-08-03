// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.Accounts;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.DataEncryption;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Accounts;

/// <summary>Holds each account's refresh token in PostgreSQL, sealed under the deployment's data-encryption key ring.</summary>
/// <remarks>
/// <para>
/// This is the adapter the port's contract describes: the ciphertext, the key identifier, the binding, and every Npgsql
/// type stay here, and what leaves is a domain value. The binding is the account's own identifier under the
/// refresh-token purpose, so a row copied to another account, moved into another column, or restored from another
/// deployment fails to open rather than opening as somebody else's credential.
/// </para>
/// <para>
/// The write is one <c>INSERT ... ON CONFLICT DO UPDATE</c> for the reason
/// <see cref="Emails.EmailContentRepairRequestStore" /> uses one: the token is stored on a path that holds no
/// persistence session, so calling <c>SaveChanges</c> on the scoped context would commit whatever else that scope had
/// pending, and PostgreSQL resolving the collision itself is what makes two replicas refreshing at once leave one row.
/// The conflict update keeps the later write, which is what stops a straggler that started earlier from restoring a
/// token the authorization server has already invalidated.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailboxRefreshTokenStore(
    MailFathomDbContext dbContext,
    FieldEncryptor fieldEncryptor,
    TimeProvider timeProvider) : IMailboxRefreshTokenStore
{
    /// <inheritdoc />
    public async Task<MailboxRefreshToken?> FindTokenAsync(MailAccountId accountId, CancellationToken cancellationToken)
    {
        var storedAccountId = accountId.Value;

        var stored = await dbContext.MailboxRefreshTokens
            .AsNoTracking()
            .Where(token => token.MailboxAccountId == storedAccountId)
            .Select(token => new { token.SealedRefreshToken, token.DataEncryptionKeyId })
            .SingleOrDefaultAsync(cancellationToken);

        if (stored is null)
        {
            return null;
        }

        var material = await fieldEncryptor.OpenAsync(
            BindingFor(accountId),
            new SealedValue(stored.DataEncryptionKeyId, stored.SealedRefreshToken),
            cancellationToken);

        try
        {
            return MailboxRefreshToken.Create(material);
        }
        finally
        {
            // The opened buffer is this method's own copy of the credential, and the token owns another. Erasing it
            // keeps the window in which a process dump could contain the token bounded by the operation that asked
            // for it rather than by whenever the collector reclaims an unreferenced array.
            CryptographicOperations.ZeroMemory(material);
        }
    }

    /// <inheritdoc />
    public async Task SaveTokenAsync(
        MailAccountId accountId,
        MailboxRefreshToken refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);

        var sealedToken = await fieldEncryptor.SealAsync(
            BindingFor(accountId),
            refreshToken.RevealBytes().ToArray(),
            cancellationToken);

        var storedAccountId = accountId.Value;
        var ciphertext = sealedToken.Ciphertext.ToArray();
        var keyId = sealedToken.KeyId;
        var updatedAt = timeProvider.GetUtcNow();

        // The identifiers are quoted because EF Core names the columns after the properties, which PostgreSQL would
        // otherwise fold to lower case and fail to find.
        await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO mailbox_refresh_tokens
                 ("MailboxAccountId", "SealedRefreshToken", "DataEncryptionKeyId", "UpdatedAt")
             VALUES ({storedAccountId}, {ciphertext}, {keyId}, {updatedAt})
             ON CONFLICT ("MailboxAccountId") DO UPDATE SET
                 "SealedRefreshToken" = EXCLUDED."SealedRefreshToken",
                 "DataEncryptionKeyId" = EXCLUDED."DataEncryptionKeyId",
                 "UpdatedAt" = EXCLUDED."UpdatedAt"
             WHERE EXCLUDED."UpdatedAt" >= mailbox_refresh_tokens."UpdatedAt"
             """,
            cancellationToken);
    }

    private static DataEncryptionBinding BindingFor(MailAccountId accountId) =>
        DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, accountId.Value);
}
