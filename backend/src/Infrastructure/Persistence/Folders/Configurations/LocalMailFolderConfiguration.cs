// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Folders.Configurations;

/// <summary>Declares the folders MailFathom keeps for an account whose mailbox it holds.</summary>
internal sealed class LocalMailFolderConfiguration : IEntityTypeConfiguration<LocalMailFolderEntity>
{
    private const string LiveFolderFilter = $"\"{nameof(LocalMailFolderEntity.ErasedAt)}\" IS NULL";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<LocalMailFolderEntity> entity)
    {
        entity.ToTable("local_mail_folders");
        entity.HasKey(folder => folder.Id);
        entity.Property(folder => folder.Id).ValueGeneratedNever();
        entity.Property(folder => folder.MailboxAccountId).HasMaxLength(128);
        entity.Property(folder => folder.Name).HasMaxLength(LocalMailFolderName.MaximumLength);
        entity.Property(folder => folder.NameKey).HasMaxLength(LocalMailFolderName.MaximumLength);
        entity.Property(folder => folder.Role).HasConversion<string>().HasMaxLength(64);
        entity.Property(folder => folder.SourceFolderAlias).HasMaxLength(128);

        // The rule that two siblings never share a name, written where a second writer cannot get past it. Only live
        // folders take part, so an erased folder's name is free again the moment it commits, and a null parent is one
        // value rather than many, so the top of the hierarchy is a set of siblings like any other. The index is named,
        // because two edits creating the same name at once is a race whose loser decides again and is refused.
        entity.HasIndex(folder => new { folder.MailboxAccountId, folder.ParentId, folder.NameKey })
            .IsUnique()
            .AreNullsDistinct(false)
            .HasFilter(LiveFolderFilter)
            .HasDatabaseName(PersistenceConstraintNames.LocalMailFolderSiblingNameUniqueIndexName);

        // One folder per protected role, for the reason the name index is named: the first act on a held account and an
        // arrival for it can both supply the five at once.
        entity.HasIndex(folder => new { folder.MailboxAccountId, folder.Role })
            .IsUnique()
            .HasFilter($"\"{nameof(LocalMailFolderEntity.Role)}\" IS NOT NULL AND {LiveFolderFilter}")
            .HasDatabaseName(PersistenceConstraintNames.LocalMailFolderRoleUniqueIndexName);

        // Unfiltered, because both indexes above are partial and a partial index covers no foreign key: this is what the
        // account's cascade and every read of one account's folders walk.
        entity.HasIndex(folder => new { folder.MailboxAccountId, folder.ErasedAt });

        entity.HasOne<MailboxAccountEntity>()
            .WithMany()
            .HasForeignKey(folder => folder.MailboxAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // No action rather than a cascade or a restriction: an account's erasure removes its whole hierarchy in one
        // statement, which a restriction checked row by row would refuse, and nothing else removes a parent before its
        // children.
        entity.HasOne<LocalMailFolderEntity>()
            .WithMany()
            .HasForeignKey(folder => folder.ParentId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
