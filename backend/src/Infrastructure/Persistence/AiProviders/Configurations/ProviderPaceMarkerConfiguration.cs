// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.AiProviders;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.AiProviders.Configurations;

/// <summary>Declares when each paced workload of this deployment may send its next provider request.</summary>
/// <remarks>
/// Keyed by the workload's own name, because a rate belongs to a workload against a quota rather than to an account or
/// a user. Nothing hangs off the row and nothing cascades into it: what it records is when this deployment may next
/// send, which is true of the deployment and of nobody in it. The column names are the entity's own constants because
/// the reservation is a composed statement, so the statement and this mapping name the same things by construction.
/// </remarks>
internal sealed class ProviderPaceMarkerConfiguration : IEntityTypeConfiguration<ProviderPaceMarkerEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProviderPaceMarkerEntity> entity)
    {
        entity.ToTable(ProviderPaceMarkerEntity.TableName);
        entity.HasKey(marker => marker.Workload);
        entity.Property(marker => marker.Workload)
            .HasColumnName(ProviderPaceMarkerEntity.WorkloadColumnName)
            .HasMaxLength(ProviderPacedWorkloads.MaxNameLength)
            .ValueGeneratedNever();
        entity.Property(marker => marker.NextSlotAt)
            .HasColumnName(ProviderPaceMarkerEntity.NextSlotAtColumnName);
    }
}
