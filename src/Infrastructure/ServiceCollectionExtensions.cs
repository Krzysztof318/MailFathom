// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailFathom.Application.EmailContent;
using MailFathom.Application.Emails;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.ListEmails;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.Application.Resilience;
using MailFathom.Application.Synchronization;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Folders;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Mail.MailKit;
using MailFathom.Infrastructure.Mail.Mime;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Resilience;
using MailFathom.Infrastructure.Secrets;
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
    /// The settings arrive already read rather than as an <c>IConfiguration</c> this method reaches into, so which key
    /// holds them stays a host decision and this assembly gains no configuration dependency. They arrive as an
    /// accessor rather than as a value because a secret reference can be repointed by a configuration reload, and a
    /// value captured at registration would keep authenticating with the reference the operator replaced.
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

            return dataSourceBuilder.Build();
        });
        // EnableRetryOnFailure is deliberately not configured. A retrying execution strategy refuses the
        // user-initiated transaction PersistenceSessionFactory opens for every session, so turning it on would fail
        // every write at session start rather than merely leave it un-retried. Adopting it instead means handing each
        // unit of work to Database.CreateExecutionStrategy().ExecuteAsync so the strategy can replay it whole, and
        // dropping OutboundDependency.DatabaseCommandExecution from those paths so the two never stack.
        services.AddDbContext<MailFathomDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<IPersistenceSessionFactory, PersistenceSessionFactory>();
        services.AddScoped<ISynchronizationCheckpointStore, SynchronizationCheckpointStore>();
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
        services.AddScoped<ISynchronizationFreshnessReader, SynchronizationFreshnessReader>();
        // The one write a read path performs. It joins no session for the reason its port states, so it is registered
        // beside the readers rather than with the repositories that take one.
        services.AddScoped<IEmailContentRepairRequestStore, EmailContentRepairRequestStore>();
        // MimeKit arrives with MailKit, so message parsing needs no dependency of its own; the adapter keeps its types
        // out of Application the same way the IMAP adapter keeps MailKit's out.
        services.AddScoped<IEmailMimeReader>(provider => new MimeKitEmailMimeReader(
            provider.GetRequiredService<EmailMimeExtractionOptions>()));
        // The HTML sanitizer the renderer owns is built per instance rather than shared, so no configuration of it can
        // be changed by one request and observed by another.
        services.AddScoped<IEmailContentRenderer>(provider => new MimeKitEmailContentRenderer(
            provider.GetRequiredService<EmailMimeExtractionOptions>(),
            provider.GetRequiredService<EmailContentReadOptions>()));
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
        services.AddScoped<IMailboxSessionFactory>(provider => new MailKitImapMailboxSessionFactory(
            static () => new ImapClient(),
            provider.GetRequiredService<IImapAccountSettingsProvider>(),
            provider.GetRequiredService<OutboundOperationExecutor>(),
            provider.GetRequiredService<ITransientFailureClassifier>(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IRemoteFolderCatalog>(provider => new MailKitRemoteFolderCatalog(
            static () => new ImapClient(),
            provider.GetRequiredService<IImapAccountSettingsProvider>(),
            provider.GetRequiredService<OutboundOperationExecutor>(),
            provider.GetRequiredService<ITransientFailureClassifier>()));

        return services;
    }
}
