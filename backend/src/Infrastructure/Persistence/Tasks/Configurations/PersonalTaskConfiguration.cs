// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Tasks;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Tasks.Configurations;

/// <summary>Declares what a person owes, and the one way a row leaves again.</summary>
/// <remarks>
/// <para>
/// The user's own row takes their tasks with them, so an erasure request never has to know this table exists — which
/// is also what makes the row reachable by an erasure at all, since this table names no mail account and the walk
/// enumerates the tables that do. That is the only cascade here, and the message a task cites is deliberately not a
/// second one: it is a plain value carrying no foreign key, for the reason
/// <c>mailbox_mutation_audit_entries</c> keeps its own message that way. A commitment read out of a thread is still a
/// commitment once the thread is gone, so erasing the mail must leave the task standing — while every association to a
/// stored message's identity cascades but for a reply's own parent, which <c>StoredEmailModelTests</c> holds the model
/// to. A reader resolving a citation whose message has been erased finds nothing under that identity, exactly as a job
/// whose payload names erased mail does.
/// </para>
/// <para>
/// What makes the row personal data is the title, which may be a sentence read out of a body. Nothing else here is
/// mail: the due date is a day, the origin and the completion are MailFathom's own state, and the message is cited by
/// identifier rather than copied.
/// </para>
/// </remarks>
internal sealed class PersonalTaskConfiguration : IEntityTypeConfiguration<PersonalTaskEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<PersonalTaskEntity> entity)
    {
        entity.ToTable("tasks");
        entity.HasKey(task => task.Id);
        entity.Property(task => task.Id).ValueGeneratedNever();

        // Stored as text for the reason every other enum in this model is: it stays readable in an ad-hoc query and
        // survives any later reordering of its enum.
        entity.Property(task => task.Origin).HasConversion<string>().HasMaxLength(64).IsRequired();

        entity.Property(task => task.Title).HasMaxLength(PersonalTask.MaximumTitleLength).IsRequired();

        // Cascade rather than a statement in the erasure walk, for the reason the notification table cascades: what a
        // person owes belongs to them and goes when they do.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(task => task.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The one index the list is walked through, in the order the list is drawn: one person's tasks, soonest due
        // first, with PostgreSQL's own NULLS LAST putting the undated ones at the end and the identifier breaking a
        // tie. Reading the proposals apart from the commitments filters within one person's rows, which is a handful
        // of them, so the origin earns no column of its own here.
        entity.HasIndex(task => new { task.UserId, task.DueOn, task.Id })
            .HasDatabaseName(PersistenceConstraintNames.PersonalTaskDueOrderIndexName);
    }
}
