// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Signals.Configurations;

/// <summary>Declares the unspent tickets the deployment's signal connections are opened against.</summary>
/// <remarks>
/// The identifier is the key, and the row being removed by the presentation that reads it is the single-use rule
/// itself rather than a property of the storage: the spend is one delete that returns what it removed, so nothing reads
/// a row back and there is no query shape for the mapping to serve. The one index beside the key is the expiry, which
/// the removal walks. The column names are the entity's own constants because every statement is composed, so the
/// statements and this mapping name the same things by construction.
/// </remarks>
internal sealed class ClientSignalTicketConfiguration : IEntityTypeConfiguration<ClientSignalTicketEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClientSignalTicketEntity> entity)
    {
        entity.ToTable(ClientSignalTicketEntity.TableName);
        entity.HasKey(ticket => ticket.Identifier);
        entity.Property(ticket => ticket.Identifier)
            .HasColumnName(ClientSignalTicketEntity.IdentifierColumnName)
            .HasMaxLength(ClientSignalTicketEntity.IdentifierLengthLimit)
            .ValueGeneratedNever();
        entity.Property(ticket => ticket.UserId)
            .HasColumnName(ClientSignalTicketEntity.UserIdColumnName);
        entity.Property(ticket => ticket.SecretDigest)
            .HasColumnName(ClientSignalTicketEntity.SecretDigestColumnName)
            .HasMaxLength(ClientSignalTicketEntity.SecretDigestByteCount)
            .IsRequired();
        entity.Property(ticket => ticket.ExpiresAt)
            .HasColumnName(ClientSignalTicketEntity.ExpiresAtColumnName);
        entity.HasIndex(ticket => ticket.ExpiresAt)
            .HasDatabaseName(PersistenceConstraintNames.ClientSignalTicketExpiryIndexName);

        // Cascade rather than a statement in the erasure walk, for the reason the preferences row carries one: a ticket
        // is minted for one person and names nobody else, so it goes when they do without an erasure having to know
        // this table exists. What it costs is one index on the user, which is what makes that delete cheap.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(ticket => ticket.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
