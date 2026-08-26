// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Jobs.Configurations;

/// <summary>Declares the queue of durable background work, and the three questions it is asked.</summary>
/// <remarks>
/// <para>
/// The unique index is the idempotency guarantee itself rather than a support for one. Two triggers asking for the
/// same execution at the same moment both pass any check the application could make between reading and writing, and
/// only the database closes that window; the same work is therefore enqueued once because the second insert is
/// refused, not because the code declined to attempt it. It spans every state a row can reach, terminal ones
/// included, because a row that succeeded is exactly what stops the same trigger asking again — which is also why a
/// row is never moved to another table when it is finished with, and why pruning is a retention decision with a
/// correctness floor rather than housekeeping.
/// </para>
/// <para>
/// The claim index carries the type and the instant a job's turn comes, because the claim statement is the only query
/// this table runs at any volume and those are what it selects and orders on. It is filtered to the states a claim can
/// still take, so a queue that has been running for a year holds an index the size of its backlog rather than of its
/// history — and the claim repeats that same membership in its own predicate so PostgreSQL can prove the index
/// applies to it. Naming the two claimable states rather than excluding the terminal ones is what keeps the filter
/// correct as terminal states are added: a job that failed leaves the index the moment it stops being claimable.
/// </para>
/// <para>
/// The turn rather than the available instant, because the order a claim drains the queue in is what decides whether
/// one owner's backlog postpones another owner's due work. The available instant stays a predicate — it is what makes
/// a job due — and the turn is what orders the jobs that are.
/// </para>
/// <para>
/// The account is a column with an index of its own rather than a value inside the payload, because erasure,
/// retention, and any per-account bound have to reach a job by query. The foreign key is what makes that structural:
/// removing an account takes its queued work with it instead of leaving rows pointing at a mailbox that is gone. A
/// job belonging to no account leaves it null.
/// </para>
/// <para>
/// Nothing here is mail content. A job type, an idempotency key composed of MailFathom's own names, an account
/// identifier, a lease owner, and a document of references are what the row holds, which is what lets work be queued
/// without the message being copied into a second place with retention obligations of its own.
/// </para>
/// </remarks>
internal sealed class JobConfiguration : IEntityTypeConfiguration<JobEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<JobEntity> entity)
    {
        // A queue row names an account and its owner or neither, which the check states because the foreign key
        // cannot: both columns are optional, so PostgreSQL leaves a row supplying only one of them unchecked, and a
        // row carrying an identifier without an owner would then reference a mailbox nothing resolved.
        entity.ToTable(
            "jobs",
            table => table.HasCheckConstraint(
                PersistenceConstraintNames.JobAccountOwnerCheckConstraintName,
                $"(\"{nameof(JobEntity.OwnerId)}\" IS NULL) = (\"{nameof(JobEntity.MailboxAccountId)}\" IS NULL)"));
        entity.HasKey(job => job.Id);
        entity.Property(job => job.Id).ValueGeneratedNever();
        entity.Property(job => job.JobType).HasMaxLength(64).IsRequired();
        entity.Property(job => job.IdempotencyKey)
            .HasMaxLength(JobIdempotencyKey.MaximumLength)
            .IsRequired();

        // A document rather than a schema: nothing queries into it, because the key, the type, the account, and the
        // available instant are all columns beside it.
        entity.Property(job => job.Payload).HasColumnType("jsonb").IsRequired();

        entity.Property(job => job.MailboxAccountId).HasMaxLength(128);
        entity.Property(job => job.LeaseOwner).HasMaxLength(JobLeaseOwner.MaximumLength);

        // Nothing queries by either, and nothing indexes them: they are read back with the row a claim already
        // selected, and their only reader is the link put on that attempt's span.
        entity.Property(job => job.EnqueuedTraceParent).HasMaxLength(JobTraceContext.MaximumTraceParentLength);
        entity.Property(job => job.EnqueuedTraceState).HasMaxLength(JobTraceContext.MaximumTraceStateLength);

        // Stored as text for the reason every other bounded value in this schema is: it stays readable in an ad-hoc
        // query and survives any later reordering of the enum.
        entity.Property(job => job.State).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(job => job.LastFailureClassification).HasConversion<string>().HasMaxLength(64);
        entity.Property(job => job.LastFailureReason).HasMaxLength(JobFailureRecord.MaximumReasonLength);

        entity.HasIndex(job => new { job.JobType, job.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.JobIdentityUniqueIndexName);
        entity.HasIndex(job => new { job.JobType, job.TurnAt })
            .HasDatabaseName(PersistenceConstraintNames.JobClaimIndexName)
            .HasFilter(
                $"\"{nameof(JobEntity.State)}\" IN ('{nameof(JobState.Pending)}', '{nameof(JobState.Claimed)}')");
        entity.HasIndex(job => new { job.OwnerId, job.MailboxAccountId, job.EnqueuedAt })
            .HasDatabaseName(PersistenceConstraintNames.JobAccountIndexName);

        // Filtered to the same states the claim index is, and for the same reason: what an enqueue asks is where the
        // owner's *waiting* work has reached, so a queue that has been running for a year reads a structure the size of
        // its backlog. The owner leads and no account column follows it, because the owner is a column on this row now:
        // the latest turn is one descending step into this index rather than a maximum over each of the owner's
        // accounts, which is what the enqueue had to compose while the owner could only be reached through a join.
        entity.HasIndex(job => new { job.OwnerId, job.TurnAt })
            .HasDatabaseName(PersistenceConstraintNames.JobOwnerTurnIndexName)
            .HasFilter(
                $"\"{nameof(JobEntity.State)}\" IN ('{nameof(JobState.Pending)}', '{nameof(JobState.Claimed)}')");

        // Partial for the reason the claim index is: the state it is filtered to is a small part of a table that
        // grows with every enqueue, and an operator reading what has stopped orders by the instant it stopped. The
        // ordering columns are the keyset pair the page is continued on, so one index serves the reading whichever
        // of its two optional filters is applied.
        entity.HasIndex(job => new { job.StateChangedAt, job.Id })
            .HasDatabaseName(PersistenceConstraintNames.JobDeadLetterIndexName)
            .HasFilter($"\"{nameof(JobEntity.State)}\" = '{nameof(JobState.DeadLettered)}'");

        // The reference is the pair, an account being identified by its owner and its identifier together. Both
        // columns are optional, so PostgreSQL enforces the constraint only on a row supplying both — which is why the
        // table above carries the check that states the invariant the enforcement rests on.
        entity.HasOne(job => job.MailboxAccount)
            .WithMany()
            .HasForeignKey(job => new { job.OwnerId, job.MailboxAccountId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
