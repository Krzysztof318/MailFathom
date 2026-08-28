// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Persistence.Accounts.Configurations;
using MailFathom.Infrastructure.Persistence.Answering.Configurations;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Contacts.Configurations;
using MailFathom.Infrastructure.Persistence.Delivery.Configurations;
using MailFathom.Infrastructure.Persistence.Emails.Configurations;
using MailFathom.Infrastructure.Persistence.Emails.Threads.Configurations;
using MailFathom.Infrastructure.Persistence.Embeddings.Configurations;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Jobs.Configurations;
using MailFathom.Infrastructure.Persistence.Mutations.Configurations;
using MailFathom.Infrastructure.Persistence.Owners.Configurations;
using MailFathom.Infrastructure.Persistence.Rules.Configurations;
using MailFathom.Infrastructure.Persistence.Settings.Configurations;
using MailFathom.Infrastructure.Persistence.Spam.Configurations;
using MailFathom.Infrastructure.Persistence.Synchronization.Configurations;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence;

/// <summary>EF Core context for local MailFathom persistence.</summary>
/// <remarks>
/// The context declares which entity types the model holds and in what order their configurations are applied, and
/// nothing about how any one of them is mapped. Each table's mapping is an <see cref="IEntityTypeConfiguration{TEntity}" />
/// beside the store that reads it, so adding an entity adds a file rather than editing this one, and the names the
/// mappings state rather than leave to convention are <see cref="PersistenceConstraintNames" />.
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailFathomDbContext : DbContext
{
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

    internal DbSet<RootSettingsEntity> RootSettings => this.Set<RootSettingsEntity>();

    internal DbSet<OwnerAccountEntity> OwnerAccounts => this.Set<OwnerAccountEntity>();

    internal DbSet<OwnerCredentialEntity> OwnerCredentials =>
        this.Set<OwnerCredentialEntity>();

    internal DbSet<OwnerStoredContentEntity> OwnerStoredContent => this.Set<OwnerStoredContentEntity>();

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

    internal DbSet<MailRederivationRunEntity> MailRederivationRuns => this.Set<MailRederivationRunEntity>();

    internal DbSet<ContentMoveRunEntity> ContentMoveRuns => this.Set<ContentMoveRunEntity>();

    internal DbSet<MailRuleEvaluationRunEntity> MailRuleEvaluationRuns => this.Set<MailRuleEvaluationRunEntity>();

    internal DbSet<SynchronizationCheckpointEntity> SynchronizationCheckpoints => this.Set<SynchronizationCheckpointEntity>();

    internal DbSet<MailboxRefreshTokenEntity> MailboxRefreshTokens => this.Set<MailboxRefreshTokenEntity>();

    internal DbSet<MailboxMutationEntity> MailboxMutations => this.Set<MailboxMutationEntity>();

    internal DbSet<OutgoingEmailEntity> OutgoingEmails => this.Set<OutgoingEmailEntity>();

    internal DbSet<OutgoingEmailRecipientEntity> OutgoingEmailRecipients =>
        this.Set<OutgoingEmailRecipientEntity>();

    internal DbSet<OutgoingEmailContentEntity> OutgoingEmailContents =>
        this.Set<OutgoingEmailContentEntity>();

    internal DbSet<OutgoingEmailFilingEntity> OutgoingEmailFilings =>
        this.Set<OutgoingEmailFilingEntity>();

    internal DbSet<RecurringSendEntity> RecurringSends => this.Set<RecurringSendEntity>();

    internal DbSet<RecurringSendRecipientEntity> RecurringSendRecipients =>
        this.Set<RecurringSendRecipientEntity>();

    internal DbSet<RecurringSendDraftEntity> RecurringSendDrafts => this.Set<RecurringSendDraftEntity>();
    internal DbSet<MailDraftEntity> MailDrafts => this.Set<MailDraftEntity>();

    internal DbSet<MailDraftRecipientEntity> MailDraftRecipients => this.Set<MailDraftRecipientEntity>();

    internal DbSet<MailDraftCopyEntity> MailDraftCopies => this.Set<MailDraftCopyEntity>();

    internal DbSet<MailDraftContentEntity> MailDraftContents => this.Set<MailDraftContentEntity>();

    internal DbSet<MailboxMutationAuditEntryEntity> MailboxMutationAuditEntries =>
        this.Set<MailboxMutationAuditEntryEntity>();

    internal DbSet<MailAnsweringAuditEntryEntity> MailAnsweringAuditEntries =>
        this.Set<MailAnsweringAuditEntryEntity>();

    internal DbSet<MailRuleExecutionEntity> MailRuleExecutions => this.Set<MailRuleExecutionEntity>();

    internal DbSet<EmailThreadEntity> EmailThreads => this.Set<EmailThreadEntity>();

    internal DbSet<EmailThreadIdentifierEntity> EmailThreadIdentifiers => this.Set<EmailThreadIdentifierEntity>();

    internal DbSet<ContactEntity> Contacts => this.Set<ContactEntity>();

    internal DbSet<ContactAddressEntity> ContactAddresses => this.Set<ContactAddressEntity>();

    internal DbSet<JobEntity> Jobs => this.Set<JobEntity>();

    internal DbSet<JobScheduleEntity> JobSchedules => this.Set<JobScheduleEntity>();

    /// <inheritdoc />
    /// <remarks>
    /// The order below is the order the configurations are applied in, and it is not alphabetical: a configuration that
    /// points a foreign key at an alternate key another one declares is applied after it, so the principal is already in
    /// the model when the dependent names it.
    /// </remarks>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Declared before any entity so the baseline migration emits CREATE EXTENSION ahead of the tables. The image
        // ships pgvector, which makes the extension installable but not installed: without this, the first vector
        // column would fail on a type PostgreSQL does not know. Enabling it here rather than in the migration that
        // introduces that column keeps the RAG stage from needing a migration whose only content is this statement,
        // and costs an empty database one catalogue entry.
        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.ApplyConfiguration(new RootSettingsConfiguration());
        modelBuilder.ApplyConfiguration(new OwnerAccountConfiguration());
        modelBuilder.ApplyConfiguration(new OwnerCredentialConfiguration());
        modelBuilder.ApplyConfiguration(new OwnerStoredContentConfiguration());
        modelBuilder.ApplyConfiguration(new MailboxAccountConfiguration());
        modelBuilder.ApplyConfiguration(new MailFolderConfiguration());
        modelBuilder.ApplyConfiguration(new StoredEmailConfiguration());
        modelBuilder.ApplyConfiguration(new EmailMessageContentConfiguration());
        modelBuilder.ApplyConfiguration(new EmailSearchDocumentConfiguration(this.textSearchConfiguration));
        modelBuilder.ApplyConfiguration(new EmailChunkConfiguration());
        modelBuilder.ApplyConfiguration(new EmbeddingProfileConfiguration());
        modelBuilder.ApplyConfiguration(new EmailEmbeddingConfiguration());
        modelBuilder.ApplyConfiguration(new EmbeddingSpendPeriodConfiguration());
        modelBuilder.ApplyConfiguration(new EmailContentRepairRequestConfiguration());
        modelBuilder.ApplyConfiguration(new EmailSpamClassificationConfiguration());
        modelBuilder.ApplyConfiguration(new EmailSpamClassificationSignalConfiguration());
        modelBuilder.ApplyConfiguration(new MailRuleEvaluationRunConfiguration());
        modelBuilder.ApplyConfiguration(new SpamClassificationRunConfiguration());
        modelBuilder.ApplyConfiguration(new BackfillPositionConfiguration());
        modelBuilder.ApplyConfiguration(new MailRederivationPositionConfiguration());
        modelBuilder.ApplyConfiguration(new MailRederivationRunConfiguration());
        modelBuilder.ApplyConfiguration(new ContentMoveRunConfiguration());
        modelBuilder.ApplyConfiguration(new SynchronizationCheckpointConfiguration());
        modelBuilder.ApplyConfiguration(new MailboxRefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new MailboxMutationConfiguration());
        modelBuilder.ApplyConfiguration(new OutgoingEmailConfiguration());
        modelBuilder.ApplyConfiguration(new OutgoingEmailRecipientConfiguration());
        modelBuilder.ApplyConfiguration(new OutgoingEmailContentConfiguration());
        modelBuilder.ApplyConfiguration(new OutgoingEmailFilingConfiguration());
        modelBuilder.ApplyConfiguration(new RecurringSendConfiguration());
        modelBuilder.ApplyConfiguration(new RecurringSendRecipientConfiguration());
        modelBuilder.ApplyConfiguration(new RecurringSendDraftConfiguration());
        modelBuilder.ApplyConfiguration(new MailDraftConfiguration());
        modelBuilder.ApplyConfiguration(new MailDraftRecipientConfiguration());
        modelBuilder.ApplyConfiguration(new MailDraftCopyConfiguration());
        modelBuilder.ApplyConfiguration(new MailDraftContentConfiguration());
        modelBuilder.ApplyConfiguration(new MailboxMutationAuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new MailAnsweringAuditEntryConfiguration());
        modelBuilder.ApplyConfiguration(new MailAnsweringAuditedEmailConfiguration());
        modelBuilder.ApplyConfiguration(new MailRuleExecutionConfiguration());
        modelBuilder.ApplyConfiguration(new MailRuleExecutedActionConfiguration());
        modelBuilder.ApplyConfiguration(new EmailThreadConfiguration());
        modelBuilder.ApplyConfiguration(new EmailThreadIdentifierConfiguration());
        modelBuilder.ApplyConfiguration(new ContactConfiguration());
        modelBuilder.ApplyConfiguration(new ContactAddressConfiguration());
        modelBuilder.ApplyConfiguration(new JobConfiguration());
        modelBuilder.ApplyConfiguration(new JobScheduleConfiguration());
    }
}
