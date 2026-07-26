// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailKit.Net.Imap;
using MailMcp.Application.EmailContent;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Mail.MailKit;
using MailMcp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MailMcp.Infrastructure;

/// <summary>Infrastructure dependency registration.</summary>
// TODO: Remove this exclusion when the planned host integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by host integration tests.")]
public static class ServiceCollectionExtensions
{
    /// <summary>Registers EF Core persistence, MailKit mailbox access, and application synchronization services.</summary>
    public static IServiceCollection AddMailMcpInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("mailmcp");
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        services.AddDbContext<MailMcpDbContext>(options => options.UseNpgsql(connectionString));
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
}
