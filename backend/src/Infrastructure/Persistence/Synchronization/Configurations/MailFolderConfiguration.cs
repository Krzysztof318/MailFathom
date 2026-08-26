// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
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
        // The owner leads it for the reason it leads every other account-narrowed structure: an account identifier
        // names one account within its owner, so uniqueness is a statement about one owner's mailbox rather than about
        // a deployment-wide namespace. Nothing that bound before this change is refused after it, the owner being
        // functionally determined by the account it is read from.
        entity.HasIndex(
                folder => new
                {
                    folder.OwnerId,
                    folder.MailboxAccountId,
                    folder.Alias,
                    folder.ResolutionGeneration,
                })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailFolderBindingUniqueIndexName);
        // The reference is the pair, because that is what identifies an account: a key naming the identifier alone
        // would point at one mailbox in one owner's hands and at a different one in another's. The unique index
        // above already leads with the same two columns, so the constraint needs no structure of its own.
        entity.HasOne(folder => folder.MailboxAccount)
            .WithMany(account => account.MailFolders)
            .HasForeignKey(folder => new { folder.OwnerId, folder.MailboxAccountId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
