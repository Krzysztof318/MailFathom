// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Rules.History;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace MailFathom.Infrastructure.Persistence;

/// <summary>EF Core context for local MailFathom persistence.</summary>
[RequiresIntegrationCoverage]
internal sealed class MailFathomDbContext : DbContext
{
    /// <summary>The mailbox account primary key, kept at the name EF Core's own convention gave the applied baseline.</summary>
    /// <remarks>
    /// Stated by the mapping rather than left implicit, because a losing writer is recognized by the constraint its
    /// insert violated and a rename that only the convention knew about would silently turn a resolvable race into a
    /// failure. The value is the conventional one so the model states the name without asking the schema to change.
    /// </remarks>
    internal const string MailboxAccountPrimaryKeyConstraintName = "PK_mailbox_accounts";

    internal const string SynchronizationCheckpointPrimaryKeyConstraintName = "pk_synchronization_checkpoints";

    internal const string MailFolderBindingUniqueIndexName = "ix_mail_folders_account_alias_generation";

    internal const string StoredEmailOccurrenceUniqueIndexName = "ix_stored_emails_folder_uidvalidity_uid";

    internal const string StoredEmailAccountTimelineIndexName = "ix_stored_emails_account_timeline";

    internal const string StoredEmailFolderTimelineIndexName = "ix_stored_emails_folder_timeline";

    internal const string StoredEmailReconciliationQueueIndexName = "ix_stored_emails_reconciliation_queue";

    internal const string StoredEmailAwaitingContentIndexName = "ix_stored_emails_awaiting_content";

    /// <summary>The order a requested whole-mailbox rule run walks an account's mail in.</summary>
    internal const string StoredEmailAccountIdentityIndexName = "ix_stored_emails_account_identity";

    /// <summary>The queue of mail no rule pass has evaluated, which is read once per account run and is usually empty.</summary>
    internal const string StoredEmailAwaitingRuleEvaluationIndexName = "ix_stored_emails_awaiting_rule_evaluation";

    internal const string StoredEmailSenderIndexName = "ix_stored_emails_sender";

    internal const string StoredEmailToAddressesIndexName = "ix_stored_emails_to_addresses";

    internal const string StoredEmailCcAddressesIndexName = "ix_stored_emails_cc_addresses";

    internal const string StoredEmailReplyToAddressesIndexName = "ix_stored_emails_reply_to_addresses";

    internal const string StoredEmailRemoteKeywordsIndexName = "ix_stored_emails_remote_keywords";

    internal const string EmailSearchDocumentVectorIndexName = "ix_email_search_documents_search_vector";

    internal const string EmailChunkOrdinalUniqueIndexName = "ix_email_chunks_email_ordinal";

    /// <summary>The unique index over an embedding profile's identity, which is what makes activation idempotent.</summary>
    /// <remarks>
    /// Named because a losing writer is recognized by the constraint its insert violated: two operators activating the
    /// same declaration is a race that resolves to the profile already registered, not a failure to report.
    /// </remarks>
    internal const string EmbeddingProfileFingerprintUniqueIndexName = "ix_embedding_profiles_identity_fingerprint";

    /// <summary>The index that admits one generation being built and one being read, and no second of either.</summary>
    /// <remarks>
    /// The guarantee is structural because the failure it prevents is silent: two rows claiming to serve would leave
    /// retrieval reading whichever one a query happened to return, with half the vectors in the table unreachable and
    /// nothing about the answers saying so. Superseded rows are outside the filter, because a deployment accumulates
    /// one per model it has ever used.
    /// </remarks>
    internal const string EmbeddingProfileLifecycleUniqueIndexName = "ix_embedding_profiles_lifecycle_state";

    /// <summary>The alternate key a vector row's dimension is checked against.</summary>
    internal const string EmbeddingProfileDimensionAlternateKeyName = "ak_embedding_profiles_id_dimension";

    /// <summary>The key an idempotent vector upsert conflicts on.</summary>
    internal const string EmailEmbeddingPrimaryKeyConstraintName = "pk_email_embeddings";

    /// <summary>The constraint that ties a stored vector's length to the width its profile declares.</summary>
    internal const string EmailEmbeddingDimensionCheckConstraintName = "ck_email_embeddings_dimension";

    /// <summary>The composite foreign key that refuses a width the named profile never declared.</summary>
    /// <remarks>
    /// Named because EF's convention would compose one from both column names and PostgreSQL would truncate it at 63
    /// characters, leaving a permanent identifier ending in a tilde.
    /// </remarks>
    internal const string EmailEmbeddingProfileForeignKeyName = "fk_email_embeddings_embedding_profiles";

    /// <summary>The index a whole generation is read by when it is removed.</summary>
    internal const string EmailEmbeddingProfileIndexName = "ix_email_embeddings_profile";

    internal const string MailboxRefreshTokenKeyIndexName = "ix_mailbox_refresh_tokens_data_encryption_key";

    /// <summary>The key that keeps one classification per occurrence, and which a second concurrent run is recognized by.</summary>
    /// <remarks>
    /// Named because losing this race is the mechanism rather than a fault: an arrival classifies an occurrence while a
    /// reclassification replaces it, one of them violates this key, and the retry reads back the row the winner wrote —
    /// which is how classifying twice produces one record.
    /// </remarks>
    internal const string EmailSpamClassificationPrimaryKeyConstraintName = "pk_email_spam_classifications";

    /// <summary>The order one classification's signals are read back in, and what stops an ordinal being written twice.</summary>
    internal const string EmailSpamClassificationSignalOrdinalUniqueIndexName =
        "ix_email_spam_classification_signals_classification_ordinal";

    /// <summary>The foreign key that removes a classification's signals with the classification.</summary>
    /// <remarks>
    /// Named because EF's convention composes one from both table names and PostgreSQL truncates an identifier at 63
    /// characters, which would leave a permanent constraint whose name ends in a tilde.
    /// </remarks>
    internal const string EmailSpamClassificationSignalForeignKeyName =
        "fk_email_spam_classification_signals_classifications";

    /// <summary>The key that keeps one whole-mailbox rule run per account, and which a second request is recognized by.</summary>
    /// <remarks>
    /// Named because losing this race is the mechanism rather than a fault: two requests for one account's first run
    /// reach the database together, one of them violates this key, and the retry reads back the run the winner asked
    /// for — which is exactly how asking twice produces one walk of one mailbox.
    /// </remarks>
    internal const string MailRuleEvaluationRunPrimaryKeyConstraintName = "pk_mail_rule_evaluation_runs";

    /// <summary>The key that keeps one whole-mailbox classification run per account, and which a second request meets.</summary>
    /// <remarks>
    /// Named for the reason the rule run's key is: two requests for one account's first run reach the database together,
    /// one of them violates this key, and the retry reads back the run the winner asked for — which is how asking twice
    /// produces one walk of one mailbox rather than two.
    /// </remarks>
    internal const string SpamClassificationRunPrimaryKeyConstraintName = "pk_spam_classification_runs";

    /// <summary>The key that keeps one re-derivation cursor per scope, and which a second walk of it is recognized by.</summary>
    /// <remarks>
    /// Named because the first batch of a scope nobody has walked is a check-then-insert over this key: two invocations
    /// asked for at once, or one request retried, both read no row and both insert. Losing that race is the mechanism
    /// rather than a fault — the retry reads back the position the winner wrote and moves it on from there, which is
    /// what makes asking twice walk the scope once instead of ending the pass on a provider failure.
    /// </remarks>
    internal const string MailRederivationPositionPrimaryKeyConstraintName = "pk_mail_rederivation_positions";

    /// <summary>The constraint a mutation's idempotency identity is enforced by, and which a losing writer is recognized from.</summary>
    /// <remarks>
    /// Named because the name is how the same request arriving twice is told apart from a genuine failure. Two callers
    /// asking for the same change reach the database together and one of them violates this index; that is the second
    /// caller learning the first got there, not a fault, and the session translates it into the conflict the retry
    /// policy loops on.
    /// </remarks>
    internal const string MailboxMutationIdentityUniqueIndexName = "ix_mailbox_mutations_identity";

    internal const string MailboxMutationOutstandingIndexName = "ix_mailbox_mutations_outstanding";

    internal const string MailboxMutationPlacementIndexName = "ix_mailbox_mutations_placement";

    /// <summary>The constraint that keeps one audit entry per mutation ending, whatever a repeated append attempts.</summary>
    internal const string MailboxMutationAuditEntryMutationUniqueIndexName =
        "ix_mailbox_mutation_audit_entries_mutation";

    /// <summary>The index the trail is both read and aged through.</summary>
    internal const string MailboxMutationAuditEntryTimelineIndexName =
        "ix_mailbox_mutation_audit_entries_account_completed";

    /// <summary>The constraint that keeps one answering entry per run per account, whatever a repeated append attempts.</summary>
    internal const string MailAnsweringAuditEntryRunUniqueIndexName = "ix_mail_answering_audit_entries_run_account";

    /// <summary>The index the answering record is both read and aged through.</summary>
    internal const string MailAnsweringAuditEntryTimelineIndexName =
        "ix_mail_answering_audit_entries_account_completed";

    /// <summary>The index the rule history is walked and aged through, which is its unfiltered page and its retention.</summary>
    internal const string MailRuleExecutionTimelineIndexName = "ix_mail_rule_executions_account_evaluated";

    /// <summary>The index that answers what one rule has been doing, which is the history's second question.</summary>
    internal const string MailRuleExecutionRuleIndexName = "ix_mail_rule_executions_account_rule_evaluated";

    /// <summary>The index that answers why one message was filed, which is the history's first question.</summary>
    internal const string MailRuleExecutionEmailIndexName = "ix_mail_rule_executions_email_evaluated";

    /// <summary>The order the contact book is listed and paginated in.</summary>
    internal const string ContactListingIndexName = "ix_contacts_display_name_sort_key_id";

    /// <summary>The constraint that keeps one address in one person's hands, across the whole book.</summary>
    /// <remarks>
    /// Named because a losing writer is recognized by the constraint its insert violated: two callers claiming one
    /// address is a race to resolve into the answer that names its holder, not a failure to report. It is also what the
    /// lookup from an address to a person is answered from.
    /// </remarks>
    internal const string ContactAddressUniqueIndexName = "ix_contact_addresses_normalized_address";

    /// <summary>The constraint an outgoing email's idempotency identity is enforced by, and which a losing writer is recognized from.</summary>
    /// <remarks>
    /// It is the mutation identity's case with the consequence raised. Two callers asking for the same send reach the
    /// database together and one of them violates this index; the retry then finds the winner's record and delivers
    /// nothing further, which is the whole of what stops one authored request putting two copies of a message in
    /// somebody's mailbox — a duplication that, unlike a local one, cannot be withdrawn afterwards.
    /// </remarks>
    internal const string OutgoingEmailIdentityUniqueIndexName = "ix_outgoing_emails_identity";

    /// <summary>The index the outbox is read through, filtered to the sends that have not finished.</summary>
    internal const string OutgoingEmailOutstandingIndexName = "ix_outgoing_emails_outstanding";

    /// <summary>The foreign key that removes an outgoing email's recipients with the record.</summary>
    /// <remarks>
    /// Named because EF's convention composes one from both table names and PostgreSQL truncates an identifier at 63
    /// characters, which would leave a permanent constraint whose name ends in a tilde.
    /// </remarks>
    internal const string OutgoingEmailRecipientForeignKeyName = "fk_outgoing_email_recipients_emails";

    /// <summary>The foreign key that removes the stored MIME with the record that says who it was for.</summary>
    /// <remarks>Named for the reason above: the composed name would be truncated and permanent.</remarks>
    internal const string OutgoingEmailContentForeignKeyName = "fk_outgoing_email_contents_emails";

    /// <summary>The uniqueness a job's idempotency rests on, which spans every state a row can reach.</summary>
    internal const string JobIdentityUniqueIndexName = "ix_jobs_identity";

    /// <summary>The index the claim statement drains the queue through, filtered to the rows a claim can still take.</summary>
    internal const string JobClaimIndexName = "ix_jobs_claimable";

    /// <summary>The index an account's jobs are erased and aged through.</summary>
    internal const string JobAccountIndexName = "ix_jobs_account";

    /// <summary>The index an operator reads what has stopped through, filtered to the one state that waits for them.</summary>
    internal const string JobDeadLetterIndexName = "ix_jobs_dead_lettered";

    private readonly PostgresTextSearchConfiguration textSearchConfiguration;

    /// <summary>Initializes a new MailFathom EF Core context.</summary>
    /// <param name="options">The provider and connection configuration.</param>
    /// <param name="textSearchConfiguration">The validated text search configuration the lexical index is built with.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="textSearchConfiguration" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The configuration reaches the model rather than a query because it is part of the schema: it is compiled into a
    /// generated column, so it is fixed for a deployment's data and changing it is a schema change that reindexes.
    /// EF caches one model per context type, which is correct here because the composition root binds exactly one
    /// configuration per process and validates it before the container is built.
    /// </remarks>
    public MailFathomDbContext(
        DbContextOptions<MailFathomDbContext> options,
        PostgresTextSearchConfiguration textSearchConfiguration)
        : base(options)
    {
        ArgumentNullException.ThrowIfNull(textSearchConfiguration);

        this.textSearchConfiguration = textSearchConfiguration;
    }

    internal DbSet<MailboxAccountEntity> MailboxAccounts => this.Set<MailboxAccountEntity>();

    internal DbSet<MailFolderEntity> MailFolders => this.Set<MailFolderEntity>();

    internal DbSet<StoredEmailEntity> StoredEmails => this.Set<StoredEmailEntity>();

    internal DbSet<EmailMessageContentEntity> EmailMessageContents => this.Set<EmailMessageContentEntity>();

    internal DbSet<EmailSearchDocumentEntity> EmailSearchDocuments => this.Set<EmailSearchDocumentEntity>();

    internal DbSet<EmailChunkEntity> EmailChunks => this.Set<EmailChunkEntity>();

    internal DbSet<EmbeddingProfileEntity> EmbeddingProfiles => this.Set<EmbeddingProfileEntity>();

    internal DbSet<EmailEmbeddingEntity> EmailEmbeddings => this.Set<EmailEmbeddingEntity>();

    internal DbSet<EmbeddingSpendPeriodEntity> EmbeddingSpendPeriods => this.Set<EmbeddingSpendPeriodEntity>();

    internal DbSet<EmailContentRepairRequestEntity> EmailContentRepairRequests => this.Set<EmailContentRepairRequestEntity>();

    internal DbSet<EmailSpamClassificationEntity> EmailSpamClassifications => this.Set<EmailSpamClassificationEntity>();

    internal DbSet<EmailSpamClassificationSignalEntity> EmailSpamClassificationSignals =>
        this.Set<EmailSpamClassificationSignalEntity>();

    internal DbSet<SpamClassificationRunEntity> SpamClassificationRuns => this.Set<SpamClassificationRunEntity>();

    internal DbSet<BackfillPositionEntity> BackfillPositions => this.Set<BackfillPositionEntity>();

    internal DbSet<MailRederivationPositionEntity> MailRederivationPositions =>
        this.Set<MailRederivationPositionEntity>();

    internal DbSet<MailRuleEvaluationRunEntity> MailRuleEvaluationRuns => this.Set<MailRuleEvaluationRunEntity>();

    internal DbSet<SynchronizationCheckpointEntity> SynchronizationCheckpoints => this.Set<SynchronizationCheckpointEntity>();

    internal DbSet<MailboxRefreshTokenEntity> MailboxRefreshTokens => this.Set<MailboxRefreshTokenEntity>();

    internal DbSet<MailboxMutationEntity> MailboxMutations => this.Set<MailboxMutationEntity>();

    internal DbSet<OutgoingEmailEntity> OutgoingEmails => this.Set<OutgoingEmailEntity>();

    internal DbSet<OutgoingEmailRecipientEntity> OutgoingEmailRecipients =>
        this.Set<OutgoingEmailRecipientEntity>();

    internal DbSet<OutgoingEmailContentEntity> OutgoingEmailContents =>
        this.Set<OutgoingEmailContentEntity>();

    internal DbSet<MailboxMutationAuditEntryEntity> MailboxMutationAuditEntries =>
        this.Set<MailboxMutationAuditEntryEntity>();

    internal DbSet<MailAnsweringAuditEntryEntity> MailAnsweringAuditEntries =>
        this.Set<MailAnsweringAuditEntryEntity>();

    internal DbSet<MailRuleExecutionEntity> MailRuleExecutions => this.Set<MailRuleExecutionEntity>();

    internal DbSet<ContactEntity> Contacts => this.Set<ContactEntity>();

    internal DbSet<ContactAddressEntity> ContactAddresses => this.Set<ContactAddressEntity>();

    internal DbSet<JobEntity> Jobs => this.Set<JobEntity>();

    internal DbSet<JobScheduleEntity> JobSchedules => this.Set<JobScheduleEntity>();

    /// <inheritdoc />
    /// <remarks>
    /// UIDVALIDITY and UID are modelled as CLR <see cref="uint" /> because that is the IMAP wire type, and PostgreSQL has
    /// no unsigned 32-bit integer. Npgsql maps both onto <c>bigint</c>, which the baseline migration emits and which holds
    /// every value the wire type can carry; the integration suite stores an occurrence at <see cref="uint.MaxValue" /> and
    /// reads it back to keep that lossless. Narrowing either column to <c>integer</c> would truncate silently rather than
    /// fail, so the column type is part of the identity contract instead of an implementation detail.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Declared before any entity so the baseline migration emits CREATE EXTENSION ahead of the tables. The image
        // ships pgvector, which makes the extension installable but not installed: without this, the first vector
        // column would fail on a type PostgreSQL does not know. Enabling it here rather than in the migration that
        // introduces that column keeps the RAG stage from needing a migration whose only content is this statement,
        // and costs an empty database one catalogue entry.
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<MailboxAccountEntity>(entity =>
        {
            entity.ToTable("mailbox_accounts");

            // The account row is created by whichever run first binds one of the account's folders, so two overlapping
            // first runs insert it together and one of them loses. The key is therefore named for the same reason the
            // alias binding index below is: the loser is recognized by the constraint it violated and reported as a
            // race to resolve rather than as a failure.
            entity.HasKey(account => account.Id).HasName(MailboxAccountPrimaryKeyConstraintName);
            entity.Property(account => account.Id).HasMaxLength(128);
        });

        modelBuilder.Entity<MailFolderEntity>(entity =>
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
            entity.HasIndex(folder => new { folder.MailboxAccountId, folder.Alias, folder.ResolutionGeneration })
                .IsUnique()
                .HasDatabaseName(MailFolderBindingUniqueIndexName);
            entity.HasOne(folder => folder.MailboxAccount)
                .WithMany(account => account.MailFolders)
                .HasForeignKey(folder => folder.MailboxAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StoredEmailEntity>(entity =>
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
            // displayed author — whose four enums are stored as text for the same reason and whose domains are bounded
            // by the length a resolver accepts, which the domain value already refuses to exceed. Each enum carries a
            // database default naming the value that establishes nothing, because that is what is true of a row written
            // before this deployment read the header: the migration that adds the columns fills every stored message in
            // with it, and a mailbox re-reads its own raw MIME through the extraction backfill.
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
            entity.Property(email => email.AuthenticatedSenderDomain).HasMaxLength(StoredEmailEntity.MaximumDomainLength);
            entity.Property(email => email.DkimSignerDomain).HasMaxLength(StoredEmailEntity.MaximumDomainLength);
            entity.Property(email => email.SpfMailFromDomain).HasMaxLength(StoredEmailEntity.MaximumDomainLength);
            entity.Property(email => email.AuthenticatedAuthorDomain).HasMaxLength(StoredEmailEntity.MaximumDomainLength);

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

            ConfigureStoredEmailIndexes(entity);

            entity.HasOne(email => email.MailFolder)
                .WithMany(folder => folder.StoredEmails)
                .HasForeignKey(email => email.MailFolderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailMessageContentEntity>(entity =>
        {
            entity.ToTable("email_message_contents");
            entity.HasKey(content => content.StoredEmailId);
            entity.Property(content => content.StoredEmailId).ValueGeneratedNever();
            entity.Property(content => content.RawMime).HasColumnType("bytea").IsRequired();
            entity.Property(content => content.Sha256Hash).HasColumnType("bytea").IsRequired();
            entity.HasOne(content => content.StoredEmail)
                .WithOne(email => email.Content)
                .HasForeignKey<EmailMessageContentEntity>(content => content.StoredEmailId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        this.ConfigureEmailSearchDocument(modelBuilder);

        // Many rows per email, keyed by a surrogate identifier rather than by the email and the ordinal together,
        // because a vector row hangs on one chunk and a composite key would put a re-cut message's ordinals into every
        // table that references it. The pair is a unique index instead, which is what a reader of one message's
        // passages orders by and what stops a re-cut from writing an ordinal twice.
        modelBuilder.Entity<EmailChunkEntity>(entity =>
        {
            entity.ToTable("email_chunks");
            entity.HasKey(chunk => chunk.Id);
            entity.Property(chunk => chunk.Id).ValueGeneratedNever();

            // Fixed length because a SHA-256 digest has one. Text rather than `bytea` for the reason the value object
            // states: this digest is compared and read, unlike the raw MIME digest that only ever round-trips.
            entity.Property(chunk => chunk.ContentHash)
                .HasMaxLength(EmailChunkContentHash.Length)
                .IsFixedLength()
                .IsRequired();

            entity.Property(chunk => chunk.Text).IsRequired();

            entity.HasIndex(chunk => new { chunk.StoredEmailId, chunk.Ordinal })
                .IsUnique()
                .HasDatabaseName(EmailChunkOrdinalUniqueIndexName);

            entity.HasOne(chunk => chunk.StoredEmail)
                .WithMany(email => email.Chunks)
                .HasForeignKey(chunk => chunk.StoredEmailId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureEmbeddingProfile(modelBuilder);
        ConfigureEmailEmbedding(modelBuilder);

        // One row per budget period, keyed by the instant that period began. Nothing hangs off it and nothing cascades
        // into it: what it records is a cost this deployment incurred, which stays true after every vector that cost
        // paid for has been superseded and removed. The column names are the entity's own constants because the one
        // write is a composed upsert, so the statement and this mapping name the same things by construction.
        modelBuilder.Entity<EmbeddingSpendPeriodEntity>(entity =>
        {
            entity.ToTable(EmbeddingSpendPeriodEntity.TableName);
            entity.HasKey(period => period.PeriodStartsAt);
            entity.Property(period => period.PeriodStartsAt)
                .HasColumnName(EmbeddingSpendPeriodEntity.PeriodStartsAtColumnName)
                .ValueGeneratedNever();
            entity.Property(period => period.ConsumedInputCharacterCount)
                .HasColumnName(EmbeddingSpendPeriodEntity.ConsumedInputCharacterCountColumnName);
        });

        // One row per email whose stored content a read found unusable, keyed by that email so a reader meeting the
        // same damage repeatedly leaves one outstanding request rather than a row per attempt. It is deliberately a
        // table of its own rather than columns on the email: the requests are sparse, they are read as a work list,
        // and a repair that succeeds deletes a row instead of nulling four columns on a row it must not otherwise touch.
        modelBuilder.Entity<EmailContentRepairRequestEntity>(entity =>
        {
            entity.ToTable("email_content_repair_requests");
            entity.HasKey(repairRequest => repairRequest.StoredEmailId);
            entity.Property(repairRequest => repairRequest.StoredEmailId).ValueGeneratedNever();

            // Stored as text for the reason the content-availability reason is: the defect stays readable in an audit
            // query and survives any later reordering of the enum.
            entity.Property(repairRequest => repairRequest.Defect).HasConversion<string>().HasMaxLength(64).IsRequired();

            entity.HasOne(repairRequest => repairRequest.StoredEmail)
                .WithOne(email => email.ContentRepairRequest)
                .HasForeignKey<EmailContentRepairRequestEntity>(repairRequest => repairRequest.StoredEmailId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureEmailSpamClassification(modelBuilder);

        // One row per account, which is what makes "one outstanding whole-mailbox rule run" a property of the key rather
        // than of a check. The ending is stored as text for the reason every other outcome here is: it stays readable
        // in an ad-hoc query and survives a later reordering of the enum.
        modelBuilder.Entity<MailRuleEvaluationRunEntity>(entity =>
        {
            entity.ToTable("mail_rule_evaluation_runs");
            entity.HasKey(run => run.MailboxAccountId).HasName(MailRuleEvaluationRunPrimaryKeyConstraintName);
            entity.Property(run => run.MailboxAccountId).HasMaxLength(128).ValueGeneratedNever();
            entity.Property(run => run.Revision)
                .HasMaxLength(MailRuleEvaluationRunEntity.RevisionLength)
                .IsFixedLength();
            entity.Property(run => run.Ending).HasConversion<string>().HasMaxLength(64);
            entity.Property(run => run.Trigger)
                .HasConversion<string>()
                .HasMaxLength(64)
                .HasDefaultValue(MailRuleExecutionTrigger.RequestedRun)
                .IsRequired();

            // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
            entity.Property(run => run.ConcurrencyVersion).IsRowVersion();
        });

        // One row per account, which is what makes "one outstanding whole-mailbox classification run" a property of the
        // key rather than of a check. The scope is a text array because it is read back whole and never filtered on: the
        // run states which folders it walks, and nothing asks the database which runs walk one folder.
        modelBuilder.Entity<SpamClassificationRunEntity>(entity =>
        {
            entity.ToTable("spam_classification_runs");
            entity.HasKey(run => run.MailboxAccountId).HasName(SpamClassificationRunPrimaryKeyConstraintName);
            entity.Property(run => run.MailboxAccountId).HasMaxLength(128).ValueGeneratedNever();
            entity.Property(run => run.FolderAliases).IsRequired();
            entity.Property(run => run.Posture).HasConversion<string>().HasMaxLength(64).IsRequired();
            entity.Property(run => run.Profile)
                .HasMaxLength(SpamClassificationRunEntity.ProfileLength)
                .IsFixedLength();
            entity.Property(run => run.Ending).HasConversion<string>().HasMaxLength(64);

            // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
            entity.Property(run => run.ConcurrencyVersion).IsRowVersion();
        });

        modelBuilder.Entity<BackfillPositionEntity>(entity =>
        {
            entity.ToTable("backfill_positions");
            entity.HasKey(position => position.Name);
            entity.Property(position => position.Name).HasMaxLength(BackfillPositionEntity.MaximumNameLength);
            entity.Property(position => position.SensitiveContentStamp)
                .HasMaxLength(SensitiveContentDerivationStamp.Length)
                .IsFixedLength();
        });

        // Keyed by the scope an operator named rather than by a constant, which is what keeps two accounts' walks
        // independent. No foreign key onto the account: the row is a cursor over rows that are already keyed to one,
        // and requiring the account row would make the walk depend on a table it never reads.
        modelBuilder.Entity<MailRederivationPositionEntity>(entity =>
        {
            entity.ToTable("mail_rederivation_positions");
            entity.HasKey(position => new { position.MailboxAccountId, position.FolderAlias })
                .HasName(MailRederivationPositionPrimaryKeyConstraintName);
            entity.Property(position => position.MailboxAccountId).HasMaxLength(128);
            entity.Property(position => position.FolderAlias).HasMaxLength(128);

            // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
            entity.Property(position => position.ConcurrencyVersion).IsRowVersion();
        });

        modelBuilder.Entity<SynchronizationCheckpointEntity>(entity =>
        {
            entity.ToTable("synchronization_checkpoints");
            entity.HasKey(checkpoint => checkpoint.MailFolderId)
                .HasName(SynchronizationCheckpointPrimaryKeyConstraintName);
            entity.Property(checkpoint => checkpoint.MailFolderId).ValueGeneratedNever();

            // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
            entity.Property(checkpoint => checkpoint.ConcurrencyVersion).IsRowVersion();
            entity.HasOne(checkpoint => checkpoint.MailFolder)
                .WithOne(folder => folder.SynchronizationCheckpoint)
                .HasForeignKey<SynchronizationCheckpointEntity>(checkpoint => checkpoint.MailFolderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // No foreign key onto the mailbox account, which is the one relationship a reader would expect here. That row
        // is written by whichever synchronization run first binds a folder, so requiring it would mean a token could
        // only be stored for an account that has already synchronized — the opposite of the order an operator works in.
        // What follows is that removing an account has to remove this row deliberately rather than by cascade, which is
        // the erasure seam's job rather than the schema's.
        modelBuilder.Entity<MailboxRefreshTokenEntity>(entity =>
        {
            entity.ToTable("mailbox_refresh_tokens");
            entity.HasKey(token => token.MailboxAccountId);
            entity.Property(token => token.MailboxAccountId).HasMaxLength(128).ValueGeneratedNever();
            entity.Property(token => token.SealedRefreshToken).HasColumnType("bytea").IsRequired();
            entity.Property(token => token.DataEncryptionKeyId)
                .HasMaxLength(MailboxRefreshTokenEntity.MaximumKeyIdLength)
                .IsRequired();

            // What a key retirement is planned against: the pass that re-seals under a new key reads the accounts still
            // holding a value under the old one, and without this it would read every row to answer that.
            entity.HasIndex(token => token.DataEncryptionKeyId).HasDatabaseName(MailboxRefreshTokenKeyIndexName);
        });

        ConfigureMailboxMutation(modelBuilder);
        ConfigureOutgoingEmail(modelBuilder);
        ConfigureOutgoingEmailRecipient(modelBuilder);
        ConfigureOutgoingEmailContent(modelBuilder);
        ConfigureMailboxMutationAuditEntry(modelBuilder);
        ConfigureMailAnsweringAuditEntry(modelBuilder);
        ConfigureMailRuleExecution(modelBuilder);
        ConfigureContact(modelBuilder);
        ConfigureJob(modelBuilder);
        ConfigureJobSchedule(modelBuilder);
    }

    /// <summary>Declares the contact book: people, the addresses they use, and which of them is the default.</summary>
    /// <remarks>
    /// <para>
    /// The addresses are rows rather than an array column, which is what makes both rules over them structural. One
    /// address belongs to one person, enforced across the whole table rather than within a contact, because a book that
    /// let two records claim one mailbox could not answer who a message is from; and erasing a person takes their
    /// addresses with them through the foreign key rather than through a second statement somebody remembers to write.
    /// </para>
    /// <para>
    /// The default address is a column on the person instead of a flag on each address. A flag would need a filtered
    /// unique index to say that nobody has two, and that index refuses the intermediate row an update changing the choice
    /// passes through; a column changes the choice in the same statement that records it. It carries no foreign key onto
    /// the address row, because a key pointing back would make inserting either table first impossible.
    /// </para>
    /// <para>
    /// The origin is held as its own name for the reason every bounded value beside it is, and the concurrency token is
    /// there because a contact is amended in place — by the administration tool, by the MCP surface, and by collection —
    /// so an amendment written from state read earlier has to fail rather than win.
    /// </para>
    /// </remarks>
    private static void ConfigureContact(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ContactEntity>(entity =>
        {
            entity.ToTable("contacts");
            entity.HasKey(contact => contact.Id);
            entity.Property(contact => contact.Id).ValueGeneratedNever();
            entity.Property(contact => contact.DisplayName)
                .HasMaxLength(ContactEntity.MaximumDisplayNameLength)
                .IsRequired();
            entity.Property(contact => contact.DisplayNameSortKey)
                .HasMaxLength(ContactEntity.MaximumDisplayNameLength)
                .UseCollation("C")
                .IsRequired();
            entity.Property(contact => contact.PreferredNormalizedAddress)
                .HasMaxLength(ContactAddressEntity.MaximumAddressLength)
                .IsRequired();
            entity.Property(contact => contact.Note).HasMaxLength(ContactEntity.MaximumNoteLength);
            entity.Property(contact => contact.Origin).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(contact => contact.ConcurrencyVersion).IsRowVersion();

            // The one order the book is walked in, and the one a keyset page continues from. The identity settles two
            // people whose names compare equal, which is what makes the order total and the walk terminate. The column
            // is pinned to the C collation so that order is the ordinal one the domain derived the key to produce,
            // rather than whichever collation the database this runs on happens to have been created with.
            entity.HasIndex(contact => new { contact.DisplayNameSortKey, contact.Id })
                .HasDatabaseName(ContactListingIndexName);
        });

        modelBuilder.Entity<ContactAddressEntity>(entity =>
        {
            entity.ToTable("contact_addresses");
            entity.HasKey(address => address.Id);
            entity.Property(address => address.Id).ValueGeneratedNever();
            entity.Property(address => address.Address)
                .HasMaxLength(ContactAddressEntity.MaximumAddressLength)
                .IsRequired();
            entity.Property(address => address.NormalizedAddress)
                .HasMaxLength(ContactAddressEntity.MaximumAddressLength)
                .IsRequired();

            // No concurrency token of its own, which ADR 0001 asks to be justified rather than assumed. An address row
            // is only ever written by an amendment of the contact it hangs on, in the same transaction and the same
            // batch as that contact's own tokened update — the amendment stamps AmendedAt on every path — so the parent
            // row is what a competing write loses on, and a token here would only repeat that decision on a row that is
            // never reached on its own.

            // Unique across the book rather than within one contact, and named because a losing writer is recognized by
            // the constraint its insert violated: two callers claiming one address is a race whose retry resolves into
            // the answer naming whoever holds it, not a failure to report.
            entity.HasIndex(address => address.NormalizedAddress)
                .IsUnique()
                .HasDatabaseName(ContactAddressUniqueIndexName);

            entity.HasOne<ContactEntity>()
                .WithMany(contact => contact.Addresses)
                .HasForeignKey(address => address.ContactId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>Declares the queue of durable background work, and the three questions it is asked.</summary>
    /// <remarks>
    /// <para>
    /// The unique index is the idempotency guarantee itself rather than a support for one. Two triggers asking for the
    /// same execution at the same moment both pass any check the application could make between reading and writing, and
    /// only the database closes that window; the same work is therefore enqueued once because the second insert is
    /// refused, not because the code declined to attempt it. It spans every state a row can reach, terminal ones
    /// included, because a row that succeeded is exactly what stops the same trigger asking again — which is also why a
    /// row is never moved to another table when it is finished with, and why pruning is a retention decision with a
    /// correctness floor rather than housekeeping.
    /// </para>
    /// <para>
    /// The claim index carries the type and the instant a job becomes available, because the claim statement is the only
    /// query this table runs at any volume and those are what it selects on. It is filtered to the states a claim can
    /// still take, so a queue that has been running for a year holds an index the size of its backlog rather than of its
    /// history — and the claim repeats that same membership in its own predicate so PostgreSQL can prove the index
    /// applies to it. Naming the two claimable states rather than excluding the terminal ones is what keeps the filter
    /// correct as terminal states are added: a job that failed leaves the index the moment it stops being claimable.
    /// </para>
    /// <para>
    /// The account is a column with an index of its own rather than a value inside the payload, because erasure,
    /// retention, and any per-account bound have to reach a job by query. The foreign key is what makes that structural:
    /// removing an account takes its queued work with it instead of leaving rows pointing at a mailbox that is gone. A
    /// job belonging to no account leaves it null.
    /// </para>
    /// <para>
    /// Nothing here is mail content. A job type, an idempotency key composed of MailFathom's own names, an account
    /// identifier, a lease owner, and a document of references are what the row holds, which is what lets work be queued
    /// without the message being copied into a second place with retention obligations of its own.
    /// </para>
    /// </remarks>
    private static void ConfigureJob(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<JobEntity>(entity =>
        {
            entity.ToTable("jobs");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.Id).ValueGeneratedNever();
            entity.Property(job => job.JobType).HasMaxLength(64).IsRequired();
            entity.Property(job => job.IdempotencyKey)
                .HasMaxLength(JobIdempotencyKey.MaximumLength)
                .IsRequired();

            // A document rather than a schema: nothing queries into it, because the key, the type, the account, and the
            // available instant are all columns beside it.
            entity.Property(job => job.Payload).HasColumnType("jsonb").IsRequired();

            entity.Property(job => job.MailboxAccountId).HasMaxLength(128);
            entity.Property(job => job.LeaseOwner).HasMaxLength(JobLeaseOwner.MaximumLength);

            // Stored as text for the reason every other bounded value in this schema is: it stays readable in an ad-hoc
            // query and survives any later reordering of the enum.
            entity.Property(job => job.State).HasConversion<string>().HasMaxLength(64).IsRequired();
            entity.Property(job => job.LastFailureClassification).HasConversion<string>().HasMaxLength(64);
            entity.Property(job => job.LastFailureReason).HasMaxLength(JobFailureRecord.MaximumReasonLength);

            entity.HasIndex(job => new { job.JobType, job.IdempotencyKey })
                .IsUnique()
                .HasDatabaseName(JobIdentityUniqueIndexName);
            entity.HasIndex(job => new { job.JobType, job.AvailableAt })
                .HasDatabaseName(JobClaimIndexName)
                .HasFilter(
                    $"\"{nameof(JobEntity.State)}\" IN ('{nameof(JobState.Pending)}', '{nameof(JobState.Claimed)}')");
            entity.HasIndex(job => new { job.MailboxAccountId, job.EnqueuedAt })
                .HasDatabaseName(JobAccountIndexName);

            // Partial for the reason the claim index is: the state it is filtered to is a small part of a table that
            // grows with every enqueue, and an operator reading what has stopped orders by the instant it stopped. The
            // ordering columns are the keyset pair the page is continued on, so one index serves the reading whichever
            // of its two optional filters is applied.
            entity.HasIndex(job => new { job.StateChangedAt, job.Id })
                .HasDatabaseName(JobDeadLetterIndexName)
                .HasFilter($"\"{nameof(JobEntity.State)}\" = '{nameof(JobState.DeadLettered)}'");

            entity.HasOne(job => job.MailboxAccount)
                .WithMany()
                .HasForeignKey(job => job.MailboxAccountId)
                .OnDelete(DeleteBehavior.Cascade);
        });

    /// <summary>Declares what each recurring dispatch has already done, which is the only state a schedule keeps.</summary>
    /// <remarks>
    /// One row per declared schedule, keyed by the identity the declaration composes, so a second replica advancing a
    /// schedule writes the same row rather than adding one. The declarations themselves are configuration and are not
    /// stored: what is durable here is the occasion last accounted for and the job it enqueued, which is what a restart
    /// would otherwise have no way to tell from a fresh deployment.
    /// </remarks>
    private static void ConfigureJobSchedule(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<JobScheduleEntity>(entity =>
        {
            entity.ToTable("job_schedules");
            entity.HasKey(schedule => schedule.ScheduleId);
            entity.Property(schedule => schedule.ScheduleId)
                .HasMaxLength(JobScheduleId.MaximumLength)
                .ValueGeneratedNever();
        });

    /// <summary>Declares the record of what each rule concluded about each email, and what those conclusions asked for.</summary>
    /// <remarks>
    /// <para>
    /// Two tables rather than one, and the split is the pointer rather than normalization for its own sake. The execution
    /// states what a rule concluded; the rows beside it name the individual changes it asked for and the mutation record
    /// each one went into, which is the join between a rule's decision and what happened on the mailbox.
    /// </para>
    /// <para>
    /// <strong>No fact value is stored anywhere here.</strong> The facts a condition read are kept as their declared
    /// names, and the expression itself stays in the configuration the recorded revision identifies. A rule name, a
    /// folder alias, a mutation name, and a set of fact names are all MailFathom's own names for things, which is what
    /// lets a decision be explained without the mail being copied into a second place.
    /// </para>
    /// <para>
    /// The email is a foreign key with a cascade. That is what makes the history inherit the deletion obligations of the
    /// mail it describes rather than merely undertaking to; the mutation record it points at is deliberately not one,
    /// because the two records have retention windows of their own and a key would let the trail's window erase the
    /// history with it.
    /// </para>
    /// <para>
    /// It is append-only. Nothing amends an execution, so no row carries a concurrency token: there is no second writer
    /// for one to protect against.
    /// </para>
    /// </remarks>
    private static void ConfigureMailRuleExecution(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MailRuleExecutionEntity>(entity =>
        {
            entity.ToTable("mail_rule_executions");
            entity.HasKey(execution => execution.Id);
            entity.Property(execution => execution.Id).ValueGeneratedNever();
            entity.Property(execution => execution.MailboxAccountId).HasMaxLength(128).IsRequired();
            entity.Property(execution => execution.RuleName)
                .HasMaxLength(MailRuleExecutionEntity.MaximumRuleNameLength)
                .IsRequired();
            entity.Property(execution => execution.Revision)
                .HasMaxLength(MailRuleExecutionEntity.RevisionLength)
                .IsFixedLength()
                .IsRequired();

            // The bounded values are held as their own names rather than as converted enums, for the reason the
            // answering record states: a converted enum fails materialization on a name it declares no member for, and
            // this record is read a page at a time, so a value a later build wrote would fail every page from there on.
            entity.Property(execution => execution.Trigger)
                .HasMaxLength(MailRuleExecutionEntity.MaximumOutcomeLength)
                .IsRequired();
            entity.Property(execution => execution.Outcome)
                .HasMaxLength(MailRuleExecutionEntity.MaximumOutcomeLength)
                .IsRequired();
            entity.Property(execution => execution.ConditionFailure)
                .HasMaxLength(MailRuleExecutionEntity.MaximumOutcomeLength);
            entity.Property(execution => execution.ReadFacts).IsRequired();

            // The cascade is the point of the association: an erased message reaches every rule decision that was made
            // about it, through the email's own deletion path rather than through a rule somebody remembers.
            entity.HasOne<StoredEmailEntity>()
                .WithMany()
                .HasForeignKey(execution => execution.StoredEmailId)
                .OnDelete(DeleteBehavior.Cascade);

            // The three indexes are the three questions the history is asked. The first is also what retention erases
            // through, which is why the account leads it and the instant follows.
            entity.HasIndex(execution => new { execution.MailboxAccountId, execution.EvaluatedAt, execution.Id })
                .HasDatabaseName(MailRuleExecutionTimelineIndexName);
            entity.HasIndex(execution =>
                    new { execution.MailboxAccountId, execution.RuleName, execution.EvaluatedAt, execution.Id })
                .HasDatabaseName(MailRuleExecutionRuleIndexName);
            entity.HasIndex(execution => new { execution.StoredEmailId, execution.EvaluatedAt, execution.Id })
                .HasDatabaseName(MailRuleExecutionEmailIndexName);
        });

        modelBuilder.Entity<MailRuleExecutedActionEntity>(entity =>
        {
            entity.ToTable("mail_rule_executed_actions");

            // The pair is the key rather than a surrogate, because one rule declares one change at one position however
            // many times the pass reads it. That makes the uniqueness the identity instead of a constraint beside one.
            entity.HasKey(action => new { action.MailRuleExecutionId, action.Position });

            entity.Property(action => action.Mutation)
                .HasMaxLength(MailRuleExecutionEntity.MaximumOutcomeLength)
                .IsRequired();
            entity.Property(action => action.Outcome)
                .HasMaxLength(MailRuleExecutionEntity.MaximumOutcomeLength)
                .IsRequired();
            entity.Property(action => action.FailureReason)
                .HasMaxLength(MailRuleExecutionEntity.MaximumOutcomeLength);
            entity.Property(action => action.Destination)
                .HasMaxLength(MailRuleExecutionEntity.MaximumAliasLength);

            entity.HasOne<MailRuleExecutionEntity>()
                .WithMany(execution => execution.Actions)
                .HasForeignKey(action => action.MailRuleExecutionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>Declares the record one answered question leaves behind, and the emails it names.</summary>
    /// <remarks>
    /// <para>
    /// Two tables rather than one, and the split is the deletion obligation rather than normalization for its own sake.
    /// The entry states that a question was answered from an account's mailbox, which stays true after the account is
    /// removed from configuration; the rows beside it name individual messages, and a message erased anywhere in this
    /// system has to stop being named here. A column holding an array of identifiers would satisfy the first and quietly
    /// defeat the second.
    /// </para>
    /// <para>
    /// Nothing here is mail content. An identifier, an endpoint alias, an instruction version, two instants, and two
    /// bounded outcomes are MailFathom's own names for things — which is what lets the run be explained without the mail
    /// being copied.
    /// </para>
    /// <para>
    /// It is append-only. Nothing amends an entry, so the row carries no concurrency token: there is no second writer
    /// for one to protect against, and the uniqueness below is what makes a repeated append leave the record as it was.
    /// </para>
    /// </remarks>
    private static void ConfigureMailAnsweringAuditEntry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MailAnsweringAuditEntryEntity>(entity =>
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
                .HasDatabaseName(MailAnsweringAuditEntryRunUniqueIndexName);

            // The one index the record is worked through, and it serves both readers: a page is the account's entries
            // ordered by when they ended, and retention erases the same account's entries that ended before a cutoff.
            entity.HasIndex(record => new { record.MailboxAccountId, record.CompletedAt, record.Id })
                .HasDatabaseName(MailAnsweringAuditEntryTimelineIndexName);
        });

        modelBuilder.Entity<MailAnsweringAuditedEmailEntity>(entity =>
        {
            entity.ToTable("mail_answering_audited_emails");

            // The pair is the key rather than a surrogate, because one run names one message once however many of its
            // lookups found it. That makes the uniqueness the identity instead of a constraint beside one.
            entity.HasKey(read => new { read.MailAnsweringAuditEntryId, read.StoredEmailId });

            entity.HasOne<MailAnsweringAuditEntryEntity>()
                .WithMany(record => record.Emails)
                .HasForeignKey(read => read.MailAnsweringAuditEntryId)
                .OnDelete(DeleteBehavior.Cascade);

            // No navigation to the stored email, so appending a row reads nothing: what is recorded is that a run
            // retrieved this identifier. The cascade is the point of the association — an erased message reaches every
            // run that read it, through the email's own deletion path rather than through a rule somebody remembers.
            entity.HasOne<StoredEmailEntity>()
                .WithMany()
                .HasForeignKey(read => read.StoredEmailId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

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
    private static void ConfigureMailboxMutationAuditEntry(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<MailboxMutationAuditEntryEntity>(entity =>
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
                .HasDatabaseName(MailboxMutationAuditEntryMutationUniqueIndexName);

            // The one index the trail is worked through, and it serves both readers: a page is the account's entries
            // ordered by when they ended, and retention erases the same account's entries that ended before a cutoff.
            // A data-subject erasure by local email is a rare, deliberate operator act and reads the table rather than
            // an index of its own, which keeps the write cost of an append to one index beyond the key.
            entity.HasIndex(entry => new { entry.MailboxAccountId, entry.CompletedAt, entry.Id })
                .HasDatabaseName(MailboxMutationAuditEntryTimelineIndexName);
        });

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
    private static void ConfigureMailboxMutation(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<MailboxMutationEntity>(entity =>
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

            ConfigureMailboxMutationIndexes(entity);

            entity.HasOne(mutation => mutation.StoredEmail)
                .WithMany(email => email.Mutations)
                .HasForeignKey(mutation => mutation.StoredEmailId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(mutation => mutation.MailFolder)
                .WithMany()
                .HasForeignKey(mutation => mutation.MailFolderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

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
    private static void ConfigureMailboxMutationIndexes(EntityTypeBuilder<MailboxMutationEntity> entity)
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
            .HasDatabaseName(MailboxMutationIdentityUniqueIndexName);

        entity.HasIndex(mutation => new { mutation.MailboxAccountId, mutation.RecordedAt })
            .HasDatabaseName(MailboxMutationOutstandingIndexName)
            .HasFilter($"\"{nameof(MailboxMutationEntity.Stage)}\" <> '{nameof(MailboxMutationStage.Completed)}'");

        entity.HasIndex(mutation => new
        {
            mutation.MailboxAccountId,
            mutation.DestinationFolderPath,
            mutation.PlacementUidValidity,
            mutation.PlacementUid,
        })
            .HasDatabaseName(MailboxMutationPlacementIndexName)
            .HasFilter($"\"{nameof(MailboxMutationEntity.PlacementObservedAt)}\" IS NULL");
    }

    /// <summary>Declares the durable record of every message this system has been asked to send.</summary>
    /// <remarks>
    /// <para>
    /// The row exists before any SMTP command is issued, which is what makes a non-atomic submission survivable: the
    /// stage says how far the attempt got, and the one stage that means "the body went out and the answer never came
    /// back" is written before the transmission rather than after it.
    /// </para>
    /// <para>
    /// Nothing here is mail content. The account, the requester identity, the reply codes, and the stage are this
    /// system's own or the server's own names for things, and the message itself is a row of its own that no query
    /// listing the outbox touches. The recipients are personal data and are the one thing on this record that could not
    /// be left out: a send cannot be resumed without knowing who is still owed it.
    /// </para>
    /// </remarks>
    private static void ConfigureOutgoingEmail(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<OutgoingEmailEntity>(entity =>
        {
            entity.ToTable("outgoing_emails");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.Id).ValueGeneratedNever();
            entity.Property(message => message.MailboxAccountId).HasMaxLength(128).IsRequired();
            entity.Property(message => message.RequesterIdentity)
                .HasMaxLength(OutgoingEmailRequester.MaximumIdentityLength)
                .IsRequired();

            // Stored as text for the reason the mutation stage is: both stay readable in an ad-hoc audit query and
            // survive any later reordering of their enum.
            entity.Property(message => message.RequesterOrigin).HasConversion<string>().HasMaxLength(64).IsRequired();
            entity.Property(message => message.Stage).HasConversion<string>().HasMaxLength(64).IsRequired();

            // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
            entity.Property(message => message.ConcurrencyVersion).IsRowVersion();

            entity.HasIndex(message => new
            {
                message.MailboxAccountId,
                message.RequesterOrigin,
                message.RequesterIdentity,
            })
                .IsUnique()
                .HasDatabaseName(OutgoingEmailIdentityUniqueIndexName);

            // Filtered to the sends that have not finished, so the structure holds what is queued and in flight rather
            // than every message the deployment has ever sent. A refused send stays in for the reason an abandoned
            // mutation does: giving up on it is what stops it being attempted, and it would be worth nothing if it also
            // stopped it being seen — so the filter names the three terminal stages rather than only the successful one.
            entity.HasIndex(message => new { message.MailboxAccountId, message.RecordedAt })
                .HasDatabaseName(OutgoingEmailOutstandingIndexName)
                .HasFilter(
                    $"\"{nameof(OutgoingEmailEntity.Stage)}\" NOT IN ("
                    + $"'{nameof(OutgoingEmailStage.Sent)}', "
                    + $"'{nameof(OutgoingEmailStage.Refused)}', "
                    + $"'{nameof(OutgoingEmailStage.Cancelled)}')");
        });

    /// <summary>Declares the people one outgoing email is offered to, and what the server said about each.</summary>
    /// <remarks>
    /// <para>
    /// A separate table rather than arrays on the record, because each recipient carries state that changes on its own:
    /// a message is offered per address and answered per address, so a mistyped address among five must not stop the
    /// other four and the four who received it must not be offered it again when the fifth is retried.
    /// </para>
    /// <para>
    /// Keyed by the record and the position in its recipient list. An address is personal data and a key is repeated
    /// into every index over a table, so the ordinal keys the row instead — and it keeps the recipients in the order the
    /// request named them, which is the order a composed message writes its headers in.
    /// </para>
    /// </remarks>
    private static void ConfigureOutgoingEmailRecipient(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<OutgoingEmailRecipientEntity>(entity =>
        {
            entity.ToTable("outgoing_email_recipients");
            entity.HasKey(recipient => new { recipient.OutgoingEmailId, recipient.Ordinal });
            entity.Property(recipient => recipient.Address)
                .HasMaxLength(OutgoingRecipient.MaximumAddressLength)
                .IsRequired();

            // Stored as text for the reason every other enum on this feature is, and required on both: a row whose text
            // names no declared value fails the read rather than being taken as a neighbouring one by elimination.
            entity.Property(recipient => recipient.Role).HasConversion<string>().HasMaxLength(64).IsRequired();
            entity.Property(recipient => recipient.Status).HasConversion<string>().HasMaxLength(64).IsRequired();

            // A recipient row is mutated on its own — an attempt answers about this address without touching the record
            // above it — so the record's token would not notice two attempts settling one recipient differently.
            entity.Property(recipient => recipient.ConcurrencyVersion).IsRowVersion();

            entity.HasOne(recipient => recipient.OutgoingEmail)
                .WithMany(message => message.Recipients)
                .HasForeignKey(recipient => recipient.OutgoingEmailId)
                .HasConstraintName(OutgoingEmailRecipientForeignKeyName)
                .OnDelete(DeleteBehavior.Cascade);
        });

    /// <summary>Declares the raw MIME one outgoing email is transmitted as, stored once and read back per attempt.</summary>
    /// <remarks>
    /// <para>
    /// A one-to-one table whose primary key is also its foreign key, which is the arrangement the incoming content table
    /// uses and for the same reason: keeping the large binary value out of the record means listing what is queued never
    /// loads a single message's bytes. PostgreSQL stores an oversized <c>bytea</c> out of line automatically.
    /// </para>
    /// <para>
    /// The message is written once and read back rather than recomposed, because a message rebuilt between attempts
    /// carries a different <c>Message-ID</c> and would thread as a second message in every recipient's client. The
    /// cascade is the erasure obligation: deleting the record destroys the message it points at, so an outgoing email
    /// cannot outlive the record that says who it was for.
    /// </para>
    /// </remarks>
    private static void ConfigureOutgoingEmailContent(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<OutgoingEmailContentEntity>(entity =>
        {
            entity.ToTable("outgoing_email_contents");
            entity.HasKey(content => content.OutgoingEmailId);
            entity.Property(content => content.OutgoingEmailId).ValueGeneratedNever();
            entity.Property(content => content.RawMime).IsRequired();
            entity.Property(content => content.Sha256Hash).HasMaxLength(32).IsRequired();

            entity.HasOne(content => content.OutgoingEmail)
                .WithOne(message => message.Content)
                .HasForeignKey<OutgoingEmailContentEntity>(content => content.OutgoingEmailId)
                .HasConstraintName(OutgoingEmailContentForeignKeyName)
                .OnDelete(DeleteBehavior.Cascade);
        });

    /// <summary>Declares the derived search document and the lexical index built over it.</summary>
    /// <remarks>
    /// <para>
    /// The search vector is a stored generated column rather than a column MailFathom writes, so it cannot drift from the
    /// text beside it: no code path, migration, or ad-hoc update can leave a row whose vector describes text the row no
    /// longer holds. PostgreSQL requires such an expression to be immutable, which is why the text search configuration
    /// is named explicitly and why the participant addresses are a text column here rather than the arrays on the
    /// stored email — the array-to-text functions are only stable.
    /// </para>
    /// <para>
    /// GIN is the index method a containment-style <c>tsvector</c> lookup needs; a B-tree over the column would serve
    /// no query that search issues.
    /// </para>
    /// </remarks>
    private void ConfigureEmailSearchDocument(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<EmailSearchDocumentEntity>(entity =>
        {
            entity.ToTable("email_search_documents");
            entity.HasKey(document => document.StoredEmailId);
            entity.Property(document => document.StoredEmailId).ValueGeneratedNever();
            entity.Property(document => document.SubjectText)
                .HasMaxLength(EmailSearchDocumentEntity.MaximumIndexedSubjectLength);

            // Stored as text for the reason the content-availability reason is: the source stays readable in an audit
            // query and survives any later reordering of the enum.
            entity.Property(document => document.TextSource).HasConversion<string>().HasMaxLength(64).IsRequired();

            // Carried without an index of its own. Both readers of the column ask which rows are *not* stamped with the
            // current configuration, and a B-tree operator class holds no inequality operator, so nothing could use one;
            // the staleness count and the rebuilding walk scan, which is what a once-per-start figure and a walk that
            // reads whole rows anyway can afford.
            entity.Property(document => document.SensitiveContentStamp)
                .HasMaxLength(SensitiveContentDerivationStamp.Length)
                .IsFixedLength();

            entity.HasOne(document => document.StoredEmail)
                .WithOne(email => email.SearchDocument)
                .HasForeignKey<EmailSearchDocumentEntity>(document => document.StoredEmailId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasGeneratedTsVectorColumn(
                    document => document.SearchVector,
                    this.textSearchConfiguration.Value,
                    document => new { document.SubjectText, document.ParticipantAddresses, document.BodyText })
                .HasIndex(document => document.SearchVector)
                .HasDatabaseName(EmailSearchDocumentVectorIndexName)
                .HasMethod("GIN");
        });

    /// <summary>Declares what classification concluded about an occurrence, and the facts it concluded it from.</summary>
    /// <remarks>
    /// <para>
    /// Both tables cascade from the email, which is what keeps derived data inside whatever erasure and retention reach
    /// the mail it describes: nothing has to remember to delete a classification, and nothing can leave one behind
    /// describing a message that is gone.
    /// </para>
    /// <para>
    /// The signals cascade from the classification rather than from the email, so replacing a verdict replaces the facts
    /// it rested on in one statement. Keeping a superseded verdict's signals beside the new ones would leave a record
    /// nobody could read.
    /// </para>
    /// <para>
    /// Every enumeration is stored as text for the reason each other outcome here is: it stays readable in an ad-hoc
    /// query and survives a later reordering of the enum.
    /// </para>
    /// </remarks>
    private static void ConfigureEmailSpamClassification(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EmailSpamClassificationEntity>(entity =>
        {
            entity.ToTable("email_spam_classifications");
            entity.HasKey(classification => classification.StoredEmailId)
                .HasName(EmailSpamClassificationPrimaryKeyConstraintName);
            entity.Property(classification => classification.StoredEmailId).ValueGeneratedNever();
            entity.Property(classification => classification.Verdict).HasConversion<string>().HasMaxLength(64).IsRequired();
            entity.Property(classification => classification.DecidedBy).HasConversion<string>().HasMaxLength(64).IsRequired();
            entity.Property(classification => classification.CorpusRevision)
                .HasMaxLength(EmailSpamClassificationEntity.MaximumCorpusRevisionLength);
            entity.Property(classification => classification.Profile)
                .HasMaxLength(EmailSpamClassificationEntity.ProfileLength)
                .IsFixedLength();

            // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
            entity.Property(classification => classification.ConcurrencyVersion).IsRowVersion();

            entity.HasOne(classification => classification.StoredEmail)
                .WithOne(email => email.SpamClassification)
                .HasForeignKey<EmailSpamClassificationEntity>(classification => classification.StoredEmailId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmailSpamClassificationSignalEntity>(entity =>
        {
            entity.ToTable("email_spam_classification_signals");
            entity.HasKey(signal => signal.Id);
            entity.Property(signal => signal.Kind).HasConversion<string>().HasMaxLength(64).IsRequired();
            entity.Property(signal => signal.Source).HasConversion<string>().HasMaxLength(64).IsRequired();
            entity.Property(signal => signal.Name)
                .HasMaxLength(EmailSpamClassificationSignalEntity.MaximumNameLength)
                .IsRequired();
            entity.Property(signal => signal.Observation)
                .HasMaxLength(EmailSpamClassificationSignalEntity.MaximumObservationLength);
            entity.Property(signal => signal.Origin)
                .HasMaxLength(EmailSpamClassificationSignalEntity.MaximumOriginLength)
                .IsRequired();

            entity.HasIndex(signal => new { signal.StoredEmailId, signal.Ordinal })
                .IsUnique()
                .HasDatabaseName(EmailSpamClassificationSignalOrdinalUniqueIndexName);

            entity.HasOne(signal => signal.Classification)
                .WithMany(classification => classification.Signals)
                .HasForeignKey(signal => signal.StoredEmailId)
                .HasConstraintName(EmailSpamClassificationSignalForeignKeyName)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    /// <summary>Declares the vector spaces this deployment has embedded into.</summary>
    /// <remarks>
    /// <para>
    /// The identity columns are fixed at insertion and the fingerprint over them carries a unique index, so activating a
    /// declaration whose geometry already exists resolves to the existing row rather than inserting a second one that
    /// would be re-embedded from scratch. Nothing in the schema stops an update of an identity column; what the schema
    /// owns is the consequence, since a changed identity would collide with its own fingerprint or leave one describing
    /// nothing.
    /// </para>
    /// <para>
    /// The alternate key over the identifier and the dimension exists for one reader: <see cref="EmailEmbeddingEntity" />
    /// points a composite foreign key at it, which is the only way a check constraint — which sees one row — can be made
    /// to enforce a width this table declares.
    /// </para>
    /// </remarks>
    private static void ConfigureEmbeddingProfile(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<EmbeddingProfileEntity>(entity =>
        {
            entity.ToTable("embedding_profiles");
            entity.HasKey(profile => profile.Id);
            entity.Property(profile => profile.Id).ValueGeneratedNever();

            entity.Property(profile => profile.Provider)
                .HasMaxLength(EmbeddingProfileIdentity.MaximumProviderLength)
                .IsRequired();
            entity.Property(profile => profile.ModelIdentifier)
                .HasMaxLength(EmbeddingProfileIdentity.MaximumModelIdentifierLength)
                .IsRequired();
            entity.Property(profile => profile.ModelVersion)
                .HasMaxLength(EmbeddingProfileIdentity.MaximumModelVersionLength);
            entity.Property(profile => profile.PassageInstruction)
                .HasMaxLength(EmbeddingInputPreparation.MaximumPassageInstructionLength);

            // Stored as text for the reason every other enum column here is: the value stays readable in an ad-hoc audit
            // query and survives any later reordering of the enum.
            entity.Property(profile => profile.DistanceMetric).HasConversion<string>().HasMaxLength(64).IsRequired();
            entity.Property(profile => profile.LifecycleState).HasConversion<string>().HasMaxLength(64).IsRequired();

            // Fixed length because a SHA-256 digest has one, and text rather than `bytea` because activation compares
            // this value and an operator reading a profile reads it.
            entity.Property(profile => profile.IdentityFingerprint)
                .HasMaxLength(EmbeddingProfileFingerprint.Length)
                .IsFixedLength()
                .IsRequired();

            entity.HasIndex(profile => profile.IdentityFingerprint)
                .IsUnique()
                .HasDatabaseName(EmbeddingProfileFingerprintUniqueIndexName);

            // Unique over the state itself and partial to the two states that admit one row each, which is how one
            // index expresses both halves of the invariant: at most one generation being built, and at most one being
            // read. The literals are the enum member names because the column stores those names.
            entity.HasIndex(profile => profile.LifecycleState)
                .IsUnique()
                .HasFilter($"\"LifecycleState\" IN ('{nameof(EmbeddingProfileLifecycleState.Building)}', '{nameof(EmbeddingProfileLifecycleState.Active)}')")
                .HasDatabaseName(EmbeddingProfileLifecycleUniqueIndexName);

            entity.HasAlternateKey(profile => new { profile.Id, profile.Dimension })
                .HasName(EmbeddingProfileDimensionAlternateKeyName);
        });

    /// <summary>Declares the vector column and the two constraints that keep a stored vector meaning what its profile says.</summary>
    /// <remarks>
    /// <para>
    /// The column is pgvector's dimensionless <c>vector</c>, so two profiles of different widths coexist in one table and
    /// each is served by an expression index created when it is activated. The width is enforced instead by a pair: a
    /// composite foreign key onto the profile's own dimension, which refuses a width the profile never declared, and a
    /// check constraint comparing that column against the stored vector's actual length. Neither half works alone —
    /// PostgreSQL evaluates a check against one row, so without the foreign key the check would only prove a vector
    /// agrees with a number beside it.
    /// </para>
    /// <para>
    /// The chunk cascades and the profile does not. Deleting a message must reach every vector derived from it, which is
    /// what the cascade makes structural rather than a rule somebody has to remember; a profile, by contrast, is what a
    /// stored vector's attribution points at, so the schema refuses to remove one while a vector still names it.
    /// </para>
    /// </remarks>
    private static void ConfigureEmailEmbedding(ModelBuilder modelBuilder) =>
        modelBuilder.Entity<EmailEmbeddingEntity>(entity =>
        {
            entity.ToTable(
                "email_embeddings",
                table => table.HasCheckConstraint(
                    EmailEmbeddingDimensionCheckConstraintName,
                    $"vector_dims(\"{nameof(EmailEmbeddingEntity.Embedding)}\") = \"{nameof(EmailEmbeddingEntity.Dimension)}\""));

            // The chunk and the profile together, because that pair is what a vector is: re-embedding a passage under
            // the profile already serving it replaces the row rather than adding one. Named so an idempotent upsert has
            // a constraint to conflict on.
            entity.HasKey(embedding => new { embedding.EmailChunkId, embedding.EmbeddingProfileId })
                .HasName(EmailEmbeddingPrimaryKeyConstraintName);

            entity.Property(embedding => embedding.Embedding).HasColumnType("vector").IsRequired();

            entity.HasOne(embedding => embedding.EmailChunk)
                .WithMany(chunk => chunk.Embeddings)
                .HasForeignKey(embedding => embedding.EmailChunkId)
                .OnDelete(DeleteBehavior.Cascade);

            // Declared rather than left to the foreign key's own convention, because a superseded generation is deleted
            // in bounded batches read by profile, and that read would otherwise scan every vector in the table.
            entity.HasIndex(embedding => new { embedding.EmbeddingProfileId, embedding.Dimension })
                .HasDatabaseName(EmailEmbeddingProfileIndexName);

            entity.HasOne(embedding => embedding.EmbeddingProfile)
                .WithMany(profile => profile.Embeddings)
                .HasForeignKey(embedding => new { embedding.EmbeddingProfileId, embedding.Dimension })
                .HasPrincipalKey(profile => new { profile.Id, profile.Dimension })
                .HasConstraintName(EmailEmbeddingProfileForeignKeyName)
                .OnDelete(DeleteBehavior.Restrict);
        });

    /// <summary>Declares the indexes mailbox reads are planned against, and with them the timeline ordering contract.</summary>
    /// <remarks>
    /// <para>
    /// Both timeline indexes reproduce <see cref="EmailTimelinePosition.NewestFirst" /> column for column: the received
    /// timestamp descending with unknown timestamps last, then the identifier descending. Keyset pagination is only
    /// contiguous while the two agree, so a change to either is a change to both.
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
    private static void ConfigureStoredEmailIndexes(EntityTypeBuilder<StoredEmailEntity> entity)
    {
        entity.HasIndex(email => new { email.MailFolderId, email.UidValidity, email.Uid })
            .IsUnique()
            .HasDatabaseName(StoredEmailOccurrenceUniqueIndexName);

        entity.HasIndex(email => new { email.MailboxAccountId, email.ReceivedAt, email.Id })
            .HasDatabaseName(StoredEmailAccountTimelineIndexName)
            .IsDescending(false, true, true)
            .HasNullSortOrder(NullSortOrder.Unspecified, NullSortOrder.NullsLast, NullSortOrder.Unspecified);

        entity.HasIndex(email => new { email.MailFolderId, email.ReceivedAt, email.Id })
            .HasDatabaseName(StoredEmailFolderTimelineIndexName)
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
            .HasDatabaseName(StoredEmailReconciliationQueueIndexName)
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
                StoredEmailAwaitingContentIndexName)
            .HasDatabaseName(StoredEmailAwaitingContentIndexName)
            .HasFilter(
                $"\"{nameof(StoredEmailEntity.ContentAvailability)}\" = '{nameof(StoredEmailContentAvailability.AwaitingStorageHeadroom)}'");

        // The order a requested whole-mailbox rule run walks in. It is the identity rather than the timeline because a
        // walk that has to resume needs a total order no later write disturbs, and because the position it commits is
        // one column rather than a nullable timestamp paired with a tie-breaker.
        entity.HasIndex(email => new { email.MailboxAccountId, email.Id })
            .HasDatabaseName(StoredEmailAccountIdentityIndexName);

        // The arrival queue, and the filter is the whole point of it. In steady state almost every row of an account
        // has been evaluated, so without the filter this read would walk the account's entire index once per run to
        // find the handful of rows that qualify — and it runs for every account on every synchronization run.
        entity.HasIndex(
                email => new { email.MailboxAccountId, email.Id },
                StoredEmailAwaitingRuleEvaluationIndexName)
            .HasDatabaseName(StoredEmailAwaitingRuleEvaluationIndexName)
            .HasFilter($"\"{nameof(StoredEmailEntity.RulesEvaluatedAt)}\" IS NULL");

        entity.HasIndex(email => email.SenderNormalizedAddress)
            .HasDatabaseName(StoredEmailSenderIndexName);

        entity.HasIndex(email => email.ToAddresses)
            .HasDatabaseName(StoredEmailToAddressesIndexName)
            .HasMethod("GIN");

        entity.HasIndex(email => email.CcAddresses)
            .HasDatabaseName(StoredEmailCcAddressesIndexName)
            .HasMethod("GIN");

        entity.HasIndex(email => email.ReplyToAddresses)
            .HasDatabaseName(StoredEmailReplyToAddressesIndexName)
            .HasMethod("GIN");

        // A keyword filter asks whether the array contains one value, which is the containment operator a GIN index
        // over a text[] serves — the same shape and the same reason as the three address arrays above.
        entity.HasIndex(email => email.RemoteKeywords)
            .HasDatabaseName(StoredEmailRemoteKeywordsIndexName)
            .HasMethod("GIN");
    }
}
