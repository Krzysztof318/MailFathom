// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.ClientAssertions.Configurations;

/// <summary>Declares the client assertions this deployment has already served.</summary>
/// <remarks>
/// The key is the pair, and the pair being unique is the anti-replay rule itself rather than a property of the storage:
/// the spend is an insert that either happens or conflicts, so nothing reads a row back and there is no query shape for
/// the mapping to serve. Nothing hangs off the table and nothing cascades into it — not from the user record either,
/// because the credential column holds a configured key's name as readily as a registered key's fingerprint and belongs
/// to neither. The one index beside the key is the expiry, which the removal walks. The column names are the entity's
/// own constants because both statements are composed, so the statement and this mapping name the same things by
/// construction.
/// </remarks>
internal sealed class SpentClientAssertionConfiguration : IEntityTypeConfiguration<SpentClientAssertionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SpentClientAssertionEntity> entity)
    {
        entity.ToTable(SpentClientAssertionEntity.TableName);
        entity.HasKey(spent => new { spent.CredentialKey, spent.Identifier });
        entity.Property(spent => spent.CredentialKey)
            .HasColumnName(SpentClientAssertionEntity.CredentialKeyColumnName)
            .HasMaxLength(SpentClientAssertionEntity.CredentialKeyLengthLimit)
            .ValueGeneratedNever();
        entity.Property(spent => spent.Identifier)
            .HasColumnName(SpentClientAssertionEntity.IdentifierColumnName)
            .HasMaxLength(SpentClientAssertionEntity.IdentifierLengthLimit)
            .ValueGeneratedNever();
        entity.Property(spent => spent.ExpiresAt)
            .HasColumnName(SpentClientAssertionEntity.ExpiresAtColumnName);
        entity.HasIndex(spent => spent.ExpiresAt)
            .HasDatabaseName(PersistenceConstraintNames.SpentClientAssertionExpiryIndexName);
    }
}
