// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Access.Credentials;
using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Contacts;
using MailFathom.Application.Contacts.Collection;
using MailFathom.Application.EmailContent.Attachments;
using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Release;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.BrowseSearch;
using MailFathom.Application.Emails.BrowseThread;
using MailFathom.Application.Emails.BrowseTimeline;
using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.DownloadAttachment;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Administration;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generations;
using MailFathom.Application.Emails.Embeddings.Limits;
using MailFathom.Application.Emails.Embeddings.Vectorization;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.ListEmails;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Folders;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.DeadLetters;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Scheduling;
using MailFathom.Application.Mail;
using MailFathom.Application.Mail.Delivery;
using MailFathom.Application.Mail.Delivery.Addressing;
using MailFathom.Application.Mail.Delivery.Authoring;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Delivery.Scheduling;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.Mail.Delivery.Submission;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Application.Mail.Maintenance;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Audit;
using MailFathom.Application.Mail.Mutations.Authoring;
using MailFathom.Application.Mail.Mutations.Convergence;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Application.Observability;
using MailFathom.Application.Persistence;
using MailFathom.Application.Resilience;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Retrieval.AskMail.Audit;
using MailFathom.Application.Rules.Evaluation;
using MailFathom.Application.Rules.History;
using MailFathom.Application.SensitiveContent;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.SensitiveContent.Detection;
using MailFathom.Application.SensitiveContent.Egress;
using MailFathom.Application.SensitiveContent.Redaction;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Actions;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Spam.History;
using MailFathom.Application.Spam.Runs;
using MailFathom.Application.Spam.Scanning;
using MailFathom.Application.Spam.Signals;
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Administration;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails.Authorship;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.DataEncryption;
using MailFathom.Infrastructure.Embeddings;
using MailFathom.Infrastructure.Folders;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.Attachments;
using MailFathom.Infrastructure.Mail.Dkim;
using MailFathom.Infrastructure.Mail.MailKit;
using MailFathom.Infrastructure.Mail.MailKit.Delivery;
using MailFathom.Infrastructure.Mail.MailKit.Writes;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.Mail.Mime.Composition;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Accounts;
using MailFathom.Infrastructure.Persistence.Answering;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Contacts;
using MailFathom.Infrastructure.Persistence.Delivery;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Emails.Threads;
using MailFathom.Infrastructure.Persistence.Embeddings;
using MailFathom.Infrastructure.Persistence.Jobs;
using MailFathom.Infrastructure.Persistence.Mutations;
using MailFathom.Infrastructure.Persistence.Owners;
using MailFathom.Infrastructure.Persistence.Rules;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Settings;
using MailFathom.Infrastructure.Persistence.Spam;
using MailFathom.Infrastructure.Persistence.Synchronization;
using MailFathom.Infrastructure.Resilience;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using MailFathom.Infrastructure.Security.ApiKeys;
using MailFathom.Infrastructure.Security.ClientAssertions;
using MailFathom.Infrastructure.Security.OAuth;
using MailFathom.Infrastructure.Security.Passwords;
using MailFathom.Infrastructure.SensitiveContent.PersonalData;
using MailFathom.Infrastructure.SensitiveContent.Secrets;
using MailFathom.Infrastructure.Spam;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

        // What a use case asks before it does the work it was reached for. The principal behind it comes from whatever
        // admitted that work, which only a composition root knows, so IAuthorizedPrincipalSource is registered there and
        // this line says only that the question is asked in the application layer rather than at whichever entrypoint
        // arrived first.
        services.AddScoped<AccessAuthorization>();

        // The record a refused caller is never told about. A singleton for the reason every other publisher here is
        // one: the counter it holds is a fact about the process, and a scoped instance would create an instrument per
        // request.
        services.AddSingleton<IAuthorizationRefusalTelemetry, AuthorizationRefusalTelemetry>();

        AddPersistenceAdapters(services, currentConnectionSettings, textSearchConfiguration);
        AddEmbeddingAdapters(services);
        AddStoredMailAdapters(services);
        AddMailRuleAdapters(services);
        AddJobQueueAdapters(services);
        AddSpamClassificationStores(services);
        AddReadSideStores(services);
        AddMailContentAdapters(services);
        AddSynchronization(services);
        AddMailboxReaders(services);
        AddSpamClassification(services);
        AddMailAnswering(services, answeringBudget);
        AddMailProtocolAdapters(services);
        AddMailboxMutationRecords(services);
        AddMailDelivery(services);
        AddMailboxMutations(services);
        AddContacts(services);

        return services;
    }

    /// <summary>Registers the PostgreSQL connection this process owns, the context over it, and the session every store commits through.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="currentConnectionSettings">Supplies where the PostgreSQL connection string and its password currently come from.</param>
    /// <param name="textSearchConfiguration">The validated PostgreSQL text search configuration the lexical index is built with.</param>
    private static void AddPersistenceAdapters(
        IServiceCollection services,
        Func<IServiceProvider, PostgresConnectionSettings> currentConnectionSettings,
        PostgresTextSearchConfiguration textSearchConfiguration)
    {
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
        // EnableRetryOnFailure is deliberately not configured, and the reason is what a retry can reach rather than
        // what a setting collides with. A dropped connection takes its transaction with it, so replaying a statement
        // meets a transaction the server has already discarded: the only repeatable unit is the whole unit of work,
        // and OptimisticConcurrencyRetryPolicy already replays that one from a fresh read. Turning the setting on
        // would also refuse the transaction a session opens when a write joins it — a transaction several writes
        // depend on, since a session issues set-based statements and a FOR UPDATE lock beside the changes it stages.
        // That is the single layer: nothing wraps a database command, so no call is covered twice.
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
        // Reads the persisted configuration layer once the process is running, which is what a reload asks. The
        // bootstrap read that composed the configuration happened before this container existed and built its own
        // data source for it; this registration is the same statement over the pool everything else uses.
        services.AddSingleton<IRootSettingsDocumentReader, RootSettingsDocumentReader>();
        // The other direction of travel over the same row, registered beside the read because it is the same one
        // statement over the same pool. Nothing resolves it unless a configuration write is composed above it.
        services.AddSingleton<IRootSettingsDocumentWriter, RootSettingsDocumentWriter>();
        // The counter every session reports its ending to is a singleton, because the instrument it holds belongs to the
        // process rather than to a scope: one per request would create the same instrument again on every call.
        services.AddSingleton<PersistenceCommitTelemetry>();
        // Both are singletons and both are registered whether or not the deployment named an object endpoint. What
        // decides whether they do anything is the object store, which is registered by AddObjectStorage alone: without
        // it the eraser is handed nothing to reach and every locator it could have been given would have been null,
        // because a database-backed payload has no key.
        services.AddSingleton<ContentObjectReclamationTelemetry>();
        services.AddSingleton<ReleasedContentObjectEraser>();
        // The database half of the sweep, registered with persistence rather than with the endpoint because that is
        // what it reads. Nothing resolves it where no endpoint is configured, since the sweep that asks it is what
        // AddObjectStorage registers.
        services.AddScoped<IContentObjectReferenceReader, ContentObjectReferenceReader>();
        services.AddScoped<IPersistenceSessionFactory, PersistenceSessionFactory>();
        services.AddScoped<ISynchronizationCheckpointStore, SynchronizationCheckpointStore>();
        // Registered here rather than beside the chunker it calls, because what it is is a table: it decides which
        // passage rows a message owns. Its `IEmailTextChunker` comes from the AI boundary, which this project may not
        // reference, so a composition root that registers persistence without the local derivations resolves nothing.
        services.AddScoped<EmailChunkWriter>();
        // The port a verdict that turned out to be junk discards a message's passages through. It carries the discarding
        // half alone, because nothing outside the arrival pipeline decides when passages are cut.
        services.AddScoped<IEmailChunkStore, EmailChunkStore>();
        // The port the pipeline's own cut reaches the writer through, and the pass that performs it. Cutting is a call
        // the account run makes after classification and the rules have finished with a message rather than something
        // the metadata write does on its way past, because both of those stages may still change what a message is.
        services.AddScoped<IStoredEmailChunkingStore, StoredEmailChunkingStore>();
        services.AddScoped<MailChunkingPass>();
    }

    /// <summary>Registers the tables a message's vectors live in, the ceilings a generation is spent against, and the operator acts on a profile.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddEmbeddingAdapters(IServiceCollection services)
    {
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
        services.AddScoped<IStoredEmailEmbeddingBackfillStore, StoredEmailEmbeddingBackfillStore>();
        services.AddScoped<IEmbeddingGenerationStore, EmbeddingGenerationStore>();
        // The two operator acts on a generation, registered here rather than beside the generation work because neither
        // resolves an `ITextEmbeddingGenerator`: they move a profile row, so they build in every container. What decides
        // that there is anything to activate is the declaration the caller reads, not this.
        services.AddScoped<EmbeddingProfileActivation>();
        services.AddScoped<EmbeddingReindexCancellation>();
        // What the administrative surface asks of those two: the counting in front of an activation, and the one read
        // that says whether semantic search is working. Registered unconditionally like the acts they wrap, because an
        // instance that declared no provider is exactly the one whose operator most needs to be told so.
        services.AddScoped<IEmbeddingWorkloadReader, EmbeddingWorkloadReader>();
        services.AddScoped<CountedEmbeddingActivation>();
        services.AddScoped<EmbeddingStatusReader>();
    }

    /// <summary>Registers the tables stored mail itself lives in, and the two maintenance walks an operator drives over them.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddStoredMailAdapters(IServiceCollection services)
    {
        services.AddScoped<IEmailMetadataRepository, StoredEmailMetadataRepository>();
        // Placing a message in its conversation is part of both write paths — the arrival that commits it and the
        // re-derivation that re-reads it — so it is registered once rather than composed into either. It holds no
        // context of its own and writes through the caller's, which is what makes the placement part of the caller's
        // transaction rather than a second commit beside it.
        services.AddScoped<IEmailThreadStore, EmailThreadStore>();
        services.AddScoped<EmailThreadAssembly>();
        services.AddScoped<IDatabaseSchemaInspector, EfCoreDatabaseSchemaInspector>();
        // Who this deployment holds owner records for. Read once while the host comes up rather than per request, which
        // is why nothing scoped to a request depends on it and why it is registered beside the schema inspector the same
        // startup step already resolves.
        services.AddScoped<IMailOwnerDirectory, PersistedMailOwnerDirectory>();
        // The envelope a declared owner is given, beside the read that establishes who is already there. Scoped for the
        // same reason and used from the same startup step: a declaration reaches this exactly once per start, and never
        // while a request is being served.
        services.AddScoped<IMailOwnerProvisioning, PersistedMailOwnerProvisioning>();
        // The credentials an owner is admitted by, of every method. Scoped because it reads and writes through the
        // request's own context, and separate from the directory above because that answers which owners exist and this
        // answers what one of them may present.
        services.AddScoped<IOwnerCredentialStore, PersistedOwnerCredentials>();
        // What a password becomes when it is stored and what a presented one is judged against. A singleton because it
        // holds no state at all: every parameter a verification needs travels inside the record it is verifying.
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        // The bound on how often one source and one username may have a password checked. A singleton because the
        // buckets are what it is: a per-request instance would count each attempt in a bucket of its own and bound
        // nothing.
        services.AddSingleton<PasswordAttemptLimiter>();
        // The record every unresolved username is compared against. A singleton because deriving it is what makes an
        // unknown username cost what a known one costs, and doing that per request would spend the same derivation on
        // every attempt before anything had been checked.
        services.AddSingleton<DecoyPasswordHash>();
        // Judging a presented username and password. Scoped rather than singleton, unlike the API key authenticator,
        // because it reads the credential store through the request's own context; everything it holds that is
        // expensive to build is one of the singletons above.
        services.AddScoped<OwnerPasswordAuthenticator>();
        // What draws a key an owner's client presents and reduces a presented one to the digest a row is resolved by. A
        // singleton because it holds nothing: the entropy is drawn per call and the digest is a pure function of what
        // was presented.
        services.AddSingleton<IOwnerApiKeyMinter, OwnerApiKeyMinter>();
        // Reading a client's public key into what a row stores and what resolves it. A singleton for the same reason.
        services.AddSingleton<IClientPublicKeyReader, ClientPublicKeyReader>();
        // Judging a presented key, and resolving a validated subject to the owner it stands for. Both scoped, because
        // both read the credential store through the request's own context.
        services.AddScoped<OwnerApiKeyAuthenticator>();
        services.AddScoped<OwnerOAuthSubjectResolver>();
        // Where a change to who can reach an owner's mail is written down.
        services.AddScoped<IOwnerCredentialAuditor, LoggedOwnerCredentialAuditor>();
        // What an administrator does to those credentials, registered beside them because the whole of what it
        // composes is the four services above.
        services.AddScoped<OwnerCredentialAdministration>();
        // One owner's own record, read by key and bounded in the statement rather than in the process. A singleton
        // over the pool for the reason the persisted configuration layer's reader is one — the command holds no state
        // between calls — and separate from the directory above because that answers for the deployment and this for
        // a person.
        services.AddSingleton<IOwnerSettingsDocumentReader, PersistedOwnerSettingsDocumentReader>();
        // Whose mail a background unit of work is acting on. A worker acts for nobody, so the ceilings it is bounded by
        // reach an owner through this rather than through a principal, and it is scoped because both reads are ordinary
        // queries on the caller's context.
        services.AddScoped<IMailOwnership, PersistedMailOwnership>();
        // Composed by hand for one parameter: the object store is registered only when the deployment selected the
        // object backend, so it is asked for rather than required, and its absence is what selects the database.
        services.AddScoped<IEmailContentStore>(provider => new EmailContentStore(
            provider.GetRequiredService<MailFathomDbContext>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<StoredEmailContentTelemetry>(),
            provider.GetService<IEmailContentObjectStore>()));
        services.AddScoped<IStoredEmailContentInventory, StoredEmailContentInventory>();
        services.AddScoped<IObjectBackedContentInventory, ObjectBackedContentInventory>();
        // What one owner's stored content holds. Beside the inventory rather than part of it, because the inventory is
        // read-only over the mail graph while this keeps a figure of its own, moved by whichever adapter owns the
        // payloads.
        services.AddScoped<IOwnerStoredContentLedger, OwnerStoredContentLedger>();
        services.AddScoped<IStoredEmailExtractionBackfillStore, StoredEmailExtractionBackfillStore>();
        // What the move of already-stored content reads and rewrites: the four content tables as one walk, and the one
        // row that says what an operator asked for. Registered whatever the selected backend is, because reading how
        // much content the database still holds is an ordinary question of a deployment that moves none of it.
        services.AddScoped<IStoredContentMoveStore, StoredContentMoveStore>();
        services.AddScoped<IStoredContentMoveRunStore, StoredContentMoveRunStore>();
        // A singleton for the reason every other publisher of instruments is: the instruments belong to the instance
        // rather than to a scope, and a registration per scope publishes a second set of series nothing collects.
        services.AddSingleton<IStoredContentMoveTelemetry, StoredContentMoveTelemetry>();
        // The two halves an operator reaches the move through. Composed by hand for one parameter, exactly as the content
        // store is: the object backend is registered only where the deployment selected it, so it is asked for rather
        // than required and its absence is what says a move is not available here.
        services.AddScoped(provider => new StoredContentMoveControl(
            provider.GetRequiredService<IStoredContentMoveRunStore>(),
            provider.GetRequiredService<OptimisticConcurrencyRetryPolicy>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<AccessAuthorization>(),
            provider.GetService<IEmailContentObjectBackend>()));
        services.AddScoped<StoredContentMoveReader>();
        // The other half of the move: what the copy left duplicated in the database, and the one act that ends it.
        // Registered whatever the selected backend is, because a deployment that has switched back to the database still
        // holds the copies its move left and is exactly the one whose operator has to be able to free them.
        services.AddScoped<IRetainedContentReleaseStore, RetainedContentReleaseStore>();
        services.AddSingleton<IRetainedContentReleaseTelemetry, RetainedContentReleaseTelemetry>();
        // What the two maintenance commands read and write. Both are ordinary scoped stores over stored mail: the
        // counter is what puts a figure in front of an operator before a rewind is agreed to, and the walk is the pass
        // that re-reads what a rewind would otherwise fetch again.
        services.AddScoped<IStoredMailCounter, StoredMailCounter>();
        services.AddScoped<IStoredMailRederivationStore, StoredMailRederivationStore>();
        // The run beside the walk: the cursor says where the passes got to and this says what the operator asked for
        // and what has come of it, which is the question a terminal that no longer drives the walk has to be able to ask.
        services.AddScoped<IStoredMailRederivationRunStore, StoredMailRederivationRunStore>();
        // A singleton because the instruments are the instance's, not a scope's: a per-request meter registration
        // publishes a second set of series that nothing collects.
        services.AddSingleton<IStoredMailRederivationTelemetry, StoredMailRederivationTelemetry>();
        // A singleton for the reason the embedding backfill's is: the backlog it publishes is one figure about the whole
        // instance, and a gauge answering per scope would report whichever scope last ran a pass.
        services.AddSingleton<MailExtractionBackfillTelemetry>();
        services.AddScoped<IStoredEmailReconciliationStore, StoredEmailReconciliationStore>();
    }

    /// <summary>Registers what a rule evaluation writes down and what the administrative surface reads back out of it.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddMailRuleAdapters(IServiceCollection services)
    {
        services.AddScoped<IMailRuleEvaluationStore, MailRuleEvaluationStore>();
        services.AddScoped<IMailRuleEvaluationRunStore, MailRuleEvaluationRunStore>();
        // What the administrative surface asks of the two rule stores. Each is a use case rather than a second port,
        // because the grant a reader has to hold is decided where the reading happens rather than at the route.
        services.AddScoped<MailRuleEvaluationRunReader>();
        services.AddSingleton<MailRuleHistoryTelemetry>();
        services.AddScoped<IMailRuleExecutionStore, MailRuleExecutionStore>();
        services.AddScoped<MailRuleHistory>();
    }

    /// <summary>Registers the queue every background pass claims work from, its dead letters, and its recurring schedule.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddJobQueueAdapters(IServiceCollection services)
    {
        // Scoped like every other store, and deliberately not registered beside a worker: nothing here runs a job. It
        // takes no persistence session either, so a caller cannot enlist an enqueue in the transaction that stored the
        // message the work is about.
        services.AddScoped<IJobStore, JobStore>();
        // Beside it because they read and write the same table, and apart from it because the callers are: the worker
        // acts under a lease it holds, and these two answer an operator who holds none.
        services.AddScoped<IJobQueueDepthReader, JobQueueDepthReader>();
        services.AddScoped<IDeadLetteredJobStore, DeadLetteredJobStore>();
        services.AddScoped<DeadLetteredJobs>();
        // What each recurring dispatch has already done. Scoped and sessionless like the queue itself, because a
        // schedule is advanced against work that is already enqueued.
        services.AddScoped<IJobScheduleStore, JobScheduleStore>();
        // A singleton with the instruments on it, for the reason every other telemetry type here is one: an instrument
        // created per scope would publish a second time series for the same measurement.
        services.AddSingleton<JobQueueTelemetry>();
    }

    /// <summary>Registers what a classification verdict is written to and read back from, whatever this deployment decides to classify.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddSpamClassificationStores(IServiceCollection services)
    {
        // Registered whether or not classification is switched on, for the reason every other store here is: what a
        // deployment decides is whether anything calls it, and a port resolvable only under one configuration is a
        // composition that fails at the moment somebody changes their mind rather than at startup.
        services.AddScoped<IEmailSpamClassificationStore, EmailSpamClassificationStore>();
        services.AddScoped<IClassifiableEmailReader, ClassifiableEmailReader>();
        services.AddScoped<ISpamActionOccurrenceReader, SpamActionOccurrenceReader>();
        services.AddScoped<ISpamClassificationRunStore, SpamClassificationRunStore>();
        services.AddScoped<ISpamClassificationHistoryReader, SpamClassificationHistoryReader>();
        services.AddScoped<SpamClassificationRunReader>();
        services.AddScoped<SpamClassificationHistory>();
    }

    /// <summary>Registers the read side over stored mail, which takes no persistence session and joins no transaction.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddReadSideStores(IServiceCollection services)
    {
        // The read side takes no persistence session and joins no transaction, so its ports are registered beside the
        // write repositories rather than through one of them.
        services.AddScoped<IStoredEmailTimelineReader, StoredEmailTimelineReader>();
        services.AddScoped<IStoredEmailSummaryReader, StoredEmailSummaryReader>();
        services.AddScoped<IStoredEmailPreviewReader, StoredEmailPreviewReader>();
        services.AddScoped<IEmailThreadReader, StoredEmailThreadReader>();
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
        // Beside the freshness reader because both narrow the same folder rows to the same scope, and apart from it
        // because this one counts mail: it is asked for by the read that draws a folder tree and by nothing that
        // attaches freshness to a listing.
        services.AddScoped<IStoredMailFolderReader, StoredMailFolderReader>();
        // A singleton because what it holds is one account of what this process's synchronization is doing: a scoped
        // ledger would be written by the workers and read, empty, by every administrative request. Registered whether or
        // not this deployment synchronizes, because a deployment that switched it off is exactly the one whose operator
        // is asking why no mail arrives, and the status surface answers that from the switch rather than from silence.
        services.AddSingleton<MailSynchronizationRunLedger>();
        services.AddScoped<IMailFolderSynchronizationProgressReader, MailFolderSynchronizationProgressReader>();
        services.AddScoped<MailSynchronizationStatusReader>();
        // The one write a read path performs. It joins no session for the reason its port states, so it is registered
        // beside the readers rather than with the repositories that take one.
        services.AddScoped<IEmailContentRepairRequestStore, EmailContentRepairRequestStore>();
        // Registered here rather than beside the OAuth client it serves, because what it is is a table: the token
        // source asks for a credential and this is what knows the credential lives in PostgreSQL, sealed.
        services.AddScoped<IMailboxRefreshTokenStore, MailboxRefreshTokenStore>();
        services.AddScoped<MailboxRefreshTokenRecorder>();
    }

    /// <summary>Registers the MimeKit adapters a stored message is parsed, rendered, and served out of, and the signed links an attachment is fetched through.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddMailContentAdapters(IServiceCollection services)
    {
        // MimeKit arrives with MailKit, so message parsing needs no dependency of its own; the adapter keeps its types
        // out of Application the same way the IMAP adapter keeps MailKit's out.
        // Wrapped where a scanner is switched on, because this port is where every derived copy of a body begins: the
        // search document, the passages cut from it, and the vectors built from those all descend from one extraction,
        // and both writers of one reach it through here. Decided from the guard rather than from configuration this
        // project does not bind, and left unwrapped where nothing is scanned so a message is read exactly as it was.
        // The sender verdict is decided at the same seam and for the same reason, and is wrapped unconditionally: a
        // deployment that recognizes nobody still records that it recognized nobody, which is what a reader is shown.
        // It sits directly over the parse because it reads what the parse established and touches nothing else, so the
        // redaction above it is unaffected either way.
        // The authorship reading sits above the verdict and still below any redaction, so that it judges the words the
        // message carried rather than the words a scanner rewrote in it. It is wrapped unconditionally as well: a
        // deployment that turned the reading off resolves the profile that reads nothing, which records the same
        // not-assessed state as a message with no readable body.
        // The DNS client and the record cache are singletons because what they hold describes the process rather than a
        // work unit: a cache per scope would ask the same signing domain for the same selector once per folder run, and
        // a client per scope would rebuild the resolver configuration each time. The client names no nameserver of its
        // own, so a deployment that routes DNS through its own resolver is followed rather than bypassed.
        // The resolver is registered only where nothing supplied one, so the one place this system reaches the network
        // to judge a sender can be replaced without the parse changing — which is what a test that must not resolve
        // anything relies on.
        services.AddSingleton(provider => new DkimPublicKeyRecordCache(provider.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IDkimPublicKeyRecordResolver>(provider => new DnsDkimPublicKeyRecordResolver(
            DnsDkimPublicKeyRecordResolver.CreateLookupClient(),
            provider.GetRequiredService<DkimPublicKeyRecordCache>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped(provider =>
        {
            // The local verification is handed to the parse only where this deployment verifies for itself, so a
            // deployment that switched it off makes no lookup rather than making one whose result is discarded. Where
            // it is on it is still a fallback: the parse asks it only for a message no trusted header was found for.
            var extractionOptions = provider.GetRequiredService<EmailMimeExtractionOptions>();
            var localSenderVerifier = extractionOptions.VerifyDkimLocally
                ? new DkimLocalSenderVerifier(
                    provider.GetRequiredService<IDkimPublicKeyRecordResolver>(),
                    provider.GetRequiredService<TimeProvider>())
                : null;

            IEmailMimeReader reader = new MachineAuthorshipEvaluatingEmailMimeReader(
                new SenderTrustEvaluatingEmailMimeReader(
                    new MimeKitEmailMimeReader(
                        extractionOptions,
                        provider.GetRequiredService<ITrustedAuthenticationAuthorityReader>(),
                        localSenderVerifier),
                    provider.GetRequiredService<ISenderTrustPolicyReader>()),
                provider.GetRequiredService<MachineAuthorshipProfile>());

            return provider.GetRequiredService<SensitiveContentDerivationGuard>() is { IsActive: true } guard
                ? new RedactingEmailMimeReader(reader, guard)
                : reader;
        });
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
    }

    /// <summary>Registers folder resolution and the passes that bring a mailbox and its local copy back into agreement.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddSynchronization(IServiceCollection services)
    {
        services.AddScoped<IMailFolderResolutionStore, MailFolderResolutionStore>();
        services.AddScoped<IStoredMailFolderMirrorStore, StoredMailFolderMirrorStore>();
        services.AddScoped<IMailFolderMappingChangeAuditor, LoggedMailFolderMappingChangeAuditor>();
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
        services.AddScoped<MailFolderResolver>();
        services.AddScoped<MailFolderReferenceResolver>();
        services.AddScoped<UnmirroredMailFolderEraser>();
        services.AddScoped<MailSynchronizationRewind>();
        services.AddScoped<StoredMailRederivation>();
        // The two halves an operator reaches the re-derivation through: asking for a run, which writes it down and
        // enqueues the work, and reading where the run has got to. Both are use cases rather than second ports, because
        // the grant each asks for is decided where the work happens rather than at the route.
        services.AddScoped<StoredMailRederivationRequests>();
        services.AddScoped<StoredMailRederivationRunReader>();
        services.AddScoped<MailboxSynchronizer>();
        services.AddScoped<MailboxReconciler>();
        services.AddScoped<StoredEmailExtractionBackfill>();
        // The caller-scoped half of the account catalog, registered beside the resolution that is its first reader. The
        // deployment-wide half is registered by the composition root, because configuration is what declares the
        // accounts; this half is application reasoning over that answer, so it is composed here and the two are
        // separate registrations rather than one service resolved two ways.
        services.AddScoped<ICallerMailAccountCatalog, OwnedMailAccountCatalog>();
        services.AddScoped<MailboxScopeResolver>();
    }

    /// <summary>Registers what a caller-facing read publishes, the two guards every derivation and every egress passes through, and the readers a protocol surface reaches.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddMailboxReaders(IServiceCollection services)
    {
        // Singletons for the reason every other publisher here is one: a span source is a fact about the process, and a
        // scoped instance would build one per request for no gain.
        services.AddSingleton<IMailboxReadTelemetry, MailboxReadTelemetry>();
        services.AddSingleton<StoredEmailContentTelemetry>();
        services.AddScoped<MailAccountDirectoryReader>();
        services.AddScoped<MailAccountFreshnessReader>();
        services.AddScoped<MailFolderDirectoryReader>();
        // The guard every egress point calls, registered for every deployment rather than only where a scanner is
        // switched on. What is conditional is the redaction behind it: with both switches off the provider hands over
        // no redactor, no detector is constructed, and every call returns its argument. Registering it conditionally
        // instead would put a null check and a second code path into each consumer, which is how two of them end up
        // disagreeing about what an unguarded egress looks like.
        services.AddSingleton<ISensitiveContentEgressTelemetry, SensitiveContentEgressTelemetry>();
        // A singleton for the reason every other instrument here is one: what it counts is a fact about the process, and
        // a scoped instance would create a meter per request.
        services.AddSingleton<IDerivedWorkGateTelemetry, DerivedWorkGateTelemetry>();
        services.AddSingleton(provider => new SensitiveContentEgressGuard(
            provider.GetService<SensitiveContentRedactor>(),
            provider.GetRequiredService<ISensitiveContentEgressTelemetry>(),
            provider.GetRequiredService<TimeProvider>()));
        // Its refusing counterpart, registered on the same terms and inert in the same two ways: no redactor where both
        // switches are off, and a policy that stops nothing where the deployment named no scanner to stop an act with.
        // A consumer therefore calls it unconditionally and never asks whether screening exists.
        services.AddSingleton(provider => new SensitiveContentEgressScreen(
            provider.GetService<SensitiveContentRedactor>(),
            provider.GetService<SensitiveContentScreeningPolicy>()
                ?? SensitiveContentScreeningPolicy.ScreeningNothing(),
            provider.GetRequiredService<ISensitiveContentEgressTelemetry>(),
            provider.GetRequiredService<TimeProvider>()));

        // Its counterpart on the way in, registered on the same terms and for the same reasons. The stamp is composed
        // here rather than held by the redactor, because it is the derived store's question rather than the redaction's:
        // what a row has to record is which detectors, revisions, categories, and suppressions produced its text, and
        // that answer is fixed for the life of a process the moment the plan and the scanners are resolved.
        services.AddSingleton<ISensitiveContentDerivationTelemetry, SensitiveContentDerivationTelemetry>();
        services.AddSingleton(provider => new SensitiveContentDerivationGuard(
            provider.GetService<SensitiveContentRedactor>(),
            provider.GetService<SensitiveContentPlan>() is { } plan
                ? SensitiveContentDerivationStamp.Compute(plan, provider.GetServices<ISensitiveContentScanner>())
                : null,
            provider.GetRequiredService<ISensitiveContentDerivationTelemetry>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<MailboxTimelineReader>();
        services.AddScoped<MailTimelineBrowser>();
        services.AddScoped<MailThreadBrowser>();
        services.AddScoped<EmailContentReader>();
        services.AddScoped<EmailAttachmentDownloadReader>();
        services.AddScoped<MailboxSearchReader>();
        services.AddScoped<MailSearchBrowser>();
    }

    /// <summary>Registers the classifier itself, the ordering that puts it in front of every other derivation, and what a verdict causes.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddSpamClassification(IServiceCollection services)
    {
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
            provider.GetRequiredService<IEmailChunkStore>(),
            provider.GetRequiredService<IDerivedWorkGateTelemetry>(),
            provider.GetRequiredService<OptimisticConcurrencyRetryPolicy>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ISpamScanner>()));
        // The ordering that puts classification in front of chunking, embedding, and rule evaluation. Registered for
        // every deployment because the paths that obey it are unconditional: with classification off the gate admits
        // everything and every one of them behaves exactly as it did before the gate existed.
        services.AddScoped<DerivedWorkGate>();
        // The arrival trigger, scoped beside the synchronization run that reaches it and beside the job store it writes
        // through. It is registered whatever the switches say, for the reason the gate is: with classification off it
        // reads one property per stored message and enqueues nothing.
        services.AddScoped<SpamClassificationArrivals>();
        // Registered beside the classifier and independent of it: what a verdict causes is a decision of its own, and the
        // classifier resolves nothing from here, which is what keeps a deployment that records verdicts and touches
        // nothing genuinely unable to reach a mailbox through classification.
        services.AddScoped<SpamActionRecorder>();
    }

    /// <summary>Registers the retrieval one question about the mailbox is served from, the bounds it runs under, and the trail it leaves behind.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="answeringBudget">The validated ceilings one question about the mailbox is subject to.</param>
    private static void AddMailAnswering(
        IServiceCollection services,
        MailAnsweringBudget answeringBudget)
    {
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
        services.AddScoped<MailAnsweringAuditTrailReader>();
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
    }

    /// <summary>Registers the MailKit sessions this process reaches a mail server over, and the token source that authenticates them.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddMailProtocolAdapters(IServiceCollection services)
    {
        // The cache outlives every scope because a token is valid for whichever work unit next needs the account,
        // while the source that fills it is scoped to the configuration snapshot it resolves settings from.
        services.AddSingleton<MailAccessTokenCache>();

        AddMailOAuthTokenClient(services);

        services.AddScoped<IMailAccessTokenSource>(provider => new MailOAuthAccessTokenSource(
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<IMailOAuthSettingsProvider>(),
            provider.GetRequiredService<IMailboxRefreshTokenStore>(),
            provider.GetRequiredService<IDeploymentMailAccountCatalog>(),
            provider.GetRequiredService<MailAccessTokenCache>(),
            provider.GetRequiredService<OutboundOperationExecutor>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<MailOAuthAccessTokenSource>>()));
        services.AddScoped<IMailboxSessionFactory>(provider => new MailKitImapMailboxSessionFactory(
            MailKitImapClientFactory.CreateWithoutProtocolLogging,
            provider.GetRequiredService<IImapAccountSettingsProvider>(),
            provider.GetRequiredService<IMailAccessTokenSource>(),
            provider.GetRequiredService<OutboundOperationExecutor>(),
            provider.GetRequiredService<ITransientFailureClassifier>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IMailboxNotificationSessionFactory>(provider => new MailKitImapNotificationSessionFactory(
            MailKitImapClientFactory.CreateWithoutProtocolLogging,
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
            MailKitImapClientFactory.CreateWithoutProtocolLogging,
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<OutboundOperationExecutor>(),
            provider.GetRequiredService<ITransientFailureClassifier>(),
            provider.GetRequiredService<MailboxWriteSessionOptions>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<MailboxWriteConnectionPool>>()));
        services.AddScoped<IMailboxWriteSessionFactory>(provider => new MailKitImapWriteSessionFactory(
            provider.GetRequiredService<MailboxWriteConnectionPool>(),
            provider.GetRequiredService<MailboxMutationTelemetry>()));
        // Registered beside the write session and reaching neither it nor the pool, because submission is a second
        // protocol against a second server. Nothing is pooled: a delivery opens its own connection and closes it, so
        // there is no shared state a scope could hold, and the factory is scoped like every other mail adapter.
        services.AddScoped<IMailDeliverySessionFactory>(provider => new MailKitSmtpDeliverySessionFactory(
            MailKitSmtpClientFactory.CreateWithoutProtocolLogging,
            SubmissionSocketConnector.ConnectAsync,
            provider.GetRequiredService<ISmtpAccountSettingsProvider>(),
            provider.GetRequiredService<IMailAccessTokenSource>(),
            provider.GetRequiredService<OutboundOperationExecutor>(),
            provider.GetRequiredService<ITransientFailureClassifier>(),
            MailDeliveryTimeouts.Default,
            provider.GetRequiredService<MailDeliveryTelemetry>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<MailKitSmtpConnection>>()));
        // Registered beside the write session and against the same pool, because a creation is issued over the account's
        // one write connection. It is a separate port rather than a method on the session above, so a component holding
        // one of the two can never reach the other.
        services.AddScoped<IRemoteFolderCreator>(provider => new MailKitRemoteFolderCreator(
            provider.GetRequiredService<MailboxWriteConnectionPool>(),
            provider.GetRequiredService<ILogger<MailKitRemoteFolderCreator>>()));
    }

    /// <summary>Registers what a mutation is written down as before the session that acts on it is opened.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddMailboxMutationRecords(IServiceCollection services)
    {
        // The record is written before the session that acts on it is opened, so the store is registered beside the
        // other repositories that take a persistence session rather than with the mail adapters above.
        services.AddScoped<IMailboxMutationRecordStore, MailboxMutationRecordStore>();
        // Beside the record store because it answers the one question a caller naming an email by its local identifier
        // cannot answer for itself: where that email currently is. It reads the local copy and reaches no mail server,
        // so a protocol request resolves nothing over the network before it has been decided whether it may write.
        services.AddScoped<IAuthoredMailboxTargetReader, AuthoredMailboxTargetReader>();
        services.AddScoped<MailFlagChangeRecorder>();
    }

    /// <summary>Registers the outbox, the drafts beside it, the bounds a send is judged against, and every use case that authors one.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddMailDelivery(IServiceCollection services)
    {
        // The outgoing record is written before the delivery session above is opened, and for a stronger reason than the
        // mutation record is: a send is the one act here that cannot be undone once it leaves. The outbox in front of it
        // is what makes the record and the message it points at one write.
        services.AddSingleton<MailDeliveryTelemetry>();
        services.AddScoped<IOutgoingEmailStore, OutgoingEmailStore>();
        // The bounds the outbox asks before it writes anything down. The counts they are answered from are read through
        // the scoped context like every other read, and the governor is scoped with them so one work unit judges a send
        // against one reload of the account list it was scheduled from.
        services.AddScoped<IOutgoingMailUsageReader, OutgoingMailUsageReader>();
        services.AddScoped<OutgoingMailGovernor>();
        // What the message says, asked beside what the bounds allow and by the same two callers. Both are singletons
        // because neither holds anything: the reader is handed bytes and parses them, and the screening composes it
        // with the process-wide screen over the process-wide policy. A scope would resolve the same two objects it
        // did last time, so scoping it would allocate per work unit and buy nothing.
        services.AddSingleton<IOutgoingMailTextReader, MimeKitOutgoingMailTextReader>();
        services.AddSingleton<OutgoingMailScreening>();
        services.AddScoped<MailOutbox>();
        // The operator's view of the same records, registered beside the outbox rather than with the administrative
        // endpoint that serves it today: the grant is asked in the use case, so a second entrypoint reaching it is
        // governed without anything here changing.
        services.AddScoped<IOutboxOperationStore, OutboxOperationStore>();
        services.AddScoped<OutboxOperations>();
        // A message an owner asked to have sent again is a declaration beside the outbox rather than a queue of its
        // own: it produces an ordinary record on every occasion, through the outbox above, and the occasions come from
        // the job model's recurring dispatch.
        services.AddScoped<IRecurringSendStore, RecurringSendStore>();
        // The bounds that exist only where a caller does, asked by the submission use cases rather than by the outbox
        // beneath them: what one client may be talked into is a question work no caller requested does not have. The
        // vouching and the governor are scoped because both read through the scoped context and the caller's own
        // principal; the ledger is a singleton because a period belongs to the process rather than to a work unit, and
        // a count per scope would be a count nobody carried to the next call.
        services.AddScoped<RecipientVouching>();
        services.AddSingleton<AuthoredSendUsageLedger>();
        services.AddScoped<IAuthoredSendAuditor, LoggedAuthoredSendAuditor>();
        services.AddScoped<AuthoredSendGovernor>();
        // The attempt and the pass over it are scoped like every other work unit, because each opens a submission
        // session and commits through the caller's persistence scope. The signal they answer is not: it carries accounts
        // between a scope that wrote a record and the loop that delivers it, so it belongs to the process.
        services.AddScoped<MailOutboxDelivery>();
        // The copies of an outgoing message this deployment puts into its own folders. They are registered beside the
        // pass that drives them rather than with the mail adapters, because each one is a write session opened per
        // append through the same pool every other mutation goes through.
        services.AddScoped<IOutgoingMailFilingStore, OutgoingMailFilingStore>();
        // The one append every filed copy goes through, whatever kind of message it is a copy of. It is registered once
        // and shared by the filers rather than owned by either, because the order of its two writes is what keeps a
        // copy one copy, and an ordering that held on one filing path and lapsed on another would be no ordering.
        services.AddScoped<MailboxCopyAppender>();
        services.AddScoped<OutgoingMailFiler>();
        services.AddScoped<OutgoingMailFilingPass>();
        // The drafts a mailbox holds, registered beside the filing they reuse rather than with it: a draft is appended
        // through the same write session and into a folder named the same way, and everything that separates the two is
        // that a draft is replaced whenever its author edits it. Scoped for the reason every other work unit is.
        services.AddScoped<IMailDraftStore, MailDraftStore>();
        services.AddScoped<MailDraftFiler>();
        services.AddScoped<MailDraftBook>();
        services.AddScoped<MailDraftPass>();
        services.AddScoped<MailOutboxPass>();
        // Scoped beside the outbox and beside the sending identities it reads, which are the account snapshot's and
        // therefore belong to one work unit. The composer itself holds nothing across a call.
        services.AddScoped<IAuthoredEmailComposer>(provider => new MimeKitAuthoredEmailComposer(
            provider.GetRequiredService<IOutgoingSenderIdentityReader>(),
            provider.GetRequiredService<OutgoingEmailBounds>(),
            provider.GetRequiredService<TimeProvider>()));
        // Scoped because the book it reads is read through the scoped context, and registered beside the composition
        // rather than beside the book: it is the step every author passes through on the way to an outgoing record, so
        // whatever authors a message resolves the people it names here and nowhere else.
        services.AddScoped<NamedRecipientResolver>();
        // Registered beside the composition it produces work for, and scoped for the reason every mailbox read is: it
        // reads stored mail through the same ports a read of that mail uses, and answers with what the composer takes.
        services.AddScoped<StoredEmailResponseAuthoring>();
        // The one way a boundary asks for a new message to be sent, composed from the account catalog, the resolver and
        // the composer registered above it, the outbox registered earlier, and the caller's own grant, and scoped with
        // every one of them. It holds no delivery session and cannot open one, which is what keeps asking to send from
        // ever becoming transmitting.
        services.AddScoped<AuthoredMailSubmission>();
        services.AddScoped<OutgoingMailReader>();
        services.AddScoped<OutgoingMailCancellation>();
        // The same three steps for the two sends that begin from mail already held, composed from the authoring
        // registered above rather than from an account and a recipient list: a reply is addressed by the message it
        // answers. It holds no delivery session either, for the same reason the submission beside it does not.
        services.AddScoped<AuthoredResponseSubmission>();
        // Declaring a message that repeats, out of the same account catalog, resolver, and composer the one-off
        // submission uses. It queues nothing, because nothing is due when a repetition is declared; what turns an
        // occasion into a message is the handler below, reached through the job model like every other recurring
        // dispatch this deployment has.
        services.AddScoped<RecurringMailSubmission>();
        // The same two authors again, ending in the draft book instead of the outbox. They are separate use cases
        // rather than a mode of the two above, because what they produce is a message that is never sent: neither holds
        // a delivery session, and neither leaves anything a delivery pass will claim.
        services.AddScoped<AuthoredMailDrafting>();
        services.AddScoped<AuthoredResponseDrafting>();
        // Where a draft stops being one. It writes an ordinary outgoing record through the outbox registered above, so
        // every bound this deployment sets is asked again at the moment the message would leave rather than only when
        // it was written.
        services.AddScoped<MailDraftPromotion>();
    }

    /// <summary>Registers what a finished mutation leaves behind, the pass that converges an unfinished one, and the gauges both publish.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddMailboxMutations(IServiceCollection services)
    {
        // Read by synchronization rather than by the performer, so that a relocation coming back through an ordinary
        // run is recognized as MailFathom's own instead of being stored as a second email.
        services.AddScoped<IMailboxMutationReconciliationStore, MailboxMutationReconciliationStore>();
        // The history a finished change leaves behind, which is a second table with a lifetime of its own rather than
        // the operational record above: that one ends with the mutation, and this one is kept for as long as the
        // account's retention says and is erased by the pass beside it.
        services.AddScoped<IMailboxMutationAuditEntryStore, MailboxMutationAuditEntryStore>();
        services.AddScoped<MailboxMutationAuditTrailReader>();
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
        // A singleton for the third time, and for the strongest form of the reason: the levels it publishes — how many
        // account runs are queued behind the concurrency bound, and how long each account waits before its next run —
        // describe the process rather than any run, and a second instance would publish a second set of them.
        services.AddSingleton<MailSynchronizationTelemetry>();
        // The same instance under the port the use case reaches it by. A folder run's stages are reported by whoever
        // reports the run itself, so a second registration would be a second vocabulary for one span tree.
        services.AddSingleton<IMailSynchronizationPhaseTelemetry>(provider =>
            provider.GetRequiredService<MailSynchronizationTelemetry>());
        services.AddScoped<MailboxMutationConverger>();
        services.AddScoped<MailboxDestinationResolver>();
        services.AddScoped<IRemoteFolderCatalog>(provider => new MailKitRemoteFolderCatalog(
            MailKitImapClientFactory.CreateWithoutProtocolLogging,
            provider.GetRequiredService<IImapAccountSettingsProvider>(),
            provider.GetRequiredService<IMailAccessTokenSource>(),
            provider.GetRequiredService<OutboundOperationExecutor>(),
            provider.GetRequiredService<ITransientFailureClassifier>()));
    }

    /// <summary>Registers the contact book, the two caller-facing use cases over it, and the collection that fills it from arriving mail.</summary>
    /// <param name="services">The service collection.</param>
    private static void AddContacts(IServiceCollection services)
    {
        // The contact book. Registered unconditionally, because it is a store rather than a capability a deployment
        // switches on: every surface over it is optional, and none of them can be reached without the book existing.
        services.AddScoped<IContactStore, ContactStore>();
        services.AddScoped<IContactDirectory, ContactDirectory>();
        services.AddScoped<ContactBook>();

        // Whose book each act reads and writes, resolved once per unit of work from the principal it was admitted
        // under. Scoped for that reason: it answers about the work in hand rather than about the process.
        services.AddScoped<ContactBookOwnership>();

        // The two caller-facing use cases over it, which are what the protocol tools reach. They are separate from the
        // book because they carry what a caller-facing act owes and the book does not: the grant the caller has to hold,
        // and the bounds every request is checked against before the store is reached.
        services.AddScoped<ContactBookReader>();
        services.AddScoped<ContactBookWriter>();

        // Collection from arriving mail, registered whatever any account's settings say, for the reason the arrival
        // trigger beside classification is: with collection off it reads one property per stored message and reaches
        // neither the book nor the tally. The tally is scoped beside the read context it queries, and the instrument is
        // a singleton because what it counts is a fact about the process rather than about one run.
        services.AddScoped<IAuthoredMailTally, AuthoredMailTally>();
        services.AddSingleton<IContactCollectionTelemetry, ContactCollectionTelemetry>();
        services.AddScoped<MailContactCollector>();
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

    /// <summary>Registers the in-process detector of credentials in mail text.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Called only where the <c>Secrets</c> switch is on, so with it off no expression is compiled and the scanner does
    /// not exist. What the scanner declares it can find is registered by <see cref="AddSensitiveContentCatalogs" />
    /// whatever the switch says, for the reason stated there — so the corpus behind the declaration is read on every
    /// start, and what the switch decides is whether anything scans with it rather than whether it is assembled.
    /// </para>
    /// <para>
    /// The scanner is a singleton, because the corpus is compiled once and the scanner holds it.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSecretContentScanning(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISensitiveContentScanner, SecretContentScanner>();

        return services;
    }

    /// <summary>Registers what every scanner declares it can find, whichever scanners this deployment switched on.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// A catalog is a declaration rather than a detector: it registers no scanner, compiles no expression, and opens no
    /// client. What it does read is the corpus behind the declaration, whenever one is constructed — and one is
    /// constructed on every start, because the host validates the declarations under <c>ValidateOnStart</c>. So the
    /// unconditional registration is not free of that reading; what it is free of is a
    /// detector nothing would use. What reads a catalog selects by the switches — a plan is composed only for a scanner
    /// that is on — so registering all of them changes nothing about what a deployment scans.
    /// </para>
    /// <para>
    /// Unconditional because of what depends on the answer, which is the rule rather than the scanning: startup refuses
    /// a scanner switched on with nothing behind it, and a configuration write is judged by that same rule against the
    /// catalogs this process holds. Registering them with their scanners would make that rule read the switches the
    /// process started with, so on a deployment that scans nothing every candidate turning a scanner on would be refused
    /// as having no detector — a sentence false about the candidate, and a setting no write could ever change.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSensitiveContentCatalogs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISensitiveContentCatalog, SecretContentCatalog>();
        services.AddSingleton<ISensitiveContentCatalog, PersonalDataContentCatalog>();

        return services;
    }

    /// <summary>Registers the detector of personal data in mail text, which reaches an analyzer deployed beside this service.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Called only where the <c>Pii</c> switch is on, which is what keeps an opt-in nobody took from opening anything:
    /// with it off no client is registered, no analyzer address is read, and none of the three descriptors below exists.
    /// The composed <see cref="PersonalDataAnalyzerProfile" /> is the host's to register, because where the analyzer is
    /// comes from configuration this project does not bind.
    /// </para>
    /// <para>
    /// The probe is registered beside the scanner rather than always, because it answers for a client this deployment
    /// only opens where the switch is on: a probe without a scanner would report on an analyzer nothing reaches. The
    /// catalog is neither here nor conditional, for the reason <see cref="AddSensitiveContentCatalogs" /> gives.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddPersonalDataContentScanning(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ISensitiveContentScanner, PresidioContentScanner>();
        services.AddSingleton<IPersonalDataAnalyzerProbe, PresidioAnalyzerProbe>();
        AddPersonalDataAnalyzerClient(services);

        return services;
    }

    /// <summary>Registers the spam scanner, which scores whole messages against a daemon deployed beside this service.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <para>
    /// Called only where the scanner switch is on, which is what makes an opt-in nobody took cost nothing: with it off
    /// none of these descriptors exists, no address is read, and <see cref="EmailSpamClassifier" />
    /// resolves no scanner and classifies through the deterministic stage alone. The composed
    /// <see cref="SpamAssassinScannerProfile" /> is the host's to register, because where the daemon is comes from
    /// configuration this project does not bind.
    /// </para>
    /// <para>
    /// The probe is registered beside the scanner rather than always, because startup refuses a switch that is on with
    /// nothing behind it, and either one present without the other would turn that refusal into a deployment that scores
    /// nothing and says so nowhere.
    /// </para>
    /// <para>
    /// All three are singletons, and the conversation is the reason: it holds the concurrency permits that bound this
    /// process against the daemon, and the corpus identity the startup probe establishes for every scan afterwards. Two
    /// instances would be two of each, so the scanner and the probe are handed the same one.
    /// </para>
    /// <para>
    /// No <c>IHttpClientFactory</c> registration appears here, unlike every other outbound dependency: the daemon speaks
    /// its own line protocol on a TCP port rather than HTTP, so there is no handler chain to bound and the bounds live in
    /// the profile the conversation reads.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSpamAssassinScanning(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<SpamAssassinDaemon>();
        services.AddSingleton<ISpamScanner, SpamAssassinScanner>();
        services.AddSingleton<ISpamScannerProbe, SpamAssassinScannerProbe>();

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
