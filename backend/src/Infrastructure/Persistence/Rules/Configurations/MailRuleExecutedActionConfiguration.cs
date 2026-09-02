// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Rules.Configurations;

/// <summary>Declares the individual changes one rule execution asked for, and the mutation record each one went into.</summary>
/// <remarks>
/// A table rather than a column on the execution, because these rows are the join between a rule's decision and what
/// happened on the mailbox. They cascade from the execution and are append-only like it, so none of them carries a
/// concurrency token: what a rule asked for cannot outlive the record of the rule concluding it, and nothing amends
/// either.
/// </remarks>
internal sealed class MailRuleExecutedActionConfiguration : IEntityTypeConfiguration<MailRuleExecutedActionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailRuleExecutedActionEntity> entity)
    {
        entity.ToTable("mail_rule_executed_actions");

        // The pair is the key rather than a surrogate, because one rule declares one change at one position however
        // many times the pass reads it. That makes the uniqueness the identity instead of a constraint beside one.
        entity.HasKey(action => new { action.MailRuleExecutionId, action.Position });

        entity.Property(action => action.Mutation)
            .HasMaxLength(MailRuleExecutionEntity.MaximumOutcomeLength)
            .IsRequired();
        entity.Property(action => action.Outcome)
            .HasMaxLength(MailRuleExecutionEntity.MaximumOutcomeLength)
            .IsRequired();
        entity.Property(action => action.FailureReason)
            .HasMaxLength(MailRuleExecutionEntity.MaximumOutcomeLength);
        entity.Property(action => action.Destination)
            .HasMaxLength(MailRuleExecutionEntity.MaximumAliasLength);

        entity.HasOne<MailRuleExecutionEntity>()
            .WithMany(execution => execution.Actions)
            .HasForeignKey(action => action.MailRuleExecutionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
