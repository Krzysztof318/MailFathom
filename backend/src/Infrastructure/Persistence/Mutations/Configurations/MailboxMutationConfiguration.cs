// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Mutations.Configurations;

/// <summary>Declares the durable record every change to a remote mailbox is written to before it is issued.</summary>
/// <remarks>
/// <para>
/// The row hangs on the local email and cascades from it, which is what makes the mutation history reachable by that
/// email's deletion path rather than by a second erasure rule somebody has to remember. A mutation history says
/// where a person's mail has been and what was done to it, so it inherits the retention and deletion obligations of
/// the mail it describes — including when the mutation recorded was the deletion.
/// </para>
/// <para>
/// The source occurrence is stored beside that association rather than read back through it. The email moves; the
/// command that was issued was aimed at one folder, UIDVALIDITY, and UID, and a record that followed the email would
/// stop describing it.
/// </para>
/// <para>
/// Nothing here is mail content. A folder path, a UID, a mutation name, and a requester identity are the server's
/// own or MailFathom's own names for things, which is what lets the record be written without the message.
/// </para>
/// </remarks>
internal sealed class MailboxMutationConfiguration : IEntityTypeConfiguration<MailboxMutationEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailboxMutationEntity> entity)
    {
        entity.ToTable("mailbox_mutations");
        entity.HasKey(mutation => mutation.Id);
        entity.Property(mutation => mutation.Id).ValueGeneratedNever();
        entity.Property(mutation => mutation.MailboxAccountId).HasMaxLength(128);
        entity.Property(mutation => mutation.Mutation).HasMaxLength(64).IsRequired();
        entity.Property(mutation => mutation.RequesterIdentity)
            .HasMaxLength(MailboxMutationRequester.MaximumIdentityLength)
            .IsRequired();
        entity.Property(mutation => mutation.DestinationFolderPath)
            .HasMaxLength(MailboxMutationEntity.MaximumDestinationPathLength);
        entity.Property(mutation => mutation.DestinationHierarchyDelimiter).HasMaxLength(1);

        // Stored as text for the reason the content-availability reason is: both stay readable in an ad-hoc audit
        // query and survive any later reordering of their enum.
        entity.Property(mutation => mutation.RequesterOrigin).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(mutation => mutation.Stage).HasConversion<string>().HasMaxLength(64).IsRequired();

        // Stored as text for the same reason, and nullable because only a delete carries one. A row whose text
        // names no declared disposition fails the read rather than being taken as the destructive value by
        // elimination, which is what an integer column would have allowed.
        entity.Property(mutation => mutation.LocalDisposition).HasConversion<string>().HasMaxLength(64);

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(mutation => mutation.ConcurrencyVersion).IsRowVersion();

        ConfigureIndexes(entity);

        entity.HasOne(mutation => mutation.StoredEmail)
            .WithMany(email => email.Mutations)
            .HasForeignKey(mutation => mutation.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasOne(mutation => mutation.MailFolder)
            .WithMany()
            .HasForeignKey(mutation => mutation.MailFolderId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>Declares the uniqueness a mutation's idempotency rests on, and the index its unfinished work is read through.</summary>
    /// <remarks>
    /// <para>
    /// The unique index is the idempotency guarantee itself rather than a support for one. Two callers asking for the
    /// same change at the same moment both pass any check the application could make between reading and writing, and
    /// only the database closes that window; the same request twice therefore performs one mutation because the second
    /// insert is refused, not because the code declined to attempt it.
    /// </para>
    /// <para>
    /// Its columns are exactly the identity the issue settled: the email occurrence, the requester, and the mutation.
    /// The occurrence is the folder binding with its UIDVALIDITY and UID rather than the local email, so an email that
    /// has moved is a new occurrence and the same rule asking about it again asks afresh.
    /// </para>
    /// <para>
    /// The second index answers the operator's question — which changes are in flight and which are stuck — and is
    /// filtered to the rows that can be either. A completed mutation never is, so keeping it in the structure would
    /// grow the index with the mailbox's whole mutation history rather than with what is outstanding. An abandoned one
    /// stays in, deliberately: giving up on a change is what makes it stop being retried, and it would be worth nothing
    /// if it also made the change stop being seen.
    /// </para>
    /// <para>
    /// The third answers the forward pass of synchronization, which asks of every batch it discovers whether any of
    /// those UIDs is where a relocation put an email. It is filtered to the records that can still answer yes, so it
    /// holds one row per relocation in flight rather than one per relocation ever made — and on a mailbox nobody
    /// relocates into it holds nothing at all, which is what makes the question free to ask on every batch. Reading a
    /// disappearance back needs no index of its own: it is asked by folder, UIDVALIDITY, and UID, which is the prefix
    /// the identity index already leads with.
    /// </para>
    /// </remarks>
    private static void ConfigureIndexes(EntityTypeBuilder<MailboxMutationEntity> entity)
    {
        entity.HasIndex(mutation => new
        {
            mutation.MailFolderId,
            mutation.UidValidity,
            mutation.Uid,
            mutation.RequesterOrigin,
            mutation.RequesterIdentity,
            mutation.Mutation,
        })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailboxMutationIdentityUniqueIndexName);

        entity.HasIndex(mutation => new
        {
            mutation.OwnerId,
            mutation.MailboxAccountId,
            mutation.RecordedAt,
        })
            .HasDatabaseName(PersistenceConstraintNames.MailboxMutationOutstandingIndexName)
            .HasFilter($"\"{nameof(MailboxMutationEntity.Stage)}\" <> '{nameof(MailboxMutationStage.Completed)}'");

        entity.HasIndex(mutation => new
        {
            mutation.OwnerId,
            mutation.MailboxAccountId,
            mutation.DestinationFolderPath,
            mutation.PlacementUidValidity,
            mutation.PlacementUid,
        })
            .HasDatabaseName(PersistenceConstraintNames.MailboxMutationPlacementIndexName)
            .HasFilter($"\"{nameof(MailboxMutationEntity.PlacementObservedAt)}\" IS NULL");
    }
}
