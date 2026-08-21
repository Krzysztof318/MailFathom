// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Jobs.Configurations;

/// <summary>Declares what each recurring dispatch has already done, which is the only state a schedule keeps.</summary>
/// <remarks>
/// One row per declared schedule, keyed by the identity the declaration composes, so a second replica advancing a
/// schedule writes the same row rather than adding one. The declarations themselves are configuration and are not
/// stored: what is durable here is the occasion last accounted for and the job it enqueued, which is what a restart
/// would otherwise have no way to tell from a fresh deployment.
/// </remarks>
internal sealed class JobScheduleConfiguration : IEntityTypeConfiguration<JobScheduleEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<JobScheduleEntity> entity)
    {
        entity.ToTable("job_schedules");
        entity.HasKey(schedule => schedule.ScheduleId);
        entity.Property(schedule => schedule.ScheduleId)
            .HasMaxLength(JobScheduleId.MaximumLength)
            .ValueGeneratedNever();
    }
}
