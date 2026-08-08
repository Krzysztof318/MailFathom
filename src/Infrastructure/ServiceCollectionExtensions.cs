// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Embeddings.Generation;
using MailFathom.Application.Emails.Embeddings.Indexing;
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
using MailFathom.Application.Synchronization;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Application.Synchronization.Reconciliation;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Embeddings;
using MailFathom.Infrastructure.Folders;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.MailKit;
using MailFathom.Infrastructure.Mail.MailKit.Writes;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Accounts;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Embeddings;
using MailFathom.Infrastructure.Persistence.Mutations;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Synchronization;
using MailFathom.Infrastructure.Resilience;
using MailFathom.Infrastructure.Secrets.References;
using MailFathom.Infrastructure.Secrets.Resolution;
using MailFathom.Infrastructure.Secrets.Sources;
using MailFathom.Infrastructure.Security.OAuth;
using MailKit.Net.Imap;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        PostgresTextSearchConfiguration textSearchConfiguration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(currentConnectionSettings);
        ArgumentNullException.ThrowIfNull(textSearchConfiguration);

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
        services.AddDbContext<MailFathomDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>(), npgsql => npgsql.UseVector()));
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
        // The only registration here that changes the schema. It is scoped like every other store so that a caller
        // which has opened a persistence session gets its statement inside that session's transaction rather than
        // beside it.
        services.AddScoped<IEmbeddingProfileVectorIndex, EmbeddingProfileVectorIndex>();
        services.AddScoped<IStoredEmailEmbeddingBackfillStore, StoredEmailEmbeddingBackfillStore>();
        services.AddScoped<IEmailMetadataRepository, StoredEmailMetadataRepository>();
        services.AddScoped<IDatabaseSchemaInspector, EfCoreDatabaseSchemaInspector>();
        services.AddScoped<IEmailContentStore, EmailContentStore>();
        services.AddScoped<IStoredEmailExtractionBackfillStore, StoredEmailExtractionBackfillStore>();
        services.AddScoped<IStoredEmailReconciliationStore, StoredEmailReconciliationStore>();
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
        // The HTML sanitizer the renderer owns is built per instance rather than shared, so no configuration of it can
        // be changed by one request and observed by another.
        services.AddScoped<IEmailContentRenderer>(provider => new MimeKitEmailContentRenderer(
            provider.GetRequiredService<EmailMimeExtractionOptions>()));
        services.AddScoped<IMailFolderResolutionStore, MailFolderResolutionStore>();
        services.AddScoped<IMailFolderMappingChangeAuditor, LoggedMailFolderMappingChangeAuditor>();
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
        services.AddScoped<MailFolderResolver>();
        services.AddScoped<MailboxSynchronizer>();
        services.AddScoped<MailboxReconciler>();
        services.AddScoped<StoredEmailExtractionBackfill>();
        services.AddScoped<MailboxScopeResolver>();
        services.AddScoped<MailboxTimelineReader>();
        services.AddScoped<EmailContentReader>();
        services.AddScoped<MailboxSearchReader>();
        // Registered for every deployment rather than only where a chat endpoint was declared, because what it is is a
        // reading of the search above: an instance that answers no questions simply resolves it and never calls it, and
        // the bounds it hands passages over under are the same wherever the retrieval is reached from.
        services.AddSingleton(EmailKnowledgeBounds.Default);
        // Registered as itself as well as behind the port, so a deployment that turns the model-judged second pass on
        // can wrap this one rather than rebuild it. Both resolve the same scoped instance, so an instance that adds no
        // filter is unchanged by the shape.
        services.AddScoped<MailboxKnowledgeSearch>();
        services.AddScoped<IEmailKnowledgeSearch>(provider => provider.GetRequiredService<MailboxKnowledgeSearch>());
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
    /// The two are one call because they are one decision: the backfill's unit of work is one message brought up to
    /// date by that same generator, so a deployment that resolves neither is the only other shape.
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

        // This call is the one place the single-layer rule is enforced for HTTP, so it is the one place it can be got
        // wrong. MailOAuthAccessTokenSource already runs the exchange under the MailAuthorizationServerInvocation
        // pipeline, and the host's service defaults add the standard resilience handler to every client the factory
        // builds; leaving both would multiply three attempts by three into nine token requests against an authorization
        // server that is already refusing. Removal is registered rather than the handler being withheld, because the
        // defaults apply to a name this project never sees.
        //
        // It removes what is registered before it, so it depends on AddServiceDefaults having run first. Host's
        // composition root does, and MailOAuthTokenTransportTests fails if that ever stops being true.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers is experimental, and is how the standard handler is opted out of.
        client.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
    }
}
