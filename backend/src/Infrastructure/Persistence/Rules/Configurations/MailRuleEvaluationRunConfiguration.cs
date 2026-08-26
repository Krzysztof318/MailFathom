// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Rules.History;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Rules.Configurations;

/// <summary>Declares the outstanding whole-mailbox rule run of one account.</summary>
/// <remarks>
/// One row per account, which is what makes "one outstanding whole-mailbox rule run" a property of the key rather than
/// of a check. The ending is stored as text for the reason every other outcome here is: it stays readable in an ad-hoc
/// query and survives a later reordering of the enum.
/// </remarks>
internal sealed class MailRuleEvaluationRunConfiguration : IEntityTypeConfiguration<MailRuleEvaluationRunEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailRuleEvaluationRunEntity> entity)
    {
        entity.ToTable("mail_rule_evaluation_runs");
        // One row per account, and an account is its owner and its identifier together: the key leads with the owner
        // so that "one outstanding whole-mailbox rule run" is a statement about one person's mailbox.
        entity.HasKey(run => new { run.OwnerId, run.MailboxAccountId })
            .HasName(PersistenceConstraintNames.MailRuleEvaluationRunPrimaryKeyConstraintName);
        entity.Property(run => run.MailboxAccountId).HasMaxLength(128).ValueGeneratedNever();
        entity.Property(run => run.Revision)
            .HasMaxLength(MailRuleEvaluationRunEntity.RevisionLength)
            .IsFixedLength();
        entity.Property(run => run.Ending).HasConversion<string>().HasMaxLength(64);
        entity.Property(run => run.Trigger)
            .HasConversion<string>()
            .HasMaxLength(64)
            .HasDefaultValue(MailRuleExecutionTrigger.RequestedRun)
            .IsRequired();

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(run => run.ConcurrencyVersion).IsRowVersion();
    }
}
