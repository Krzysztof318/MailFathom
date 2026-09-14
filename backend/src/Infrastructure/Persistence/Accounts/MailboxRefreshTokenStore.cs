// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
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
/// type stay here, and what leaves is a domain value. The binding is the account's own identity — its user and its
/// identifier together — under the refresh-token purpose, so a row copied to another account, moved into another
/// column, or restored from another deployment fails to open rather than opening as somebody else's credential.
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
    public async Task<MailboxRefreshToken?> FindTokenAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var storedAccountId = account.Value;

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
            BindingFor(account),
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
        MailAccountId account,
        MailboxRefreshToken refreshToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);

        // The encryptor takes the plaintext across an async boundary, so it cannot be a span and has to be a copy the
        // token does not own. That copy is this method's to erase: leaving it for the collector would put the credential
        // back on the managed heap for an unbounded time, which is the whole thing the domain type refuses to do.
        var plaintext = refreshToken.RevealBytes().ToArray();
        SealedValue sealedToken;
        try
        {
            sealedToken = await fieldEncryptor.SealAsync(BindingFor(account), plaintext, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }

        var storedAccountId = account.Value;
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

    /// <summary>Composes what a token is bound to, which is the account's generated identifier and nothing beside it.</summary>
    /// <remarks>
    /// The identifier names one mailbox across the whole deployment, so it is the whole subject: there is no second
    /// mailbox it could collide with, and a user beside it would make one mailbox's sealed token fail to open for the
    /// next person assigned the same mailbox. What the binding refuses is a token resealed under another account's
    /// name, which the identifier alone is enough to refuse.
    /// </remarks>
    private static DataEncryptionBinding BindingFor(MailAccountId account) =>
        DataEncryptionBinding.Create(DataEncryptionPurpose.MailboxRefreshToken, account.Value);
}
