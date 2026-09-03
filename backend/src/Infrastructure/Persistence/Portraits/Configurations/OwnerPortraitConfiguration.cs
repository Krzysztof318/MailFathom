// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Portraits.Configurations;

/// <summary>Declares the row holding the picture one person is drawn by.</summary>
/// <remarks>
/// The owner is the primary key rather than a column beside a generated one, because one person has one portrait and
/// nothing else identifies it: that is what makes a write an upsert on a key the caller already holds, and what makes
/// the foreign key onto the owner row and the key the same column.
/// </remarks>
internal sealed class OwnerPortraitConfiguration : IEntityTypeConfiguration<OwnerPortraitEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OwnerPortraitEntity> entity)
    {
        entity.ToTable("owner_portraits");
        entity.HasKey(portrait => portrait.OwnerId);
        entity.Property(portrait => portrait.OwnerId).ValueGeneratedNever();

        entity.Property(portrait => portrait.Content).HasColumnType("bytea").IsRequired();

        // Cascade rather than a statement in the erasure walk: a person's picture is derived from them, so it goes
        // when they do without an erasure having to know this table exists.
        entity.HasOne<OwnerAccountEntity>()
            .WithMany()
            .HasForeignKey(portrait => portrait.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
