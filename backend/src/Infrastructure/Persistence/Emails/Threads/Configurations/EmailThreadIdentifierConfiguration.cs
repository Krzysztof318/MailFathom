// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Threads.Configurations;

/// <summary>Declares what binds one message identifier of one account to the conversation it belongs to.</summary>
/// <remarks>
/// <para>
/// The key is the whole row bar the thread it points at, which is what makes assembly idempotent without a
/// read-then-write: an arrival that re-registers an identifier it already registered is refused by the key rather than
/// duplicated, and a genuine race between two first arrivals is reported as the conflict it is.
/// </para>
/// <para>
/// The table cascades from the thread and, through it, from the account: a conversation is an assembly of one account's
/// mail and outlives none of it.
/// </para>
/// </remarks>
internal sealed class EmailThreadIdentifierConfiguration : IEntityTypeConfiguration<EmailThreadIdentifierEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailThreadIdentifierEntity> entity)
    {
        entity.ToTable("email_thread_identifiers");
        entity.Property(identifier => identifier.MailboxAccountId).HasMaxLength(128);
        // Bounded rather than declared fixed-length, although every value this column ever holds is exactly that
        // long. A blank-padded PostgreSQL `character(n)` compares by its own rules and is the type every other
        // bounded string in this model deliberately is not, and the width is already guaranteed by the digest that
        // produces the value rather than by the column that stores it.
        entity.Property(identifier => identifier.IdentifierHash)
            .HasMaxLength(EmailThreadIdentifierEntity.IdentifierHashLength);

        // The owner leads the key, so the binding is one message identifier of one account of one owner. Without it
        // two owners whose mailboxes carry the same identifier would compete for a single row and one arrival would
        // be threaded into the other's conversation.
        entity.HasKey(identifier => new { identifier.OwnerId, identifier.MailboxAccountId, identifier.IdentifierHash })
            .HasName(PersistenceConstraintNames.EmailThreadIdentifierPrimaryKeyConstraintName);

        entity.HasIndex(identifier => identifier.EmailThreadId)
            .HasDatabaseName(PersistenceConstraintNames.EmailThreadIdentifierThreadIndexName);

        entity.HasOne<EmailThreadEntity>()
            .WithMany()
            .HasForeignKey(identifier => identifier.EmailThreadId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
