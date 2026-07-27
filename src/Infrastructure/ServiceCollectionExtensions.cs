// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailKit.Net.Imap;
using MailMcp.Application.EmailContent;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Mail.MailKit;
using MailMcp.Infrastructure.Persistence;
using MailMcp.Infrastructure.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace MailMcp.Infrastructure;

/// <summary>Infrastructure dependency registration.</summary>
// TODO: Remove this exclusion when the planned host integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by host integration tests.")]
public static class ServiceCollectionExtensions
{
    /// <summary>Registers the secret reference grammar, the shipped scheme adapters, and the composite dispatch.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="interpretation">How configured secret-bearing values are interpreted.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A provider for a managed store registers its own <see cref="ISecretSchemeResolver" /> through its own extension
    /// beside this call and needs no edit here, because the composite dispatches over whatever adapters it is handed.
    /// </remarks>
    public static IServiceCollection AddSecretResolution(
        this IServiceCollection services,
        SecretValueInterpretation interpretation)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new SecretResolutionOptions(interpretation));
        services.AddSingleton<ISecretFileReader, FileSystemSecretFileReader>();
        services.AddSingleton<IEnvironmentVariableReader, ProcessEnvironmentVariableReader>();
        services.AddSingleton<ISecretSchemeResolver, SystemdCredentialSecretReferenceResolver>();
        services.AddSingleton<ISecretSchemeResolver, FileSecretReferenceResolver>();
        services.AddSingleton<ISecretSchemeResolver, EnvironmentVariableSecretReferenceResolver>();
        services.AddSingleton<ISecretSchemeResolver, PlaintextSecretReferenceResolver>();
        services.AddSingleton<ISecretReferenceResolver, CompositeSecretReferenceResolver>();

        return services;
    }

    /// <summary>Registers EF Core persistence, MailKit mailbox access, and application synchronization services.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration carrying the <c>mailmcp</c> connection string.</param>
    /// <param name="databasePassword">The database password block, or <see langword="null" /> when the deployment authenticates without one.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> or <paramref name="configuration" /> is <see langword="null" />.</exception>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        ConfiguredSecret? databasePassword)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("mailmcp");
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddSingleton(provider => BuildDataSource(provider, connectionString, databasePassword));
        services.AddDbContext<MailMcpDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<NpgsqlDataSource>()));
        services.AddScoped<IPersistenceSessionFactory, PersistenceSessionFactory>();
        services.AddScoped<ISynchronizationCheckpointStore, SynchronizationCheckpointStore>();
        services.AddScoped<IEmailMetadataRepository, StoredEmailMetadataRepository>();
        services.AddScoped<IEmailContentStore, EmailContentStore>();
        services.AddScoped<OptimisticConcurrencyRetryPolicy>();
        services.AddScoped<MailboxSynchronizer>();
        services.AddScoped<IMailboxSessionFactory>(provider => new MailKitImapMailboxSessionFactory(
            static () => new MailKitImapClientAdapter(new ImapClient()),
            provider.GetRequiredService<IImapAccountSettingsProvider>()));

        return services;
    }

    /// <summary>Builds the PostgreSQL data source, resolving the configured password on first use.</summary>
    /// <remarks>
    /// Composition is deferred to first use rather than performed during registration, because registration runs
    /// synchronously before the startup validator resolves anything, so a password passed in already-resolved form
    /// would simply not exist yet and the host would quietly keep using a passwordless connection string.
    /// </remarks>
    [SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits", Justification = "The dependency-injection factory contract is synchronous. This runs once, in a singleton factory, after startup validation has already proved the reference resolves.")]
    private static NpgsqlDataSource BuildDataSource(
        IServiceProvider provider,
        string connectionString,
        ConfiguredSecret? databasePassword)
    {
        var connectionSettings = ConnectionStringComposer
            .ComposeAsync(
                connectionString,
                databasePassword,
                provider.GetRequiredService<ISecretReferenceResolver>(),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        return new NpgsqlDataSourceBuilder(connectionSettings.ConnectionString).Build();
    }
}
