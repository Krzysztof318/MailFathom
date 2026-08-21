// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Delivery.Filing;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Delivery.Configurations;

/// <summary>Declares the copies of one outgoing message this deployment put into folders of its own mailbox.</summary>
/// <remarks>
/// <para>
/// One row per role rather than per append, which is what makes filing idempotent without a read-then-write: the key
/// is the record and the role, so a second attempt to file the same message into the same role is refused by the key
/// rather than putting a second copy in the mailbox. That is the duplication no local correction can withdraw for
/// the owner, since a copy in their sent folder is a message they will read as one they sent twice.
/// </para>
/// <para>
/// The row is written before the <c>APPEND</c> goes out and completed after it, which is why the stage is a column
/// rather than the presence of a placement. A process that died between the command and its answer left a row
/// saying a copy may be there, and nothing appends again on the strength of it.
/// </para>
/// <para>
/// The account and the path are copied here from the binding rather than joined to it, because the query that reads
/// this table runs once per synchronized batch and leads with both. The placement columns are nullable together:
/// a server without <c>UIDPLUS</c> answers an append with no identity at all, and the <c>Message-ID</c> beside them
/// is what the copy is recognized by there.
/// </para>
/// <para>
/// The cascade is the erasure obligation the content table carries: erasing the record of a send erases what it
/// says about the mailbox with it. The copy in the mailbox is the mailbox's own and stays where it is.
/// </para>
/// </remarks>
internal sealed class OutgoingEmailFilingConfiguration : IEntityTypeConfiguration<OutgoingEmailFilingEntity>
{
    /// <summary>What both filing indexes are filtered to, which is exactly the rows the join they serve can match.</summary>
    private const string JoinableFilingIndexFilter =
        $"\"{nameof(OutgoingEmailFilingEntity.ObservedAt)}\" IS NULL "
        + $"AND \"{nameof(OutgoingEmailFilingEntity.Stage)}\" = '{nameof(OutgoingMailFilingStage.Confirmed)}'";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutgoingEmailFilingEntity> entity)
    {
        entity.ToTable("outgoing_email_filings");
        entity.HasKey(filing => new { filing.OutgoingEmailId, filing.Filing })
            .HasName(PersistenceConstraintNames.OutgoingEmailFilingPrimaryKeyConstraintName);

        // The filing's own published name, which is what the closed enumeration is; stored as itself for the reason
        // the mutation name is, and bounded rather than free so a row can never name something longer than a value.
        entity.Property(filing => filing.Filing).HasMaxLength(64);
        entity.Property(filing => filing.MailboxAccountId).HasMaxLength(128).IsRequired();
        entity.Property(filing => filing.FolderAlias).HasMaxLength(128).IsRequired();
        entity.Property(filing => filing.FolderPath)
            .HasMaxLength(OutgoingEmailFilingEntity.MaximumFolderPathLength)
            .IsRequired();
        entity.Property(filing => filing.InternetMessageId)
            .HasMaxLength(OutgoingEmailFilingEntity.MaximumInternetMessageIdLength);

        // Stored as text for the reason every other enum on this feature is: it stays readable in an ad-hoc audit
        // query and survives any later reordering of the enum.
        entity.Property(filing => filing.Stage).HasConversion<string>().HasMaxLength(64).IsRequired();

        // A filing row is completed and withdrawn on its own, without the record above it changing, so the record's
        // token would not notice two passes settling one copy differently.
        entity.Property(filing => filing.ConcurrencyVersion).IsRowVersion();

        // The join a synchronized batch runs, filtered to exactly the rows that join can still match. A copy is met
        // once, and stamping it observed is what takes it out of both this structure and the work the join does;
        // the stage is the other half of the same bound, because a row is only ever a candidate while it is
        // confirmed. Without it a mirror withdrawn before any run saw it, and an append the server never answered,
        // would each leave a row nothing can match sitting in both structures for the life of the deployment —
        // which would make them grow with everything ever sent rather than with what is in flight.
        entity.HasIndex(filing => new
        {
            filing.MailboxAccountId,
            filing.FolderPath,
            filing.PlacementUidValidity,
            filing.PlacementUid,
        })
            .HasDatabaseName(PersistenceConstraintNames.OutgoingEmailFilingPlacementIndexName)
            .HasFilter(JoinableFilingIndexFilter);

        entity.HasIndex(filing => new { filing.MailboxAccountId, filing.InternetMessageId })
            .HasDatabaseName(PersistenceConstraintNames.OutgoingEmailFilingMessageIdIndexName)
            .HasFilter(JoinableFilingIndexFilter);

        entity.HasOne(filing => filing.OutgoingEmail)
            .WithMany(message => message.Filings)
            .HasForeignKey(filing => filing.OutgoingEmailId)
            .HasConstraintName(PersistenceConstraintNames.OutgoingEmailFilingForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
