// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Discovery.Configurations;

/// <summary>Declares the Discover runs the deployment is executing and the answers they have not yet been forgotten for.</summary>
/// <remarks>
/// <para>
/// The identifier is the key, because a client reads a run by the identifier it was handed and every other statement
/// reaches one the same way. The column names are the entity's own constants because every statement is composed, so
/// the statements and this mapping name the same things by construction.
/// </para>
/// <para>
/// <strong>The only index beside the key is the foreign key's own</strong>, which the bound on one person's concurrent
/// runs counts through and which an erasure reaches a person's runs by. The retention sweep reads the two instants and
/// is deliberately left to a scan: what the table holds is the runs currently executing plus whatever ended inside a
/// five-minute window, so it is small by construction — while an index on the instant that sweep would use is one
/// rewritten on every read of every run, which is the one thing a watched run does constantly. The write it would cost
/// is paid on every read; the read it would save is paid once every five minutes.
/// </para>
/// </remarks>
internal sealed class DiscoveryRunConfiguration : IEntityTypeConfiguration<DiscoveryRunEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DiscoveryRunEntity> entity)
    {
        entity.ToTable(DiscoveryRunEntity.TableName);
        entity.HasKey(run => run.Id);
        entity.Property(run => run.Id)
            .HasColumnName(DiscoveryRunEntity.IdColumnName)
            .ValueGeneratedNever();
        entity.Property(run => run.UserId)
            .HasColumnName(DiscoveryRunEntity.UserIdColumnName);
        entity.Property(run => run.StartedAt)
            .HasColumnName(DiscoveryRunEntity.StartedAtColumnName);
        entity.Property(run => run.LastUsedAt)
            .HasColumnName(DiscoveryRunEntity.LastUsedAtColumnName);
        entity.Property(run => run.EndedAt)
            .HasColumnName(DiscoveryRunEntity.EndedAtColumnName);
        entity.Property(run => run.StopRequestedAt)
            .HasColumnName(DiscoveryRunEntity.StopRequestedAtColumnName);
        // Cascade rather than a statement in the erasure walk, for the reason the signal tickets carry one: a run
        // answers one person's question about their own mail and names nobody else, so it goes when they do without an
        // erasure having to know this table exists — and what goes with it is everything the run composed, through the
        // events' own cascade from here. What it costs is one index on the user, which is also what the bound on one
        // person's concurrent runs is counted through.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(run => run.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
