// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares the metadata of one remote email occurrence, which is the row the whole mailbox hangs off.</summary>
/// <remarks>
/// UIDVALIDITY and UID are modelled as CLR <see cref="uint" /> because that is the IMAP wire type, and PostgreSQL has
/// no unsigned 32-bit integer. Npgsql maps both onto <c>bigint</c>, which the baseline migration emits and which holds
/// every value the wire type can carry; the integration suite stores an occurrence at <see cref="uint.MaxValue" /> and
/// reads it back to keep that lossless. Narrowing either column to <c>integer</c> would truncate silently rather than
/// fail, so the column type is part of the identity contract instead of an implementation detail.
/// </remarks>
internal sealed class StoredEmailConfiguration : IEntityTypeConfiguration<StoredEmailEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StoredEmailEntity> entity)
    {
        entity.ToTable("stored_emails");
        entity.HasKey(email => email.Id);
        entity.Property(email => email.Id).ValueGeneratedNever();
        entity.Property(email => email.MailboxAccountId).HasMaxLength(128);

        // Every mail-derived column is bounded by the same constant the mapping refuses an over-long value against,
        // so a header nobody bounds cannot reach a column that would reject it. A truncating column would be the
        // worse half of that pair: the write would succeed and the row would carry an address nobody wrote.
        entity.Property(email => email.InternetMessageId).HasMaxLength(StoredEmailEntity.MaximumIdentifierLength);
        entity.Property(email => email.InReplyTo).HasMaxLength(StoredEmailEntity.MaximumIdentifierLength);
        entity.Property(email => email.SenderAddress).HasMaxLength(StoredEmailEntity.MaximumAddressLength);
        entity.Property(email => email.SenderNormalizedAddress).HasMaxLength(StoredEmailEntity.MaximumAddressLength);

        // A `uint` row version is Npgsql's mapping onto the PostgreSQL `xmin` system column, so no concurrency column
        // is created in the table and PostgreSQL updates the token itself. Changing the CLR type or the row-version
        // configuration would silently turn this into an ordinary column that nothing ever updates.
        entity.Property(email => email.ConcurrencyVersion).IsRowVersion();

        // Stored as text so the availability reason stays readable in ad-hoc audit queries and survives enum reordering.
        entity.Property(email => email.ContentAvailability).HasConversion<string>().HasMaxLength(64).IsRequired();

        // The sender-authentication verdict — what authenticated, and separately what that establishes about the
        // displayed author — whose five enums are stored as text for the same reason and whose domains are bounded
        // by the length a resolver accepts, which the domain value already refuses to exceed. Each enum carries a
        // database default naming the value that establishes nothing, because that is what is true of a row written
        // before this deployment read the header: the migration that adds the columns fills every stored message in
        // with it, and a mailbox re-reads its own raw MIME through a re-derivation pass.
        entity.Property(email => email.SenderAuthenticationOutcome)
            .HasConversion<string>()
            .HasMaxLength(64)
            .HasDefaultValue(SenderAuthenticationOutcome.NotEstablished)
            .IsRequired();
        entity.Property(email => email.SenderAuthenticationMethod)
            .HasConversion<string>()
            .HasMaxLength(64)
            .HasDefaultValue(SenderAuthenticationMethod.None)
            .IsRequired();
        entity.Property(email => email.DmarcOutcome)
            .HasConversion<string>()
            .HasMaxLength(64)
            .HasDefaultValue(DmarcOutcome.NotReported)
            .IsRequired();
        entity.Property(email => email.AuthorAuthenticationOutcome)
            .HasConversion<string>()
            .HasMaxLength(64)
            .HasDefaultValue(AuthorAuthenticationOutcome.NotEstablished)
            .IsRequired();
        // Who reached that verdict, stored the same way and defaulted to the receiving server: every row written
        // before this deployment verified anything itself came from the trusted-header reading, whatever it found.
        entity.Property(email => email.SenderAuthenticationSource)
            .HasConversion<string>()
            .HasMaxLength(64)
            .HasDefaultValue(SenderAuthenticationSource.ReceivingServer)
            .IsRequired();
        entity.Property(email => email.AuthenticatedSenderDomain).HasMaxLength(StoredEmailEntity.MaximumDomainLength);
        entity.Property(email => email.DkimSignerDomain).HasMaxLength(StoredEmailEntity.MaximumDomainLength);
        entity.Property(email => email.SpfMailFromDomain).HasMaxLength(StoredEmailEntity.MaximumDomainLength);
        entity.Property(email => email.AuthenticatedAuthorDomain).HasMaxLength(StoredEmailEntity.MaximumDomainLength);
        entity.Property(email => email.DisplayedAuthorDomain).HasMaxLength(StoredEmailEntity.MaximumDomainLength);

        // What this deployment made of that verdict, stored the same way and defaulted the same way: a row written
        // before authors were judged at all recognized nobody, which is exactly what the two defaults say. The
        // revision is nullable rather than defaulted, because its absence is what separates a row no policy judged
        // from one a policy judged and left unknown.
        entity.Property(email => email.SenderTrustLevel)
            .HasConversion<string>()
            .HasMaxLength(64)
            .HasDefaultValue(SenderTrustLevel.Unknown)
            .IsRequired();
        entity.Property(email => email.SenderTrustGrantedBy)
            .HasConversion<string>()
            .HasMaxLength(64)
            .HasDefaultValue(SenderTrustSource.None)
            .IsRequired();
        entity.Property(email => email.SenderTrustPolicyRevision)
            .HasMaxLength(SenderTrustPolicyRevision.Length);

        // How much the message's own text read as machine written. The band is stored as text like every other
        // enum here and defaults to the value that claims nothing, which is what a row written before the
        // assessment existed holds and what a deployment that turned it off writes on all of its mail. The signal
        // set is the one enum stored numerically, for the reason its property states: a flag set written as text
        // is a formatted list no query can ask a member of. The revision is nullable rather than defaulted,
        // because its absence is what says nothing assessed this row at all.
        entity.Property(email => email.MachineAuthorshipBand)
            .HasConversion<string>()
            .HasMaxLength(64)
            .HasDefaultValue(MachineAuthorshipBand.NotAssessed)
            .IsRequired();
        entity.Property(email => email.MachineAuthorshipLikelihood)
            .HasDefaultValue(0d)
            .IsRequired();
        entity.Property(email => email.MachineAuthorshipSignals)
            .HasConversion<int>()
            .HasDefaultValue(MachineAuthorshipSignals.None)
            .IsRequired();
        entity.Property(email => email.MachineAuthorshipProfileRevision)
            .HasMaxLength(MachineAuthorshipProfileRevision.Length);

        ConfigureIndexes(entity);

        entity.HasOne(email => email.MailFolder)
            .WithMany(folder => folder.StoredEmails)
            .HasForeignKey(email => email.MailFolderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Neither association takes the mail with it. A thread is an assembly of messages rather than their owner,
        // so losing one must leave every message readable and unthreaded; and an answer must outlive the message it
        // answers, published as a root of what remains rather than erased alongside it.
        entity.HasOne<EmailThreadEntity>()
            .WithMany()
            .HasForeignKey(email => email.EmailThreadId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne<StoredEmailEntity>()
            .WithMany()
            .HasForeignKey(email => email.ParentStoredEmailId)
            .OnDelete(DeleteBehavior.SetNull);
    }

    /// <summary>Declares the indexes mailbox reads are planned against, and with them the timeline ordering contract.</summary>
    /// <remarks>
    /// <para>
    /// Every index a mailbox read narrows on leads with the owner, because that is the first term such a read carries:
    /// an account identifier is unique within its owner and nowhere else, so a structure led by the account alone would
    /// interleave two owners' mail under one key. The folder-led indexes are the exception and stay as they are — a
    /// folder identity is generated and belongs to exactly one account, so it already names one owner's rows.
    /// </para>
    /// <para>
    /// All three timeline indexes reproduce <see cref="EmailTimelinePosition.NewestFirst" /> column for column after
    /// the columns they lead with: the received timestamp descending with unknown timestamps last, then the identifier
    /// descending. Keyset pagination is only contiguous while the server's order and the process's order are the same
    /// order, so a change to any one of them is a change to all of them and to the comparer.
    /// </para>
    /// <para>
    /// <c>NULLS LAST</c> is stated rather than left out, because PostgreSQL orders nulls first under <c>DESC</c> and the
    /// silent default is the opposite of the decision: it would float every message nobody could date above the newest
    /// mail, on every page, forever.
    /// </para>
    /// <para>
    /// The recipient arrays are indexed with GIN, which is what makes a containment test over an array column an index
    /// scan. A B-tree over an array would only serve equality against a whole array, which no query asks for.
    /// </para>
    /// </remarks>
    private static void ConfigureIndexes(EntityTypeBuilder<StoredEmailEntity> entity)
    {
        entity.HasIndex(email => new { email.MailFolderId, email.UidValidity, email.Uid })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.StoredEmailOccurrenceUniqueIndexName);

        entity.HasIndex(email => new { email.OwnerId, email.MailboxAccountId, email.ReceivedAt, email.Id })
            .HasDatabaseName(PersistenceConstraintNames.StoredEmailAccountTimelineIndexName)
            .IsDescending(false, false, true, true)
            .HasNullSortOrder(
                NullSortOrder.Unspecified,
                NullSortOrder.Unspecified,
                NullSortOrder.NullsLast,
                NullSortOrder.Unspecified);

        entity.HasIndex(email => new { email.MailFolderId, email.ReceivedAt, email.Id })
            .HasDatabaseName(PersistenceConstraintNames.StoredEmailFolderTimelineIndexName)
            .IsDescending(false, true, true)
            .HasNullSortOrder(NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified);

        // The reconciliation queue, ordered exactly as the query that reads it: within one folder, by the moment the
        // server was last asked and then by UID. The UID is a key rather than a decoration, because the query orders by
        // it too, and without it a folder whose emails were observed in one batch would tie on the timestamp and make a
        // bounded window cost a sort of the whole folder.
        //
        // No null sort order is stated, and that is deliberate rather than an omission. The window is read as two
        // queries — one for the emails observed before, one for those never observed — so neither of them orders a null
        // against a value, and both take PostgreSQL's default. Declaring NULLS FIRST here instead would leave the index
        // ordered the one way the ASC query does not ask for, which is not a near miss: the planner cannot use an index
        // whose null placement differs from the ordering, and the measured plan degraded to a parallel sequential scan
        // and a top-N sort over every eligible row in the folder.
        //
        // The filter is what keeps the index proportionate to the queue rather than to the mailbox: a tombstoned email
        // is outside every window, so it has no place in the structure a window is read from.
        entity.HasIndex(email => new { email.MailFolderId, email.RemoteFlagsObservedAt, email.Uid })
            .HasDatabaseName(PersistenceConstraintNames.StoredEmailReconciliationQueueIndexName)
            .HasFilter($"\"{nameof(StoredEmailEntity.RemoteExpungeObservedAt)}\" IS NULL");

        // The queue of occurrences recorded without their payload, read once per folder run and almost always empty.
        // The filter is what makes that read cost nothing on a deployment that has never reached its storage ceiling:
        // without it the query would walk a folder's whole occurrence index to discover that none of its rows qualify,
        // on every run of every folder. It is keyed by folder and UID because the pass fetches within one open folder
        // and asks in UID order, which is the order the mailbox itself is walked in.
        // The model name is given explicitly because an index is identified in the model by the properties it covers,
        // and these are the same three the unique occurrence index above covers. Without a name of its own this
        // declaration would reconfigure that index rather than add one — which generates a migration that drops the
        // constraint holding occurrence identity unique and replaces it with a filtered index.
        entity.HasIndex(
                email => new { email.MailFolderId, email.UidValidity, email.Uid },
                PersistenceConstraintNames.StoredEmailAwaitingContentIndexName)
            .HasDatabaseName(PersistenceConstraintNames.StoredEmailAwaitingContentIndexName)
            .HasFilter(
                $"\"{nameof(StoredEmailEntity.ContentAvailability)}\" = '{nameof(StoredEmailContentAvailability.AwaitingStorageHeadroom)}'");

        // The order a requested whole-mailbox rule run walks in. It is the identity rather than the timeline because a
        // walk that has to resume needs a total order no later write disturbs, and because the position it commits is
        // one column rather than a nullable timestamp paired with a tie-breaker.
        entity.HasIndex(email => new { email.OwnerId, email.MailboxAccountId, email.Id })
            .HasDatabaseName(PersistenceConstraintNames.StoredEmailAccountIdentityIndexName);

        // The arrival queue, and the filter is the whole point of it. In steady state almost every row of an account
        // has been evaluated, so without the filter this read would walk the account's entire index once per run to
        // find the handful of rows that qualify — and it runs for every account on every synchronization run.
        entity.HasIndex(
                email => new { email.OwnerId, email.MailboxAccountId, email.Id },
                PersistenceConstraintNames.StoredEmailAwaitingRuleEvaluationIndexName)
            .HasDatabaseName(PersistenceConstraintNames.StoredEmailAwaitingRuleEvaluationIndexName)
            .HasFilter(
                $"\"{nameof(StoredEmailEntity.RulesEvaluatedAt)}\" IS NULL AND "
                + $"\"{nameof(StoredEmailEntity.FiledFromOutgoingEmailId)}\" IS NULL");

        // Every read of a conversation runs on this: assembling an arrival asks its thread for the message it answers
        // and for the messages already stored that answer it, and publishing a thread reads its whole membership. The
        // identity is carried beside the thread because it is the last term of the one order a thread has, so the rows
        // arrive already sorted on the tie-breaker rather than being sorted again for it.
        entity.HasIndex(email => new { email.EmailThreadId, email.Id })
            .HasDatabaseName(PersistenceConstraintNames.StoredEmailThreadIndexName);

        entity.HasIndex(email => email.SenderNormalizedAddress)
            .HasDatabaseName(PersistenceConstraintNames.StoredEmailSenderIndexName);

        entity.HasIndex(email => email.ToAddresses)
            .HasDatabaseName(PersistenceConstraintNames.StoredEmailToAddressesIndexName)
            .HasMethod("GIN");

        entity.HasIndex(email => email.CcAddresses)
            .HasDatabaseName(PersistenceConstraintNames.StoredEmailCcAddressesIndexName)
            .HasMethod("GIN");

        entity.HasIndex(email => email.ReplyToAddresses)
            .HasDatabaseName(PersistenceConstraintNames.StoredEmailReplyToAddressesIndexName)
            .HasMethod("GIN");

        // A keyword filter asks whether the array contains one value, which is the containment operator a GIN index
        // over a text[] serves — the same shape and the same reason as the three address arrays above.
        entity.HasIndex(email => email.RemoteKeywords)
            .HasDatabaseName(PersistenceConstraintNames.StoredEmailRemoteKeywordsIndexName)
            .HasMethod("GIN");
    }
}
