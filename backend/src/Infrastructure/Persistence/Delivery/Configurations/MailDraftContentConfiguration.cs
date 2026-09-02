// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Storage;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares the raw MIME the current revision of one draft is held as.</summary>
/// <remarks>
/// <para>
/// A one-to-one table whose primary key is also its foreign key, which is the arrangement both other content tables
/// use and for the same reason: keeping the large binary value out of the record means listing what is held never
/// loads a single message's bytes.
/// </para>
/// <para>
/// One row per draft rather than per revision, which is the one place a raw-MIME row is rewritten rather than
/// written once. A send's payload is fixed because a retry has to transmit the bytes an earlier attempt may already
/// have begun transmitting; a draft's payload is what its author is still editing, and keeping every version would
/// hold a message per keystroke for as long as the draft lives.
/// </para>
/// <para>
/// The cascade is the erasure obligation: deleting the draft destroys the message it points at, so a draft's
/// message cannot outlive the record that says whose it is.
/// </para>
/// </remarks>
internal sealed class MailDraftContentConfiguration : IEntityTypeConfiguration<MailDraftContentEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailDraftContentEntity> entity)
    {
        entity.ToTable(
            "mail_draft_contents",
            table => table.HasCheckConstraint(
                "ck_mail_draft_contents_backend_payload",
                """
                ("Backend" = 'Database' AND "RawMime" IS NOT NULL AND "ObjectLocator" IS NULL AND "ObjectVerifiedAt" IS NULL)
                OR ("Backend" = 'ObjectStorage' AND "ObjectLocator" IS NOT NULL
                    AND ("RawMime" IS NULL OR "ObjectVerifiedAt" IS NOT NULL))
                """));
        entity.HasKey(content => content.MailDraftId);
        entity.Property(content => content.MailDraftId).ValueGeneratedNever();
        entity.Property(content => content.Backend)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired()
            .HasDefaultValue(ContentStorageBackend.Database);
        entity.Property(content => content.ObjectLocator).HasMaxLength(1024);
        entity.Property(content => content.Sha256Hash).HasMaxLength(32).IsRequired();

        entity.HasOne(content => content.MailDraft)
            .WithOne(draft => draft.Content)
            .HasForeignKey<MailDraftContentEntity>(content => content.MailDraftId)
            .HasConstraintName(PersistenceConstraintNames.MailDraftContentForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);

        // The index both readers of the object backend meet, and unique because a key is minted by the write that
        // produced it: the readiness census asks whether any row here names an object at all, and the sweep for objects
        // nothing points at asks whether any row names each of a listed page of keys. Filtered to the object backend so
        // a deployment that configured no endpoint answers either from an empty index rather than by scanning the table.
        entity.HasIndex(content => content.ObjectLocator)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailDraftContentObjectLocatorUniqueIndexName)
            .HasFilter($"\"Backend\" = '{nameof(ContentStorageBackend.ObjectStorage)}'");
    }
}
