// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Threads.Configurations;

/// <summary>Declares the conversations an account's stored mail is assembled into.</summary>
/// <remarks>
/// <para>
/// The thread row holds an identity and nothing derivable from its members, so no column here can disagree with the
/// emails that reference it. It cascades from the account, because a conversation is an assembly of that account's mail
/// and outlives none of it: erasing the account erases the threads with the messages.
/// </para>
/// <para>
/// The two pointers the email itself carries are declared with the rest of that row rather than here, and both are set
/// to null on delete rather than cascading: erasing one message must leave its answer readable as a root of what
/// remains rather than take the answer with it, and removing a conversation must leave its mail in place.
/// </para>
/// <para>
/// The survivor pointer is the third of them and is a constraint like the others, because a merged conversation is
/// still reachable by the identifier a tool published before the merge and resolving it is a walk this column decides.
/// It differs only in doing nothing on delete, since both ends of it are one account's and are erased in the same
/// statement.
/// </para>
/// </remarks>
internal sealed class EmailThreadConfiguration : IEntityTypeConfiguration<EmailThreadEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailThreadEntity> entity)
    {
        entity.ToTable("email_threads");
        entity.HasKey(thread => thread.Id);
        entity.Property(thread => thread.Id).ValueGeneratedNever();
        entity.Property(thread => thread.MailboxAccountId).HasMaxLength(128).IsRequired();
        entity.Property(thread => thread.ConcurrencyVersion).IsRowVersion();

        // The pair rather than the identifier, because an account is identified by both: the same word names one
        // mailbox within one owner and another within the next, so a conversation keyed onto the identifier alone
        // would hang on whichever of them the database happened to hold.
        entity.HasOne<MailboxAccountEntity>()
            .WithMany()
            .HasForeignKey(thread => new { thread.OwnerId, thread.MailboxAccountId })
            .OnDelete(DeleteBehavior.Cascade);

        // The survivor a merge points at is a row of this same table, and it is constrained rather than trusted: a
        // pointer at a conversation nothing holds would end the walk that resolves a published identifier at a row
        // that is nobody's survivor. Nothing is done on delete because nothing deletes one of these rows on its
        // own — both sides belong to one account and go together, in the statement that erases it.
        entity.HasOne<EmailThreadEntity>()
            .WithMany()
            .HasForeignKey(thread => thread.MergedIntoEmailThreadId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
