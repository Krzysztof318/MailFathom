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
/// type stay here, and what leaves is a domain value. The binding is the account's own identity — its owner and its
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
        MailAccountIdentity account,
        CancellationToken cancellationToken)
    {
        var storedOwnerId = account.Owner.Value;
        var storedAccountId = account.Id.Value;

        var stored = await dbContext.MailboxRefreshTokens
            .AsNoTracking()
            .Where(token => token.OwnerId == storedOwnerId && token.MailboxAccountId == storedAccountId)
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
        MailAccountIdentity account,
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

        var storedOwnerId = account.Owner.Value;
        var storedAccountId = account.Id.Value;
        var ciphertext = sealedToken.Ciphertext.ToArray();
        var keyId = sealedToken.KeyId;
        var updatedAt = timeProvider.GetUtcNow();

        // The identifiers are quoted because EF Core names the columns after the properties, which PostgreSQL would
        // otherwise fold to lower case and fail to find.
        await dbContext.Database.ExecuteSqlAsync(
            $"""
             INSERT INTO mailbox_refresh_tokens
                 ("OwnerId", "MailboxAccountId", "SealedRefreshToken", "DataEncryptionKeyId", "UpdatedAt")
             VALUES ({storedOwnerId}, {storedAccountId}, {ciphertext}, {keyId}, {updatedAt})
             ON CONFLICT ("OwnerId", "MailboxAccountId") DO UPDATE SET
                 "SealedRefreshToken" = EXCLUDED."SealedRefreshToken",
                 "DataEncryptionKeyId" = EXCLUDED."DataEncryptionKeyId",
                 "UpdatedAt" = EXCLUDED."UpdatedAt"
             WHERE EXCLUDED."UpdatedAt" >= mailbox_refresh_tokens."UpdatedAt"
             """,
            cancellationToken);
    }

    /// <summary>Composes what a token is bound to, which is the whole of the account rather than the name it goes by.</summary>
    /// <remarks>
    /// The owner leads the subject because the identifier after it names one mailbox within that owner and a different
    /// one within the next: bound to the identifier alone, two people's <c>work</c> accounts would share a binding and
    /// one's sealed token would open as the other's credential, which is the one thing the binding exists to refuse.
    /// The owner is a GUID in its fixed 36-character form, so the separator cannot be read as part of either half and
    /// no two identities compose one subject.
    /// </remarks>
    private static DataEncryptionBinding BindingFor(MailAccountIdentity account) =>
        DataEncryptionBinding.Create(
            DataEncryptionPurpose.MailboxRefreshToken,
            $"{account.Owner.Value:D}/{account.Id.Value}");
}
