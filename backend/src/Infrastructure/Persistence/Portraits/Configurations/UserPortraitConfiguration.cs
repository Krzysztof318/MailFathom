// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Portraits.Configurations;

/// <summary>Declares the row holding the picture one person is drawn by.</summary>
/// <remarks>
/// The user is the primary key rather than a column beside a generated one, because one person has one portrait and
/// nothing else identifies it: that is what makes a write an upsert on a key the caller already holds, and what makes
/// the foreign key onto the user row and the key the same column.
/// </remarks>
internal sealed class UserPortraitConfiguration : IEntityTypeConfiguration<UserPortraitEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserPortraitEntity> entity)
    {
        entity.ToTable("user_portraits");
        entity.HasKey(portrait => portrait.UserId);
        entity.Property(portrait => portrait.UserId).ValueGeneratedNever();

        entity.Property(portrait => portrait.Content).HasColumnType("bytea").IsRequired();

        // Cascade rather than a statement in the erasure walk: a person's picture is derived from them, so it goes
        // when they do without an erasure having to know this table exists.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(portrait => portrait.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
