// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Coordination;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Coordination.Configurations;

/// <summary>Declares the table one unit of work at a time is held in, and the one question it is asked.</summary>
/// <remarks>
/// <para>
/// The primary key over the scope is the exclusion itself rather than a support for one. Two replicas asking for one
/// scope at the same instant both pass any check the application could make between reading and writing, and only the
/// database closes that window: one of the two inserts wins the key and the other is answered as a conflict, which the
/// claim's own <c>ON CONFLICT</c> clause then resolves against the expiry it read in the same statement.
/// </para>
/// <para>
/// No index beside it, because there is no second query. Every statement here names a scope, so the key answers all
/// four of them — including the administrative read, which names the set of scopes it is asking about and is a lookup
/// per scope against that key — and a table holding one row per currently held scope is bounded by how much singleton
/// work the deployment has rather than by how long it has been running.
/// </para>
/// <para>
/// Nothing here is mail content. A composed scope name, a generated hold identity, a replica identity, and two
/// instants are what the row holds, which is what lets exclusion be recorded without a second place carrying retention
/// obligations.
/// </para>
/// </remarks>
internal sealed class WorkLeaseConfiguration : IEntityTypeConfiguration<WorkLeaseEntity>
{
    /// <summary>What a hold taken before the replica was recorded reads as, which is the only row the column's default reaches.</summary>
    internal const string UnknownReplica = "unknown";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WorkLeaseEntity> entity)
    {
        entity.ToTable("work_leases");
        entity.HasKey(lease => lease.Scope);
        entity.Property(lease => lease.Scope).HasMaxLength(WorkScope.MaximumLength);
        entity.Property(lease => lease.Holder).HasMaxLength(WorkLeaseHolder.MaximumLength).IsRequired();
        // The default is for the rows that already exist when the column is added, and for nothing else: every claim
        // names the replica outright, so what it covers is a hold taken by a replica of the release before this one.
        // Those rows expire within a lease duration, and until they do an operator is told the holder is unknown rather
        // than shown a blank.
        entity.Property(lease => lease.Replica)
            .HasMaxLength(ReplicaIdentity.MaximumLength)
            .HasDefaultValue(UnknownReplica)
            .IsRequired();
    }
}
