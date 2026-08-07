// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
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

    internal const string StoredEmailSenderIndexName = "ix_stored_emails_sender";

    internal const string StoredEmailToAddressesIndexName = "ix_stored_emails_to_addresses";

    internal const string StoredEmailCcAddressesIndexName = "ix_stored_emails_cc_addresses";

    internal const string StoredEmailReplyToAddressesIndexName = "ix_stored_emails_reply_to_addresses";

    internal const string EmailSearchDocumentVectorIndexName = "ix_email_search_documents_search_vector";

    internal const string EmailChunkOrdinalUniqueIndexName = "ix_email_chunks_email_ordinal";

    /// <summary>The unique index over an embedding profile's identity, which is what makes activation idempotent.</summary>
    /// <remarks>
    /// Named because a losing writer is recognized by the constraint its insert violated: two operators activating the
    /// same declaration is a race that resolves to the profile already registered, not a failure to report.
    /// </remarks>
    internal const string EmbeddingProfileFingerprintUniqueIndexName = "ix_embedding_profiles_identity_fingerprint";

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

    internal DbSet<EmailContentRepairRequestEntity> EmailContentRepairRequests => this.Set<EmailContentRepairRequestEntity>();

    internal DbSet<BackfillPositionEntity> BackfillPositions => this.Set<BackfillPositionEntity>();

    internal DbSet<SynchronizationCheckpointEntity> SynchronizationCheckpoints => this.Set<SynchronizationCheckpointEntity>();

    internal DbSet<MailboxRefreshTokenEntity> MailboxRefreshTokens => this.Set<MailboxRefreshTokenEntity>();

    internal DbSet<MailboxMutationEntity> MailboxMutations => this.Set<MailboxMutationEntity>();

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

        modelBuilder.Entity<BackfillPositionEntity>(entity =>
        {
            entity.ToTable("backfill_positions");
            entity.HasKey(position => position.Name);
            entity.Property(position => position.Name).HasMaxLength(BackfillPositionEntity.MaximumNameLength);
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
    }

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
    }
}
