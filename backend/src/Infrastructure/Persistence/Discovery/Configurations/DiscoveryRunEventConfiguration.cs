// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Discovery.Configurations;

/// <summary>Declares what one Discover run has written, in the order it wrote it.</summary>
/// <remarks>
/// <para>
/// The run and the sequence together are the key, which is both the promise the run's contract makes and the order its
/// one reader walks — everything of one run after a cursor — so no index stands beside it. The cascade from the run is
/// what makes the retention removal one statement against the runs alone.
/// </para>
/// <para>
/// <strong>The payload is <c>json</c> rather than <c>jsonb</c>, and the difference is the whole of the column.</strong>
/// Both refuse a value that is not a document, which is why the column is neither text nor a blob; only <c>json</c>
/// stores the one it was handed. <c>jsonb</c> parses the document and writes a normalized form back, ordering the keys
/// by length and then bytewise — and an event is a polymorphic document whose type discriminator the serializer refuses
/// to read anywhere but first. A block event is where the two meet: <c>block</c> is as long as <c>event</c> and sorts
/// before it, so a normalized row hands the reader a document it cannot identify and the run is lost on the way back.
/// Nothing queries inside the column, the events being read back whole, so the indexing <c>jsonb</c> buys is worth
/// nothing against that.
/// </para>
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
            .HasColumnType("json")
            .IsRequired();
        entity.Property(@event => @event.WrittenAt)
            .HasColumnName(DiscoveryRunEventEntity.WrittenAtColumnName);

        entity.HasOne<DiscoveryRunEntity>()
            .WithMany()
            .HasForeignKey(@event => @event.RunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
