// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Accounts.Configurations;

/// <summary>Declares the account row every folder binding, message, and durable job hangs on.</summary>
/// <remarks>
/// <para>
/// The row carries the configured alias and nothing else: what is known about an account beyond its identity is
/// configuration, which is read-only, so the table exists to give the rows that reference an account something to
/// reference rather than to hold state of its own.
/// </para>
/// <para>
/// It is keyed by the owner and the identifier together, which is what ADR 0014 decided an account is identified by.
/// The identifier stays the readable string whoever declared the account wrote, and it names one account within its
/// owner rather than across the deployment — so two people served by one instance may each call a mailbox
/// <c>work</c> and neither is claiming the word from the other.
/// </para>
/// </remarks>
internal sealed class MailboxAccountConfiguration : IEntityTypeConfiguration<MailboxAccountEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailboxAccountEntity> entity)
    {
        entity.ToTable("mailbox_accounts");

        // The account row is created by whichever run first binds one of the account's folders, so two overlapping
        // first runs insert it together and one of them loses. The key is therefore named for the same reason the
        // alias binding index below is: the loser is recognized by the constraint it violated and reported as a
        // race to resolve rather than as a failure.
        //
        // The owner leads it, so the key is also the structure that answers which mail accounts one owner owns —
        // the read the erasure performs before it takes the rows no cascade reaches, and the reason the foreign key
        // below needs no index of its own.
        entity.HasKey(account => new { account.OwnerId, account.Id })
            .HasName(PersistenceConstraintNames.MailboxAccountPrimaryKeyConstraintName);
        entity.Property(account => account.Id).HasMaxLength(128);

        // The owner is required, so a mailbox belongs to somebody from the moment its row exists rather than from the
        // moment something remembers to say so. The cascade is what makes erasing an owner one statement: the mail
        // graph hangs off this table, so the account rows take their folders, and the folders take everything derived
        // from the mail beneath them.
        entity.HasOne<OwnerAccountEntity>()
            .WithMany()
            .HasForeignKey(account => account.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
