// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Discovery.Configurations;

/// <summary>Declares what one Discover run has written, in the order it wrote it.</summary>
/// <remarks>
/// The run and the sequence together are the key, which is both the promise the run's contract makes and the order its
/// one reader walks — everything of one run after a cursor — so no index stands beside it. The payload is
/// <c>jsonb</c> rather than text because that is what the column takes a document as and what refuses one that is not
/// a document; nothing queries inside it, the events being read back whole. The cascade from the run is what makes the
/// retention removal one statement against the runs alone.
/// </remarks>
internal sealed class DiscoveryRunEventConfiguration : IEntityTypeConfiguration<DiscoveryRunEventEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DiscoveryRunEventEntity> entity)
    {
        entity.ToTable(DiscoveryRunEventEntity.TableName);
        entity.HasKey(@event => new { @event.RunId, @event.Sequence })
            .HasName(PersistenceConstraintNames.DiscoveryRunEventPrimaryKeyConstraintName);
        entity.Property(@event => @event.RunId)
            .HasColumnName(DiscoveryRunEventEntity.RunIdColumnName);
        entity.Property(@event => @event.Sequence)
            .HasColumnName(DiscoveryRunEventEntity.SequenceColumnName)
            .ValueGeneratedNever();
        entity.Property(@event => @event.Kind)
            .HasColumnName(DiscoveryRunEventEntity.KindColumnName)
            .HasMaxLength(DiscoveryRunEventEntity.KindLengthLimit)
            .IsRequired();
        entity.Property(@event => @event.Payload)
            .HasColumnName(DiscoveryRunEventEntity.PayloadColumnName)
            .HasColumnType("jsonb")
            .IsRequired();
        entity.Property(@event => @event.WrittenAt)
            .HasColumnName(DiscoveryRunEventEntity.WrittenAtColumnName);

        entity.HasOne<DiscoveryRunEntity>()
            .WithMany()
            .HasForeignKey(@event => @event.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
