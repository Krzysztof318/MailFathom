// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares the maintained figure of what one user's stored mail content holds.</summary>
/// <remarks>
/// Keyed by the user alone, because there is one figure per person and it is read by that key before every folder run.
/// It cascades from the user record, so erasing a user takes the figure with the mail it described. The column names
/// are the entity's own constants because both writes are composed statements, so the statements and this mapping name
/// the same things by construction.
/// </remarks>
internal sealed class UserStoredContentConfiguration : IEntityTypeConfiguration<UserStoredContentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserStoredContentEntity> entity)
    {
        entity.ToTable(UserStoredContentEntity.TableName);
        entity.HasKey(total => total.UserId);
        entity.Property(total => total.UserId)
            .HasColumnName(UserStoredContentEntity.UserIdColumnName)
            .ValueGeneratedNever();
        entity.Property(total => total.StoredContentByteCount)
            .HasColumnName(UserStoredContentEntity.StoredContentByteCountColumnName);
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(total => total.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
