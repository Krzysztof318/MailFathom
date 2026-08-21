// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Answering.Configurations;

/// <summary>Declares the record one answered question leaves behind.</summary>
/// <remarks>
/// <para>
/// Two tables rather than one, and the split is the deletion obligation rather than normalization for its own sake. The
/// entry states that a question was answered from an account's mailbox, which stays true after the account is removed
/// from configuration; the rows beside it name individual messages, and a message erased anywhere in this system has to
/// stop being named here. A column holding an array of identifiers would satisfy the first and quietly defeat the
/// second.
/// </para>
/// <para>
/// Nothing here is mail content. An identifier, an endpoint alias, an instruction version, two instants, and two
/// bounded outcomes are MailFathom's own names for things — which is what lets the run be explained without the mail
/// being copied.
/// </para>
/// <para>
/// It is append-only. Nothing amends an entry, so the row carries no concurrency token: there is no second writer for
/// one to protect against, and the uniqueness below is what makes a repeated append leave the record as it was.
/// </para>
/// </remarks>
internal sealed class MailAnsweringAuditEntryConfiguration : IEntityTypeConfiguration<MailAnsweringAuditEntryEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailAnsweringAuditEntryEntity> entity)
    {
        entity.ToTable("mail_answering_audit_entries");
        entity.HasKey(record => record.Id);
        entity.Property(record => record.Id).ValueGeneratedNever();
        entity.Property(record => record.MailboxAccountId).HasMaxLength(128).IsRequired();
        entity.Property(record => record.ChatEndpointAlias)
            .HasMaxLength(MailAnsweringAuditEntryEntity.MaximumAliasLength)
            .IsRequired();
        entity.Property(record => record.InstructionsVersion)
            .HasMaxLength(MailAnsweringAuditEntryEntity.MaximumInstructionsVersionLength)
            .IsRequired();

        // The two bounded outcomes are held as their own names rather than as converted enums, which is the one
        // place this model departs from the pattern beside it. A converted enum fails materialization on a name it
        // declares no member for, and this record is read a page at a time: a value a later build wrote would fail
        // the page holding it and every page after it, on exactly the artifact an audit cannot afford that on.
        entity.Property(record => record.Outcome).HasMaxLength(64).IsRequired();
        entity.Property(record => record.Degradation).HasMaxLength(128).IsRequired();

        // One entry per run per account, enforced by the database rather than checked before the insert: an append
        // repeated after a commit whose answer was lost passes any application check and only the constraint closes
        // that window.
        entity.HasIndex(record => new { record.RunId, record.MailboxAccountId })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailAnsweringAuditEntryRunUniqueIndexName);

        // The one index the record is worked through, and it serves both readers: a page is the account's entries
        // ordered by when they ended, and retention erases the same account's entries that ended before a cutoff.
        entity.HasIndex(record => new { record.MailboxAccountId, record.CompletedAt, record.Id })
            .HasDatabaseName(PersistenceConstraintNames.MailAnsweringAuditEntryTimelineIndexName);
    }
}
