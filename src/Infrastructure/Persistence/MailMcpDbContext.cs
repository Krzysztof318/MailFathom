// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.CodeCoverage;
using MailMcp.Domain.Emails;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>EF Core context for local MailMcp persistence.</summary>
[RequiresIntegrationCoverage]
internal sealed class MailMcpDbContext : DbContext
{
    internal const string SynchronizationCheckpointPrimaryKeyConstraintName = "pk_synchronization_checkpoints";

    internal const string MailFolderBindingUniqueIndexName = "ix_mail_folders_account_alias_generation";

    internal const string StoredEmailOccurrenceUniqueIndexName = "ix_stored_emails_folder_uidvalidity_uid";

    internal const string StoredEmailAccountTimelineIndexName = "ix_stored_emails_account_timeline";

    internal const string StoredEmailFolderTimelineIndexName = "ix_stored_emails_folder_timeline";

    internal const string StoredEmailSenderIndexName = "ix_stored_emails_sender";

    internal const string StoredEmailToAddressesIndexName = "ix_stored_emails_to_addresses";

    internal const string StoredEmailCcAddressesIndexName = "ix_stored_emails_cc_addresses";

    internal const string StoredEmailReplyToAddressesIndexName = "ix_stored_emails_reply_to_addresses";

    internal const string EmailSearchDocumentVectorIndexName = "ix_email_search_documents_search_vector";

    private readonly PostgresTextSearchConfiguration textSearchConfiguration;

    /// <summary>Initializes a new MailMcp EF Core context.</summary>
    /// <param name="options">The provider and connection configuration.</param>
    /// <param name="textSearchConfiguration">The validated text search configuration the lexical index is built with.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="textSearchConfiguration" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The configuration reaches the model rather than a query because it is part of the schema: it is compiled into a
    /// generated column, so it is fixed for a deployment's data and changing it is a schema change that reindexes.
    /// EF caches one model per context type, which is correct here because the composition root binds exactly one
    /// configuration per process and validates it before the container is built.
    /// </remarks>
    public MailMcpDbContext(
        DbContextOptions<MailMcpDbContext> options,
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

    internal DbSet<BackfillPositionEntity> BackfillPositions => this.Set<BackfillPositionEntity>();

    internal DbSet<SynchronizationCheckpointEntity> SynchronizationCheckpoints => this.Set<SynchronizationCheckpointEntity>();

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
            entity.HasKey(account => account.Id);
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
    }

    /// <summary>Declares the derived search document and the lexical index built over it.</summary>
    /// <remarks>
    /// <para>
    /// The search vector is a stored generated column rather than a column MailMcp writes, so it cannot drift from the
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
