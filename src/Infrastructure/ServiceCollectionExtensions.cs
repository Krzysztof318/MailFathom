// Copyright © 2026 Krzysztof Kasprowicz

using MailKit.Net.Imap;
using MailMcp.Application.MessageContent;
using MailMcp.Application.Synchronization;
using MailMcp.Infrastructure.Mail.MailKit;
using MailMcp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MailMcp.Infrastructure;

/// <summary>Infrastructure dependency registration.</summary>
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
        services.AddScoped<ISessionFactory, UnitOfWork>();
        services.AddScoped<ISynchronizationCheckpointStore, SynchronizationCheckpointStore>();
        services.AddScoped<IMessageMetadataRepository, MessageMetadataRepository>();
        services.AddScoped<IMessageContentStore, MessageContentStore>();
        services.AddScoped<MailboxSynchronizer>();
        services.AddScoped<IMailboxSessionFactory>(provider => new MailKitImapMailboxSessionFactory(static () => new ImapClient(), provider.GetRequiredService<IMailKitImapAccountSettingsProvider>()));
        return services;
    }
}
