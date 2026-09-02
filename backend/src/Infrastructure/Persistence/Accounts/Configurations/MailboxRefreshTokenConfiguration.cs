// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Accounts.Configurations;

/// <summary>Declares the sealed OAuth refresh token one account's connections are renewed from.</summary>
/// <remarks>
/// No foreign key onto the mailbox account, which is the one relationship a reader would expect here. That row is
/// written by whichever synchronization run first binds a folder, so requiring it would mean a token could only be
/// stored for an account that has already synchronized — the opposite of the order an operator works in. What follows
/// is that removing an account has to remove this row deliberately rather than by cascade, which is the erasure seam's
/// job rather than the schema's.
/// </remarks>
internal sealed class MailboxRefreshTokenConfiguration : IEntityTypeConfiguration<MailboxRefreshTokenEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailboxRefreshTokenEntity> entity)
    {
        entity.ToTable("mailbox_refresh_tokens");
        // One token per account, and an account is the owner and the identifier together — so the key is the pair
        // rather than the identifier, which names one mailbox within its owner and another within the next.
        entity.HasKey(token => new { token.OwnerId, token.MailboxAccountId });
        entity.Property(token => token.MailboxAccountId).HasMaxLength(128).ValueGeneratedNever();
        entity.Property(token => token.SealedRefreshToken).HasColumnType("bytea").IsRequired();
        entity.Property(token => token.DataEncryptionKeyId)
            .HasMaxLength(MailboxRefreshTokenEntity.MaximumKeyIdLength)
            .IsRequired();

        // What a key retirement is planned against: the pass that re-seals under a new key reads the accounts still
        // holding a value under the old one, and without this it would read every row to answer that.
        entity.HasIndex(token => token.DataEncryptionKeyId).HasDatabaseName(PersistenceConstraintNames.MailboxRefreshTokenKeyIndexName);
    }
}
