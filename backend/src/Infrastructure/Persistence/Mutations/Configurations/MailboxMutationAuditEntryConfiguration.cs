// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Mutations.Configurations;

/// <summary>Declares the history a finished change to a remote mailbox leaves behind.</summary>
/// <remarks>
/// <para>
/// The row hangs on nothing. Every identity it carries — the account, the local email, the source and destination
/// folders — is a value rather than an association, so no cascade reaches it and the entry outlives everything it
/// describes. That is the whole reason the table exists separately from <c>mailbox_mutations</c>, which deliberately
/// does cascade from the email: a trail that inherited the mail's deletion path would erase the record that
/// MailFathom deleted it, which is exactly the entry an audit of deletions exists to hold.
/// </para>
/// <para>
/// It is append-only. Nothing amends an entry, so the row carries no concurrency token: there is no second writer
/// for one to protect against, and the uniqueness below is what makes a repeated append leave the trail as it was.
/// </para>
/// <para>
/// Nothing here is mail content. A folder path, a UID, a mutation name, and a requester identity are the server's own
/// or MailFathom's own names for things — which is what lets the act be recorded without the message.
/// </para>
/// </remarks>
internal sealed class MailboxMutationAuditEntryConfiguration : IEntityTypeConfiguration<MailboxMutationAuditEntryEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailboxMutationAuditEntryEntity> entity)
    {
        entity.ToTable("mailbox_mutation_audit_entries");
        entity.HasKey(entry => entry.Id);
        entity.Property(entry => entry.Id).ValueGeneratedNever();
        entity.Property(entry => entry.MailboxAccountId).HasMaxLength(128).IsRequired();
        entity.Property(entry => entry.Mutation).HasMaxLength(64).IsRequired();
        entity.Property(entry => entry.SourceFolderPath)
            .HasMaxLength(MailboxMutationAuditEntryEntity.MaximumFolderPathLength)
            .IsRequired();
        entity.Property(entry => entry.SourceHierarchyDelimiter).HasMaxLength(1);
        entity.Property(entry => entry.DestinationFolderPath)
            .HasMaxLength(MailboxMutationAuditEntryEntity.MaximumFolderPathLength);
        entity.Property(entry => entry.DestinationHierarchyDelimiter).HasMaxLength(1);
        entity.Property(entry => entry.RequesterIdentity)
            .HasMaxLength(MailboxMutationRequester.MaximumIdentityLength)
            .IsRequired();

        // Stored as text for the reason every other enum in this model is: both stay readable in the ad-hoc query
        // an audit is answered from, and survive any later reordering of their enum.
        entity.Property(entry => entry.RequesterOrigin).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(entry => entry.Outcome).HasConversion<string>().HasMaxLength(64).IsRequired();

        // One ending per mutation, enforced by the database rather than checked before the insert: an append
        // repeated after a commit whose answer was lost passes any application check and only the constraint
        // closes that window.
        entity.HasIndex(entry => entry.MutationRecordId)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailboxMutationAuditEntryMutationUniqueIndexName);

        // The one index the trail is worked through, and it serves both readers: a page is the account's entries
        // ordered by when they ended, and retention erases the same account's entries that ended before a cutoff.
        // A data-subject erasure by local email is a rare, deliberate operator act and reads the table rather than
        // an index of its own, which keeps the write cost of an append to one index beyond the key.
        entity.HasIndex(entry => new
        {
            entry.OwnerId,
            entry.MailboxAccountId,
            entry.CompletedAt,
            entry.Id,
        })
            .HasDatabaseName(PersistenceConstraintNames.MailboxMutationAuditEntryTimelineIndexName);
    }
}
