// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Synchronization.Configurations;

/// <summary>Declares the remote folders an account's mail is read from, and the alias binding an occurrence stays attributable through.</summary>
internal sealed class MailFolderConfiguration : IEntityTypeConfiguration<MailFolderEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailFolderEntity> entity)
    {
        entity.ToTable("mail_folders");
        entity.HasKey(folder => folder.Id);
        entity.Property(folder => folder.MailboxAccountId).HasMaxLength(128);
        entity.Property(folder => folder.Alias).HasMaxLength(128);
        entity.Property(folder => folder.RemotePath).HasMaxLength(512);
        entity.Property(folder => folder.HierarchyDelimiter).HasMaxLength(1);

        // The alias is unique per generation rather than per account, because every binding of an alias is kept:
        // its occurrences stay attributable to the remote folder they were actually read from.
        // The index is named, because a losing writer is recognized by the constraint its insert violated: two
        // runs binding the same alias for the first time is a race to resolve, not a failure to report.
        // The account leads it and no user stands ahead of it: the identifier is generated and names one mailbox
        // across the deployment, so uniqueness here is a statement about the mailbox rather than about one reader's
        // view of it, and a second user assigned the account binds no second folder.
        entity.HasIndex(
                folder => new
                {
                    folder.MailboxAccountId,
                    folder.Alias,
                    folder.ResolutionGeneration,
                })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailFolderBindingUniqueIndexName);

        // The reference is the identifier alone, which is the whole of what identifies an account now that the
        // deployment generates it. The unique index above already leads with the same column, so the constraint
        // needs no structure of its own.
        entity.HasOne(folder => folder.MailboxAccount)
            .WithMany(account => account.MailFolders)
            .HasForeignKey(folder => folder.MailboxAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
