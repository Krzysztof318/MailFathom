// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares the maintained figure of what one account's stored mail content holds.</summary>
/// <remarks>
/// Keyed by the generated identifier alone, because there is one figure per mailbox and it is read by that key before
/// every folder run. It cascades from the account, so erasing the mailbox takes the figure with the mail it described.
/// The column names are the entity's own constants because both writes are composed statements, so the statements and
/// this mapping name the same things by construction.
/// </remarks>
internal sealed class AccountStoredContentConfiguration : IEntityTypeConfiguration<AccountStoredContentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AccountStoredContentEntity> entity)
    {
        entity.ToTable(AccountStoredContentEntity.TableName);
        entity.HasKey(total => total.MailboxAccountId);
        entity.Property(total => total.MailboxAccountId)
            .HasColumnName(AccountStoredContentEntity.MailboxAccountIdColumnName)
            .HasMaxLength(128)
            .ValueGeneratedNever();
        entity.Property(total => total.StoredContentByteCount)
            .HasColumnName(AccountStoredContentEntity.StoredContentByteCountColumnName);
        entity.HasOne<MailboxAccountEntity>()
            .WithMany()
            .HasForeignKey(total => total.MailboxAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
