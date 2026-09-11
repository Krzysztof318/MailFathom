// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares the room each replica has reserved for a payload it is about to store.</summary>
/// <remarks>
/// Keyed by the claim's own identity, because a holder releases exactly the claim it took and nothing else joins to
/// one. The index is on the expiry, which is what every read of this table filters on: a claim binds while it has not
/// expired, and the sweep removes the rows that no longer do. Nothing cascades into it, not even from the user record,
/// because a claim outlives nothing — the expiry removes every row a release did not. The column names are the
/// entity's own constants because the claim is a composed statement, so the statement and this mapping name the same
/// things by construction.
/// </remarks>
internal sealed class StoredContentClaimConfiguration : IEntityTypeConfiguration<StoredContentClaimEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StoredContentClaimEntity> entity)
    {
        entity.ToTable(StoredContentClaimEntity.TableName);
        entity.HasKey(claim => claim.Id);
        entity.Property(claim => claim.Id)
            .HasColumnName(StoredContentClaimEntity.IdColumnName)
            .ValueGeneratedNever();
        entity.Property(claim => claim.UserId)
            .HasColumnName(StoredContentClaimEntity.UserIdColumnName)
            .ValueGeneratedNever();
        entity.Property(claim => claim.ClaimedByteCount)
            .HasColumnName(StoredContentClaimEntity.ClaimedByteCountColumnName);
        entity.Property(claim => claim.ExpiresAt)
            .HasColumnName(StoredContentClaimEntity.ExpiresAtColumnName);
        entity.HasIndex(claim => claim.ExpiresAt);
    }
}
