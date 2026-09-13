// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.StoredFiles.Configurations;

/// <summary>Declares the row one stored file is recorded by.</summary>
/// <remarks>
/// The backend, payload, and locator columns are declared exactly as the content tables declare theirs, under the same
/// check constraint, because the move and the release read and rewrite a file as one more payload kind.
/// </remarks>
internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFileEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StoredFileEntity> entity)
    {
        entity.ToTable(
            "stored_files",
            table => table.HasCheckConstraint(
                "ck_stored_files_backend_payload",
                """
                ("Backend" = 'Database' AND "Content" IS NOT NULL AND "ObjectLocator" IS NULL AND "ObjectVerifiedAt" IS NULL)
                OR ("Backend" = 'ObjectStorage' AND "ObjectLocator" IS NOT NULL
                    AND ("Content" IS NULL OR "ObjectVerifiedAt" IS NOT NULL))
                """));
        entity.HasKey(file => file.Id);
        entity.Property(file => file.Id).ValueGeneratedNever();
        entity.Property(file => file.MediaType).HasMaxLength(StoredFileEntity.MaximumMediaTypeLength);
        entity.Property(file => file.Backend)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired()
            .HasDefaultValue(ContentStorageBackend.Database);
        entity.Property(file => file.ObjectLocator).HasMaxLength(1024);
        entity.Property(file => file.Sha256Hash).HasMaxLength(32).IsRequired();

        // Cascade rather than a statement in the erasure walk: a person's files are derived from them. The erasure still
        // reads the object keys before the user row goes, which is what reaches a file held in the bucket.
        entity.HasOne<UserAccountEntity>()
            .WithMany()
            .HasForeignKey(file => file.UserId)
            .HasConstraintName(PersistenceConstraintNames.StoredFileUserForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        // The index the readiness census and the sweep for objects nothing points at both meet, filtered to the object
        // backend so a deployment that configured no endpoint answers from an empty index.
        entity.HasIndex(file => file.ObjectLocator)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.StoredFileObjectLocatorUniqueIndexName)
            .HasFilter($"\"Backend\" = '{nameof(ContentStorageBackend.ObjectStorage)}'");
    }
}
