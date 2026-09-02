// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Preferences.Configurations;

/// <summary>Declares the row holding what one person set about their own client.</summary>
/// <remarks>
/// The owner is the primary key rather than a column beside a generated one, because one person has one set of
/// preferences and nothing else identifies them: that is what makes a write an upsert on a key the caller already
/// holds, and what makes the foreign key onto the owner row and the key the same column.
/// </remarks>
internal sealed class ClientPreferencesConfiguration : IEntityTypeConfiguration<ClientPreferencesEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClientPreferencesEntity> entity)
    {
        entity.ToTable("client_preferences");
        entity.HasKey(preferences => preferences.OwnerId);
        entity.Property(preferences => preferences.OwnerId).ValueGeneratedNever();

        // A document rather than a column per preference, for the reason the owner record is one: what it holds is
        // decided by the layer that writes it, and a preference added later is a key rather than a migration.
        entity.Property(preferences => preferences.Document).HasColumnType("jsonb").IsRequired();

        // Cascade rather than a statement in the erasure walk: what somebody set about their own client is derived
        // from them, so it goes when they do without an erasure having to know this table exists.
        entity.HasOne<OwnerAccountEntity>()
            .WithMany()
            .HasForeignKey(preferences => preferences.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
