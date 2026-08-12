// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.DownloadAttachment;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Administration;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Indexing;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.ListEmails;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Persistence;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Scanning;
using MailFathom.Application.Spam.Signals;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Embeddings;
using MailFathom.Infrastructure.Folders;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.Attachments;
using MailFathom.Infrastructure.Mail.MailKit;
using MailFathom.Infrastructure.Mail.MailKit.Writes;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Accounts;
using MailFathom.Infrastructure.Persistence.Answering;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Embeddings;
using MailFathom.Infrastructure.Persistence.Mutations;
using MailFathom.Infrastructure.Persistence.Rules;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Spam;
using MailFathom.Infrastructure.Persistence.Synchronization;
using MailFathom.Infrastructure.Resilience;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using MailFathom.Infrastructure.Security.OAuth;
using MailFathom.Infrastructure.SensitiveContent.PersonalData;
using MailFathom.Infrastructure.SensitiveContent.Secrets;
using MailKit.Net.Imap;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MailFathom.Infrastructure;

/// <summary>Infrastructure dependency registration.</summary>
[RequiresIntegrationCoverage]
public static class ServiceCollectionExtensions
{
    /// <summary>The largest token endpoint response read, beyond which the request fails.</summary>
    /// <remarks>An RFC 6749 token response is a few hundred bytes; even a JWT access token stays well inside this. The limit exists so a replaced or compromised authorization server cannot make a synchronization run buffer an unbounded body.</remarks>
    private const int MailOAuthTokenResponseSizeLimitInBytes = 64 * 1024;

    /// <summary>How many response bytes one analyzed character of text may produce, which bounds an analyzer answer.</summary>
    /// <remarks>
    /// An entity is reported as an object of about ninety bytes naming its type, its offsets, and its score, and the
    /// shortest thing any recognizer matches is a handful of characters. Four characters per entity is therefore already
    /// pessimistic, and the ceiling exists for the case that is not a scan at all: an address that answers with something
    /// other than an analyzer must not be able to make a guarded read buffer an unbounded body. Exceeding it fails the
    /// scan, which fails the operation closed like any other analyzer that could not answer.
    /// </remarks>
    private const int AnalyzerResponseBytesPerAnalyzedCharacter = 24;

    /// <summary>What an analyzer answer costs beyond its entities, which is the JSON array around them.</summary>
    private const int AnalyzerResponseEnvelopeBytes = 4 * 1024;

    /// <summary>How much longer than one scan's own budget the analyzer transport waits before it gives up.</summary>
    /// <remarks>
    /// Deliberately looser than the configured per-scan budget, so a slow analyzer surfaces as this deployment's own
    /// timeout — which the redactor reports as the scanner not answering in time and names the budget it spent — rather
    /// than as a transport exception from underneath it that says nothing about a budget at all. It is added to the
    /// configured budget to produce the resilience handler's attempt and total-request timeouts, which is where the
    /// transport's bound lives; <see cref="AddPersonalDataAnalyzerClient" /> records why it is not a client property.
    /// </remarks>
    private static readonly TimeSpan AnalyzerTransportTimeoutMargin = TimeSpan.FromSeconds(30);

    /// <summary>Registers the secret reference grammar, the shipped scheme adapters, and the composite dispatch.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="interpretation">How configured secret-bearing values are interpreted.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="interpretation" /> is not a defined member.</exception>
    /// <remarks>
    /// <para>
    /// A provider for a managed store registers its own <see cref="ISecretSchemeResolver" /> through its own extension
    /// beside this call and needs no edit here, because the composite dispatches over whatever adapters it is handed.
    /// </para>
    /// <para>
    /// The mode is checked here rather than trusted, because a numeric configuration value binds to an undefined
    /// member without complaint. The composite compares against the two inline modes explicitly, so an undefined value
    /// would fall through and behave like the strictest mode — the safe outcome, but reached by accident and reported
    /// in the startup log as a mode nobody selected. A configuration mistake has to fail rather than be absorbed.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSecretResolution(
        this IServiceCollection services,
        SecretValueInterpretation interpretation)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!Enum.IsDefined(interpretation))
        {
            throw new ArgumentOutOfRangeException(
                nameof(interpretation),
                interpretation,
                "The configured secret value interpretation is not a supported mode.");
        }

        services.AddSingleton(new SecretResolutionOptions(interpretation));
        services.AddSingleton<ISecretFileReader, FileSystemSecretFileReader>();
        services.AddSingleton<IEnvironmentVariableReader, ProcessEnvironmentVariableReader>();
        services.AddSingleton<ISecretSchemeResolver, SystemdCredentialSecretReferenceResolver>();
        services.AddSingleton<ISecretSchemeResolver, FileSecretReferenceResolver>();
        services.AddSingleton<ISecretSchemeResolver, EnvironmentVariableSecretReferenceResolver>();
        services.AddSingleton<ISecretSchemeResolver, PlaintextSecretReferenceResolver>();
        services.AddSingleton<ISecretReferenceResolver, CompositeSecretReferenceResolver>();
        // The loaders belong here rather than beside the mail adapter or the endpoint: they turn resolved bytes into
        // typed material and know nothing about IMAP or about hosting, so a future material kind joins them instead of
        // touching a scheme adapter. The two are separate types because they enforce opposite rules about a private
        // key — an anchor must not carry one, a server identity must.
        services.AddSingleton<TrustAnchorLoader>();
        services.AddSingleton<TlsServerCertificateLoader>();

        return services;
    }

    /// <summary>Registers EF Core persistence, MailKit mailbox access, and application synchronization services.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="currentConnectionSettings">Supplies where the PostgreSQL connection string and its password currently come from.</param>
    /// <param name="textSearchConfiguration">The validated PostgreSQL text search configuration the lexical index is built with.</param>
    /// <param name="answeringBudget">The validated ceilings one question about the mailbox is subject to.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// The settings arrive already read rather than as an <c>IConfiguration</c> this method reaches into, so which key
    /// holds them stays a host decision and this assembly gains no configuration dependency. They arrive as an
    /// accessor rather than as a value because a secret reference can be repointed by a configuration reload, and a
    /// value captured at registration would keep authenticating with the reference the operator replaced.
    /// </para>
    /// <para>
    /// The embedding stores registered here are the tables; the units of work that write vectors into them are not,
    /// and belong to <see cref="AddEmailEmbeddingGeneration" /> for the reason that method states. A registration
    /// added here that <em>constructor-injects</em> an <see cref="ITextEmbeddingGenerator" /> belongs there instead,
    /// because that is the descriptor a container cannot build where no chain was declared.
    /// </para>
    /// <para>
    /// A factory that asks for one optionally is the other case and stays here. Nothing validates a factory's body, so
    /// such a descriptor builds in every container and answers <see langword="null" /> where there is no generator —
    /// which is what the read path needs, because search is served by every deployment and a registration made only
    /// where a chain was declared would leave a lexical-only instance unable to resolve a search at all.
    /// <see cref="SemanticEmailSearch" /> is the one such registration.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        Func<IServiceProvider, PostgresConnectionSettings> currentConnectionSettings,
        PostgresTextSearchConfiguration textSearchConfiguration,
        MailAnsweringBudget answeringBudget)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(currentConnectionSettings);
        ArgumentNullException.ThrowIfNull(textSearchConfiguration);
        ArgumentNullException.ThrowIfNull(answeringBudget);

        // A value rather than an accessor, unlike the connection settings beside it: this one is compiled into the
        // search vector's column definition, so it is fixed for a deployment's schema and a reload cannot adopt a new
        // one without reindexing. Changing it is a migration, not a configuration reload.
        services.AddSingleton(textSearchConfiguration);
        services.AddSingleton(provider => new PostgresConnectionStringProvider(
            () => currentConnectionSettings(provider),
            provider.GetRequiredService<ISecretReferenceResolver>(),
            provider.GetRequiredService<SecretResolutionOptions>(),
            provider.GetRequiredService<ILogger<PostgresConnectionStringProvider>>()));
        services.AddHostedService(provider => provider.GetRequiredService<PostgresConnectionStringProvider>());
        // The adapter that composed the pool is the only thing that knows which setting currently supplies the
        // credential, so it is also what answers whether a reloaded candidate can be adopted.
        services.AddSingleton<IDatabaseConnectionSettingsValidator>(provider => provider.GetRequiredService<PostgresConnectionStringProvider>());
        // The container both creates and disposes the data source, so no second owner can leave its pool open. The
        // credential is not part of the composed string: it is retrieved per physical connection so that rotating it
        // needs neither a restart nor a rebuilt pool.
        services.AddSingleton(provider =>
        {
            var connectionStringProvider = provider.GetRequiredService<PostgresConnectionStringProvider>();
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionStringProvider.ConnectionString);
            connectionStringProvider.SupplyThePasswordPerConnection(dataSourceBuilder);
            // The vector type is resolved per data source rather than per context, so a pool built without this reads a
            // stored embedding as an unknown type at the first query instead of failing where the mapping was declared.
            dataSourceBuilder.UseVector();

            return dataSourceBuilder.Build();
        });
        // EnableRetryOnFailure is deliberately not configured. A retrying execution strategy refuses the
        // user-initiated transaction PersistenceSessionFactory opens for every session, so turning it on would fail
        // every write at session start rather than merely leave it un-retried. Adopting it instead means handing each
        // unit of work to Database.CreateExecutionStrategy().ExecuteAsync so the strategy can replay it whole, and
        // dropping OutboundDependency.DatabaseCommandExecution from those paths so the two never stack.
        // EF Core reports every executed command at Information, which is one record per round trip and therefore most
        // of what a deployment writes: a synchronization run, a backfill sweep, and every MCP read reach the database
        // repeatedly, so the records an operator is reading for are buried among the SQL that served them. Lowering the
        // event to Debug is not the same as filtering the category out, which would remove the records at every level:
        // they stay reachable through Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command, which is what a
        // slow or unexpected query is diagnosed with. Only this event moves, so a failed command still surfaces at its
        // own level.
        services.AddDbContext<MailFathomDbContext>((provider, options) => options
            .UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), npgsql => npgsql.UseVector())
            .ConfigureWarnings(warnings => warnings.Log((RelationalEventId.CommandExecuted, LogLevel.Debug))));
        services.AddScoped<IPersistenceSessionFactory, PersistenceSessionFactory>();
        services.AddScoped<ISynchronizationCheckpointStore, SynchronizationCheckpointStore>();
        // Registered here rather than beside the chunker it calls, because what it is is a table: it decides which
        // passage rows a message owns. Its `IEmailTextChunker` comes from the AI boundary, which this project may not
        // reference, so a composition root that registers persistence without the local derivations resolves nothing.
        services.AddScoped<EmailChunkWriter>();
        // The backlog is a singleton because the bound it enforces is one process-wide limit on how much embedding work
        // is held in memory; a scoped one would hold that bound per scope, and a synchronization run would be offering
        // into a backlog no worker is reading. Registered whether or not this deployment embeds, because the
        // synchronization run offers into it either way and an instance that has activated no profile simply has a
        // worker that finds nothing to do.
        services.AddSingleton<IEmailEmbeddingBacklog, BoundedEmailEmbeddingBacklog>();
        services.AddSingleton<EmailEmbeddingTelemetry>();
        // Registered whichever providers this deployment declared, and as one instance behind both ports: the state is
        // one fact per provider about the whole process, and a second instance would leave a health check reading what
        // nothing had written to. An instance that declares no provider simply reports both roles as unobserved, which
        // is what "nothing has been asked yet" should look like.
        services.AddSingleton<AiProviderHealthTracker>();
        services.AddSingleton<IAiProviderHealthRecorder>(provider => provider.GetRequiredService<AiProviderHealthTracker>());
        services.AddSingleton<IAiProviderHealthReader>(provider => provider.GetRequiredService<AiProviderHealthTracker>());
        // A singleton for the reason the backlog is: the sweep's outstanding count is one figure about the whole
        // instance, and a gauge answering per scope would publish whichever scope observed it last.
        services.AddSingleton<EmailEmbeddingBackfillTelemetry>();
        services.AddScoped<IActiveEmbeddingProfileReader, ActiveEmbeddingProfileReader>();
        services.AddScoped<IEmailEmbeddingStore, EmailEmbeddingStore>();
        // Registered whether or not this deployment embeds, for the reason the backlog is: what a period spent is a
        // fact about the instance rather than about an activation, and an operator deciding whether to declare a
        // ceiling reads it before there is anything to bound.
        services.AddScoped<IEmbeddingSpendLedger, EmbeddingSpendLedger>();
        // The gate over that ledger is registered here rather than beside the generation work for the same reason.
        // Where a period stands is what an activation weighs its estimate against and what a status command reports,
        // and both are asked of an instance that has declared nothing at all.
        services.AddScoped<EmbeddingSpendGate>();
        // A singleton because the pass it schedules is one thing the process does: a scoped schedule would let an
        // activation bring forward a pass nothing is waiting on, and would report a due instant whichever request
        // happened to create it last. Registered whether or not this deployment embeds, because the status surface
        // reads it on every instance; what decides that no pass is ever scheduled is the worker saying so, not the
        // absence of anything to embed.
        services.AddSingleton<EmbeddingBackfillSchedule>();
        // The only registration here that changes the schema. It is scoped like every other store so that a caller
        // which has opened a persistence session gets its statement inside that session's transaction rather than
        // beside it.
        services.AddScoped<IEmbeddingProfileVectorIndex, EmbeddingProfileVectorIndex>();
        services.AddScoped<IStoredEmailEmbeddingBackfillStore, StoredEmailEmbeddingBackfillStore>();
        services.AddScoped<IEmbeddingGenerationStore, EmbeddingGenerationStore>();
        // The two operator acts on a generation, registered here rather than beside the generation work because neither
        // resolves an `ITextEmbeddingGenerator`: they move a profile row and maintain an index, so they build in every
        // container. What decides that there is anything to activate is the declaration the caller reads, not this.
        services.AddScoped<EmbeddingProfileActivation>();
        services.AddScoped<EmbeddingReindexCancellation>();
        // What the administrative surface asks of those two: the counting in front of an activation, and the one read
        // that says whether semantic search is working. Registered unconditionally like the acts they wrap, because an
        // instance that declared no provider is exactly the one whose operator most needs to be told so.
        services.AddScoped<IEmbeddingWorkloadReader, EmbeddingWorkloadReader>();
        services.AddScoped<CountedEmbeddingActivation>();
        services.AddScoped<EmbeddingStatusReader>();
        services.AddScoped<IEmailMetadataRepository, StoredEmailMetadataRepository>();
        services.AddScoped<IDatabaseSchemaInspector, EfCoreDatabaseSchemaInspector>();
        services.AddScoped<IEmailContentStore, EmailContentStore>();
        services.AddScoped<IStoredEmailContentInventory, StoredEmailContentInventory>();
        services.AddScoped<IStoredEmailExtractionBackfillStore, StoredEmailExtractionBackfillStore>();
        services.AddScoped<IStoredEmailReconciliationStore, StoredEmailReconciliationStore>();
        services.AddScoped<IMailRuleEvaluationStore, MailRuleEvaluationStore>();
        services.AddScoped<IMailRuleEvaluationRunStore, MailRuleEvaluationRunStore>();
        services.AddSingleton<MailRuleHistoryTelemetry>();
        services.AddScoped<IMailRuleExecutionStore, MailRuleExecutionStore>();

        // Registered whether or not classification is switched on, for the reason every other store here is: what a
        // deployment decides is whether anything calls it, and a port resolvable only under one configuration is a
        // composition that fails at the moment somebody changes their mind rather than at startup.
        services.AddScoped<IEmailSpamClassificationStore, EmailSpamClassificationStore>();
        services.AddScoped<IClassifiableEmailReader, ClassifiableEmailReader>();

        // The read side takes no persistence session and joins no transaction, so its ports are registered beside the
        // write repositories rather than through one of them.
        services.AddScoped<IStoredEmailTimelineReader, StoredEmailTimelineReader>();
        services.AddScoped<IStoredEmailSummaryReader, StoredEmailSummaryReader>();
        services.AddScoped<IEmailSearchIndexReader, StoredEmailSearchIndexReader>();
        services.AddScoped<IEmailVectorSearchIndexReader, EmailVectorSearchIndexReader>();
        // The one read-path service built by hand, because its embedding generator is the one dependency a supported
        // deployment may not have: an instance that declared no endpoint chain registers no `ITextEmbeddingGenerator`,
        // and a constructor injection of one would make lexical-only search fail to resolve rather than run. Asking the
        // provider keeps that decision where the composition root made it and out of the use case.
        //
        // It stays here rather than joining AddEmailEmbeddingGeneration, which is called only where a chain was
        // declared: search is served by every deployment, so a registration made there would leave a lexical-only
        // instance unable to resolve MailboxSearchReader at all.
        services.AddScoped(provider => new SemanticEmailSearch(
            provider.GetRequiredService<IActiveEmbeddingProfileReader>(),
            provider.GetRequiredService<IEmailVectorSearchIndexReader>(),
            provider.GetRequiredService<IAiProviderHealthReader>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ITextEmbeddingGenerator>()));
        services.AddScoped<ISynchronizationFreshnessReader, SynchronizationFreshnessReader>();
        // The one write a read path performs. It joins no session for the reason its port states, so it is registered
        // beside the readers rather than with the repositories that take one.
        services.AddScoped<IEmailContentRepairRequestStore, EmailContentRepairRequestStore>();
        // Registered here rather than beside the OAuth client it serves, because what it is is a table: the token
        // source asks for a credential and this is what knows the credential lives in PostgreSQL, sealed.
        services.AddScoped<IMailboxRefreshTokenStore, MailboxRefreshTokenStore>();
        services.AddScoped<MailboxRefreshTokenRecorder>();
        // MimeKit arrives with MailKit, so message parsing needs no dependency of its own; the adapter keeps its types
        // out of Application the same way the IMAP adapter keeps MailKit's out.
        services.AddScoped<IEmailMimeReader>(provider => new MimeKitEmailMimeReader(
            provider.GetRequiredService<EmailMimeExtractionOptions>()));
        // Beside the metadata reader and separate from it, because it parses only the header block and needs none of the
        // structural limits a body walk is bounded by: the parser stops at the blank line that ends the headers.
        services.AddScoped<IEmailSpamHeaderReader, MimeKitEmailSpamHeaderReader>();
        // The HTML sanitizer the renderer owns is built per instance rather than shared, so no configuration of it can
        // be changed by one request and observed by another.
        services.AddScoped<IEmailContentRenderer>(provider => new MimeKitEmailContentRenderer(
            provider.GetRequiredService<EmailMimeExtractionOptions>()));
        // Beside the renderer because it parses the same bytes under the same structural limits, and separate from it
        // because it holds one part open while the renderer holds nothing: a download states what it is serving and then
        // streams it, which is two steps a rendering has no use for.
        services.AddScoped<IEmailAttachmentContentReader>(provider => new MimeKitEmailAttachmentContentReader(
            provider.GetRequiredService<EmailMimeExtractionOptions>()));
        // Both halves of the attachment capability, registered as singletons because neither holds anything a scope
        // owns: the key behind a signature is resolved per operation and erased with it, exactly as the encryptor's is.
        // The settings arrive as a value because where this deployment publishes itself is a restart-level fact.
        services.AddSingleton<IAttachmentDownloadLinkIssuer>(provider => new SignedAttachmentDownloadLinkIssuer(
            provider.GetRequiredService<DataEncryptionKeyRing>(),
            provider.GetRequiredService<AttachmentDownloadSettings>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IAttachmentDownloadTicketReader>(provider => new SignedAttachmentDownloadTicketReader(
            provider.GetRequiredService<DataEncryptionKeyRing>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IMailFolderResolutionStore, MailFolderResolutionStore>();
        services.AddScoped<IStoredMailFolderMirrorStore, StoredMailFolderMirrorStore>();
        services.AddScoped<IMailFolderMappingChangeAuditor, LoggedMailFolderMappingChangeAuditor>();
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
        services.AddScoped<MailFolderResolver>();
        services.AddScoped<MailFolderReferenceResolver>();
        services.AddScoped<UnmirroredMailFolderEraser>();
        services.AddScoped<MailboxSynchronizer>();
        services.AddScoped<MailboxReconciler>();
        services.AddScoped<StoredEmailExtractionBackfill>();
        services.AddScoped<MailboxScopeResolver>();
        services.AddScoped<MailAccountDirectoryReader>();
        services.AddScoped<MailboxTimelineReader>();
        services.AddScoped<EmailContentReader>();
        services.AddScoped<EmailAttachmentDownloadReader>();
        services.AddScoped<MailboxSearchReader>();
        // Both halves of classification, registered for every deployment rather than only where it is switched on: what
        // the switch decides is whether the classifier does anything, and the classifier asks the settings reader that.
        // The scanner is the one dependency a supported deployment may not have — this change ships no implementation of
        // the port at all — so it is asked for rather than injected, exactly as the embedding generator is.
        services.AddScoped<DeterministicSpamClassifier>();
        services.AddScoped(provider => new EmailSpamClassifier(
            provider.GetRequiredService<IClassifiableEmailReader>(),
            provider.GetRequiredService<IEmailContentStore>(),
            provider.GetRequiredService<IEmailSpamHeaderReader>(),
            provider.GetRequiredService<IJunkMailFolderCatalog>(),
            provider.GetRequiredService<DeterministicSpamClassifier>(),
            provider.GetRequiredService<ISpamClassificationSettingsReader>(),
            provider.GetRequiredService<IEmailSpamClassificationStore>(),
            provider.GetRequiredService<OptimisticConcurrencyRetryPolicy>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ISpamScanner>()));
        // Registered for every deployment rather than only where a chat endpoint was declared, because what it is is a
        // reading of the search above: an instance that answers no questions simply resolves it and never calls it, and
        // the bounds it hands passages over under are the same wherever the retrieval is reached from.
        services.AddSingleton(answeringBudget.Retrieval);
        services.AddSingleton(answeringBudget.Run);
        services.AddSingleton(answeringBudget.Period);
        // Registered as itself as well as behind the port, so a deployment that turns the model-judged second pass on
        // can wrap this one rather than rebuild it. Both resolve the same scoped instance, so an instance that adds no
        // filter is unchanged by the shape.
        services.AddScoped<MailboxKnowledgeSearch>();
        services.AddScoped<IEmailKnowledgeSearch>(provider => provider.GetRequiredService<MailboxKnowledgeSearch>());
        // Built by hand for the reason the semantic search above is: the answering agent is the one dependency a
        // supported deployment may not have, and a constructor injection of it would leave an instance that declared no
        // chat endpoint unable to resolve the capability at all — which is the very instance that has to be able to
        // report that it answers no questions.
        services.AddScoped(provider => new MailAnsweringCapability(
            provider.GetRequiredService<SemanticEmailSearch>(),
            provider.GetRequiredService<IAiProviderHealthReader>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<IMailQuestionAnswerer>()));
        services.AddScoped<MailboxQuestionReader>();
        // The two halves of what a run leaves behind, registered for every deployment because both decide for
        // themselves whether they have anything to publish: the span exists only where something is listening, and the
        // record only for an account whose operator turned it on. A singleton for the span because it holds one
        // activity source, and scoped for the record because it commits through the scoped persistence session.
        services.AddSingleton<MailAnsweringAuditTelemetry>();
        services.AddSingleton<IMailAnsweringRunTelemetry, MailAnsweringRunTelemetry>();
        services.AddScoped<IMailAnsweringAuditEntryStore, MailAnsweringAuditEntryStore>();
        services.AddScoped<IMailAnsweringAuditTrail, MailAnsweringAuditTrail>();
        services.AddScoped<MailAnsweringAuditTrailRetention>();
        // Beside the retrieval bounds above and registered for every deployment for the same reason: what they bound is
        // a response rather than a provider, so an instance that answers no questions simply resolves them and never
        // publishes one.
        services.AddSingleton(answeringBudget.Answer);
        // A singleton because a ceiling over a period is one answer about the deployment: a ledger per scope would let
        // every concurrent question believe it was the first one of the period. Registered for every deployment for the
        // reason the bounds above are — an instance that answers nothing admits nothing and spends nothing.
        services.AddSingleton<MailAnsweringSpendTracker>();
        services.AddSingleton<IMailAnsweringSpendLedger>(provider => provider.GetRequiredService<MailAnsweringSpendTracker>());
        // The cache outlives every scope because a token is valid for whichever work unit next needs the account,
        // while the source that fills it is scoped to the configuration snapshot it resolves settings from.
        services.AddSingleton<MailAccessTokenCache>();

        AddMailOAuthTokenClient(services);

        services.AddScoped<IMailAccessTokenSource>(provider => new MailOAuthAccessTokenSource(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<IMailOAuthSettingsProvider>(),
            provider.GetRequiredService<IMailboxRefreshTokenStore>(),
            provider.GetRequiredService<MailAccessTokenCache>(),
            provider.GetRequiredService<OutboundOperationExecutor>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<MailOAuthAccessTokenSource>>()));
        services.AddScoped<IMailboxSessionFactory>(provider => new MailKitImapMailboxSessionFactory(
            static () => new ImapClient(),
            provider.GetRequiredService<IImapAccountSettingsProvider>(),
            provider.GetRequiredService<IMailAccessTokenSource>(),
            provider.GetRequiredService<OutboundOperationExecutor>(),
            provider.GetRequiredService<ITransientFailureClassifier>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IMailboxNotificationSessionFactory>(provider => new MailKitImapNotificationSessionFactory(
            static () => new ImapClient(),
            provider.GetRequiredService<IImapAccountSettingsProvider>(),
            provider.GetRequiredService<IMailAccessTokenSource>(),
            provider.GetRequiredService<OutboundOperationExecutor>(),
            provider.GetRequiredService<ITransientFailureClassifier>(),
            MailKitImapChangeSubscription.RequestFolderNotificationsAsync,
            provider.GetRequiredService<TimeProvider>()));
        // The pool is a singleton because the bound it enforces is one write connection per account across the whole
        // process; a scoped one would hold that bound per scope and let two concurrent runs open two. The factory in
        // front of it stays scoped like every other mail adapter, and carries no state of its own.
        services.AddSingleton<MailboxMutationTelemetry>();
        services.AddSingleton(provider => new MailboxWriteConnectionPool(
            static () => new ImapClient(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<OutboundOperationExecutor>(),
            provider.GetRequiredService<ITransientFailureClassifier>(),
            provider.GetRequiredService<MailboxWriteSessionOptions>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<MailboxWriteConnectionPool>>()));
        services.AddScoped<IMailboxWriteSessionFactory>(provider => new MailKitImapWriteSessionFactory(
            provider.GetRequiredService<MailboxWriteConnectionPool>(),
            provider.GetRequiredService<MailboxMutationTelemetry>()));
        // Registered beside the write session and against the same pool, because a creation is issued over the account's
        // one write connection. It is a separate port rather than a method on the session above, so a component holding
        // one of the two can never reach the other.
        services.AddScoped<IRemoteFolderCreator>(provider => new MailKitRemoteFolderCreator(
            provider.GetRequiredService<MailboxWriteConnectionPool>(),
            provider.GetRequiredService<ILogger<MailKitRemoteFolderCreator>>()));
        // The record is written before the session that acts on it is opened, so the store is registered beside the
        // other repositories that take a persistence session rather than with the mail adapters above.
        services.AddScoped<IMailboxMutationRecordStore, MailboxMutationRecordStore>();
        // Read by synchronization rather than by the performer, so that a relocation coming back through an ordinary
        // run is recognized as MailFathom's own instead of being stored as a second email.
        services.AddScoped<IMailboxMutationReconciliationStore, MailboxMutationReconciliationStore>();
        // The history a finished change leaves behind, which is a second table with a lifetime of its own rather than
        // the operational record above: that one ends with the mutation, and this one is kept for as long as the
        // account's retention says and is erased by the pass beside it.
        services.AddScoped<IMailboxMutationAuditEntryStore, MailboxMutationAuditEntryStore>();
        services.AddSingleton<MailboxMutationAuditTelemetry>();
        services.AddScoped<IMailboxMutationAuditTrail, MailboxMutationAuditTrail>();
        services.AddScoped<MailboxMutationAuditTrailRetention>();
        services.AddScoped<IMailboxMutationPerformer, MailboxMutationPerformer>();
        // A singleton, because the gauges it publishes are the process's and the account snapshots behind them outlive
        // any one run; the pass that fills them is scoped like everything else that reaches a mail server.
        services.AddSingleton<MailboxConvergenceTelemetry>();
        // A singleton for the same reason: the level it publishes belongs to the deployment's one content store rather
        // than to any run, and the counters beside it accumulate across every account.
        services.AddSingleton<MailboxContentVolumeTelemetry>();
        services.AddScoped<MailboxMutationConverger>();
        services.AddScoped<IRemoteFolderCatalog>(provider => new MailKitRemoteFolderCatalog(
            static () => new ImapClient(),
            provider.GetRequiredService<IImapAccountSettingsProvider>(),
            provider.GetRequiredService<IMailAccessTokenSource>(),
            provider.GetRequiredService<OutboundOperationExecutor>(),
            provider.GetRequiredService<ITransientFailureClassifier>()));

        return services;
    }

    /// <summary>Registers the units of work that turn a message's passages into vectors.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="AddInfrastructure" /> because both of these resolve an
    /// <see cref="ITextEmbeddingGenerator" />, which comes from the AI boundary and exists only where a deployment
    /// declared an embedding chain. Registering them beside the stores they write through would put a descriptor in
    /// every container that cannot be constructed in most of them, and a container that validates its descriptors on
    /// build — which is what a Development run does — then fails to start an instance that was never going to embed
    /// anything. Serving lexical search alone is a supported state, so the condition is expressed by not making the
    /// registration rather than by making one that would fail.
    /// </para>
    /// <para>
    /// The three are one call because they are one decision: the backfill's unit of work is one message brought up to
    /// date by that same generator, the upkeep pass is that walk plus the transitions it completes, so a deployment
    /// that resolves none of them is the only other shape.
    /// </para>
    /// <para>
    /// Semantic retrieval reads the same generator and is deliberately <em>not</em> here. It is asked for through a
    /// factory rather than injected, so its descriptor builds without one, and it has to: a search is served by every
    /// deployment, so registering it only where a chain was declared would make a lexical-only instance fail to
    /// resolve the search use case instead of serving it lexically.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddEmailEmbeddingGeneration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<StoredEmailEmbeddingGenerator>();
        services.AddScoped<StoredEmailEmbeddingBackfill>();
        services.AddScoped<EmbeddingGenerationUpkeep>();

        return services;
    }

    /// <summary>Registers the in-process detector of credentials in mail text, and what it declares it can find.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Called only where the <c>Secrets</c> switch is on, which is what makes an opt-in nobody took cost nothing: with
    /// it off no corpus is assembled, no expression is compiled, and neither descriptor exists.
    /// </para>
    /// <para>
    /// The catalog is registered beside the scanner rather than always, because startup refuses a switch that is on
    /// with nothing behind it, and a catalog present without a detector would turn that refusal into a scanner that
    /// runs and finds nothing. Both are singletons: the corpus is compiled once and the scanner holds it.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSecretContentScanning(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISensitiveContentCatalog, SecretContentCatalog>();
        services.AddSingleton<ISensitiveContentScanner, SecretContentScanner>();

        return services;
    }

    /// <summary>Registers the detector of personal data in mail text, which reaches an analyzer deployed beside this service.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Called only where the <c>Pii</c> switch is on, which is what makes an opt-in nobody took cost nothing: with it off
    /// no client is registered, no analyzer address is read, and none of the three descriptors below exists. The composed
    /// <see cref="PersonalDataAnalyzerProfile" /> is the host's to register, because where the analyzer is comes from
    /// configuration this project does not bind.
    /// </para>
    /// <para>
    /// The probe is registered beside the scanner rather than always, for the reason the catalog is: startup refuses a
    /// switch that is on with nothing behind it, and either one present without the other would turn that refusal into a
    /// scanner that runs and finds nothing.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddPersonalDataContentScanning(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISensitiveContentCatalog, PersonalDataContentCatalog>();
        services.AddSingleton<ISensitiveContentScanner, PresidioContentScanner>();
        services.AddSingleton<IPersonalDataAnalyzerProbe, PresidioAnalyzerProbe>();
        AddPersonalDataAnalyzerClient(services);

        return services;
    }

    /// <summary>Registers the transport a mailbox token request is sent over.</summary>
    /// <remarks>
    /// <para>
    /// The bounds are the ones the inbound metadata backchannel applies, for the same reason: an authorization server is
    /// a machine this process does not own, it is reached inside an authentication path, and one that has been replaced
    /// or misconfigured must not be able to answer with an unbounded body, hold the request open, or send it somewhere
    /// the configuration never named. No base address is set, because the token endpoint is a per-account setting the
    /// source resolves per request rather than an address this registration could know.
    /// </para>
    /// <para>
    /// Nothing here bounds the connection lifetime, and that is what makes the client's own lifetime part of the
    /// contract rather than an implementation detail of its caller. The factory retires a handler chain on its own
    /// schedule and hands the replacement to the *next* client it is asked for, so an address the authorization server
    /// has moved is picked up only by a caller that asks for a client per operation.
    /// <see cref="MailOAuthAccessTokenSource" /> does, which is why this registration needs no connection lifetime of
    /// its own; a caller that held one across a synchronization run would keep the address it resolved when the run
    /// began, and would have to set the lifetime instead.
    /// </para>
    /// </remarks>
    private static void AddMailOAuthTokenClient(IServiceCollection services)
    {
        var client = services.AddHttpClient(MailOAuthAccessTokenSource.TransportName)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { AllowAutoRedirect = false })
            .AddHttpMessageHandler(static () =>
                new BoundedMetadataHttpMessageHandler(MailOAuthTokenResponseSizeLimitInBytes))
            .ConfigureHttpClient(static client =>

                // Deliberately tighter than the mailbox session establishment timeout that encloses it, so a hung
                // authorization server surfaces as itself rather than as a mailbox timeout.
                client.Timeout = TimeSpan.FromSeconds(15));

        // This call is one of the two places the single-layer rule is enforced for HTTP, so it is one of the two places it
        // can be got wrong; AddPersonalDataAnalyzerClient below is the other, and the more delicate one, because it adds a
        // handler back rather than leaving none, so its own removal has to reach one handler and spare the other.
        // MailOAuthAccessTokenSource already runs the exchange under the MailAuthorizationServerInvocation
        // pipeline, and the host's service defaults add the standard resilience handler
        // to every client the factory builds; leaving both would multiply three attempts by three into nine token requests
        // against an authorization server that is already refusing. Removal is registered rather than the handler being
        // withheld, because the defaults apply to a name this project never sees.
        //
        // It removes what is registered before it, so it depends on AddServiceDefaults having run first. Host's
        // composition root does, and MailOAuthTokenTransportTests fails if that ever stops being true.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental, and is how the standard handler is opted out of.
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    }

    /// <summary>Registers the transport the personal-data analyzer is reached over.</summary>
    /// <remarks>
    /// <para>
    /// The analyzer's address is a base address here, unlike the provider clients, because a deployment reaches one
    /// analyzer rather than an endpoint chosen per call. Redirects are refused because a redirect would carry the mail
    /// content in the body to whatever host the answer named, which is the boundary this feature exists to keep the
    /// content inside.
    /// </para>
    /// <para>
    /// It carries a standard resilience handler, which is the whole of this call's retry story: an analyze request
    /// establishes what a text carries and changes nothing, so repeating one is safe, and the call runs under no
    /// <c>OutboundDependency</c> pipeline for the handler to nest inside. The one the host's service defaults add is
    /// replaced rather than inherited, because that one's bounds are fixed while this deployment's are configured.
    /// </para>
    /// <para>
    /// <b>The handler owns the transport's timeout, so <see cref="HttpClient.Timeout" /> is left disabled.</b> That is the
    /// handler's own arrangement — it sets the property to <see cref="Timeout.InfiniteTimeSpan" /> so its timeout
    /// strategies are what bound a call, rather than a property that would cut across the retries as a group. The bound an
    /// operator configured therefore reaches the transport as
    /// <c>HttpStandardResilienceOptions.TotalRequestTimeout</c> instead of as a client property, and a registration that
    /// set both would be deciding by action order which of the two won.
    /// </para>
    /// <para>
    /// Every bound is read from the plan rather than from a snapshot, because this runs on the root provider whenever the
    /// factory builds a client, and all of them take a restart to change.
    /// </para>
    /// </remarks>
    private static void AddPersonalDataAnalyzerClient(IServiceCollection services)
    {
        var client = services.AddHttpClient(PersonalDataAnalyzerProfile.TransportName)
            .ConfigurePrimaryHttpMessageHandler(static () => new SocketsHttpHandler { AllowAutoRedirect = false })
            .ConfigureHttpClient(static (provider, client) =>
            {
                var bounds = provider.GetRequiredService<SensitiveContentPlan>().Bounds;

                client.BaseAddress = provider.GetRequiredService<PersonalDataAnalyzerProfile>().Endpoint;
                client.MaxResponseContentBufferSize =
                    ((long)bounds.MaximumAnalyzedCharacters * AnalyzerResponseBytesPerAnalyzedCharacter)
                    + AnalyzerResponseEnvelopeBytes;
            });

        // The inherited handler's bounds are ten seconds per attempt and thirty in total, whatever the deployment
        // configured. SensitiveContent:ScanTimeout accepts up to two minutes, and the reason to raise it is an analyzer
        // that is slow over a large body — exactly the scan those fixed bounds would cut a long way inside the budget,
        // after re-sending the body twice on the way. So it is replaced with one derived from that budget.
        //
        // Removal is registered before the replacement because the build-time pass removes what the actions before it
        // added: the defaults' handler, which is registered against a client name this project never sees, and not the
        // one added after. PersonalDataAnalyzerTransportTests asserts the outcome rather than the arrangement: it composes
        // the service defaults around this call and fails if the analyzer client ever carries two handlers, which is what
        // deleting the removal below does. Measured against that composition, the surviving handler is this call's own and
        // the order the two registrations run in does not change it.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental, and is how the standard handler is opted out of.
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        client.AddStandardResilienceHandler().Configure(static (options, provider) =>
        {
            var bounds = provider.GetRequiredService<SensitiveContentPlan>().Bounds;
            var backstop = bounds.ScanTimeout + AnalyzerTransportTimeoutMargin;

            // Both are set above the configured budget rather than inside it, so a scan that runs long is cancelled by
            // that budget and never by a layer an operator did not set: the redactor then reports the scanner missing the
            // budget it spent rather than a transport failure that says nothing about a budget. What the handler still
            // buys is the retry — a refused connection or a fast rejection costs almost none of the budget, so the
            // attempt after it runs inside one.
            options.AttemptTimeout.Timeout = backstop;
            options.TotalRequestTimeout.Timeout = backstop;

            // The handler's own validator refuses a sampling window shorter than twice the attempt timeout, and the
            // standard thirty seconds is shorter than that for every budget past fifteen.
            if (options.CircuitBreaker.SamplingDuration < backstop * 2)
            {
                options.CircuitBreaker.SamplingDuration = backstop * 2;
            }
        });
    }
}
