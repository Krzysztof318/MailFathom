// Copyright © 2026 Krzysztof Kasprowicz

using MailKit.Net.Imap;
using MailMcp.Application.MessageContent;
using MailMcp.Application.Synchronization;
using MailMcp.Infrastructure.Mail.MailKit;
using MailMcp.Infrastructure.Persistence.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MailMcp.Infrastructure;

/// <summary>Registers infrastructure adapters for MailMcp.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers PostgreSQL persistence, MailKit IMAP, and application synchronization services.</summary>
    public static IServiceCollection AddMailMcpInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var connectionString = configuration.GetConnectionString("mailmcp");
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<MailMcpDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ISynchronizationCheckpointStore, PostgreSqlSynchronizationCheckpointStore>();
        services.AddScoped<IMessageMetadataRepository, PostgreSqlMessageMetadataRepository>();
        services.AddScoped<IMessageContentStore, PostgreSqlMessageContentStore>();
        services.AddScoped<MailboxSynchronizer>();
        services.AddScoped<IImapMailboxSessionFactory>(provider => new MailKitImapMailboxSessionFactory(static () => new ImapClient(), provider.GetRequiredService<IMailKitImapAccountSettingsProvider>()));
        return services;
    }
}
