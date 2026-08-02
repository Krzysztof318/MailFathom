// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.Emails;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.ListEmails;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Synchronization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;

namespace MailFathom.Mcp.UnitTests;

/// <summary>Composes the MailFathom protocol surface and hands back what it advertises.</summary>
/// <remarks>
/// The composition is shared by every descriptor test, because the descriptors are produced by the registration rather
/// than by any one tool: a test that built its own container would assert against a surface no host composes. The
/// application ports are stubbed only so the container can be built; nothing here calls a tool.
/// </remarks>
internal static class RegisteredMcpToolSurface
{
    /// <summary>Gets every tool the registration advertises.</summary>
    /// <returns>The registered tools, in registration order.</returns>
    public static IReadOnlyList<McpServerTool> Tools()
    {
        using var provider = Compose();

        return [.. provider.GetServices<McpServerTool>()];
    }

    /// <summary>Gets the implementation information the registration reports to a client that initializes a session.</summary>
    /// <returns>The advertised server information, or <see langword="null" /> when the registration advertises none.</returns>
    public static Implementation? ServerInfo()
    {
        using var provider = Compose();

        return provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ServerInfo;
    }

    /// <summary>Gets the descriptor one tool is advertised with.</summary>
    /// <param name="toolName">The protocol name of the tool.</param>
    /// <returns>The advertised descriptor.</returns>
    public static Tool AdvertisedTool(string toolName) =>
        Tools().Single(tool => tool.ProtocolTool.Name == toolName).ProtocolTool;

    private static ServiceProvider Compose()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IStoredEmailTimelineReader>(new StubStoredEmailTimelineReader());
        services.AddSingleton<ISynchronizationFreshnessReader>(new StubSynchronizationFreshnessReader());
        services.AddSingleton<IStoredEmailSummaryReader>(new StubStoredEmailSummaryReader());
        services.AddSingleton<IEmailSearchIndexReader>(new StubEmailSearchIndexReader());
        services.AddSingleton(EmailSearchSnippetBounds.Default);
        services.AddSingleton<IEmailContentStore>(new StubEmailContentStore());
        services.AddSingleton(Substitute.For<IEmailContentRenderer>());
        services.AddSingleton(Substitute.For<IEmailContentRepairRequestStore>());
        services.AddSingleton<IMailAccountCatalog>(new StubMailAccountCatalog("personal"));
        services.AddSingleton<MailboxScopeResolver>();
        services.AddSingleton<MailboxTimelineReader>();
        services.AddSingleton<EmailContentReader>();
        services.AddSingleton<MailboxSearchReader>();
        services.AddMailFathomServer();

        return services.BuildServiceProvider();
    }
}
