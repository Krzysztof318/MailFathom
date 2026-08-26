// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Rules.Configurations;

/// <summary>Declares the record of what each rule concluded about each email.</summary>
/// <remarks>
/// <para>
/// Two tables rather than one, and the split is the pointer rather than normalization for its own sake. The execution
/// states what a rule concluded; the rows beside it name the individual changes it asked for and the mutation record
/// each one went into, which is the join between a rule's decision and what happened on the mailbox.
/// </para>
/// <para>
/// <strong>No fact value is stored anywhere here.</strong> The facts a condition read are kept as their declared names,
/// and the expression itself stays in the configuration the recorded revision identifies. A rule name, a folder alias,
/// a mutation name, and a set of fact names are all MailFathom's own names for things, which is what lets a decision be
/// explained without the mail being copied into a second place.
/// </para>
/// <para>
/// The email is a foreign key with a cascade. That is what makes the history inherit the deletion obligations of the
/// mail it describes rather than merely undertaking to; the mutation record it points at is deliberately not one,
/// because the two records have retention windows of their own and a key would let the trail's window erase the history
/// with it.
/// </para>
/// <para>
/// It is append-only. Nothing amends an execution, so no row carries a concurrency token: there is no second writer for
/// one to protect against.
/// </para>
/// </remarks>
internal sealed class MailRuleExecutionConfiguration : IEntityTypeConfiguration<MailRuleExecutionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailRuleExecutionEntity> entity)
    {
        entity.ToTable("mail_rule_executions");
        entity.HasKey(execution => execution.Id);
        entity.Property(execution => execution.Id).ValueGeneratedNever();
        entity.Property(execution => execution.MailboxAccountId).HasMaxLength(128).IsRequired();
        entity.Property(execution => execution.RuleName)
            .HasMaxLength(MailRuleExecutionEntity.MaximumRuleNameLength)
            .IsRequired();
        entity.Property(execution => execution.Revision)
            .HasMaxLength(MailRuleExecutionEntity.RevisionLength)
            .IsFixedLength()
            .IsRequired();

        // The bounded values are held as their own names rather than as converted enums, for the reason the
        // answering record states: a converted enum fails materialization on a name it declares no member for, and
        // this record is read a page at a time, so a value a later build wrote would fail every page from there on.
        entity.Property(execution => execution.Trigger)
            .HasMaxLength(MailRuleExecutionEntity.MaximumOutcomeLength)
            .IsRequired();
        entity.Property(execution => execution.Outcome)
            .HasMaxLength(MailRuleExecutionEntity.MaximumOutcomeLength)
            .IsRequired();
        entity.Property(execution => execution.ConditionFailure)
            .HasMaxLength(MailRuleExecutionEntity.MaximumOutcomeLength);
        entity.Property(execution => execution.ReadFacts).IsRequired();

        // The cascade is the point of the association: an erased message reaches every rule decision that was made
        // about it, through the email's own deletion path rather than through a rule somebody remembers.
        entity.HasOne<StoredEmailEntity>()
            .WithMany()
            .HasForeignKey(execution => execution.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);

        // The three indexes are the three questions the history is asked. The first is also what retention erases
        // through, which is why the account leads it and the instant follows.
        entity.HasIndex(execution => new
        {
            execution.OwnerId,
            execution.MailboxAccountId,
            execution.EvaluatedAt,
            execution.Id,
        })
            .HasDatabaseName(PersistenceConstraintNames.MailRuleExecutionTimelineIndexName);
        entity.HasIndex(execution => new
        {
            execution.OwnerId,
            execution.MailboxAccountId,
            execution.RuleName,
            execution.EvaluatedAt,
            execution.Id,
        })
            .HasDatabaseName(PersistenceConstraintNames.MailRuleExecutionRuleIndexName);
        entity.HasIndex(execution => new { execution.StoredEmailId, execution.EvaluatedAt, execution.Id })
            .HasDatabaseName(PersistenceConstraintNames.MailRuleExecutionEmailIndexName);
    }
}
