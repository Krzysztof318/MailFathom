// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Accounts;
using MailMcp.Application.EmailContent;
using MailMcp.Application.Emails;
using MailMcp.Application.Emails.GetEmailContent;
using MailMcp.Application.Emails.ListEmails;
using MailMcp.Application.Synchronization;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;

namespace MailMcp.Mcp.UnitTests;

/// <summary>Composes the MailMcp protocol surface and hands back what it advertises.</summary>
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
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IStoredEmailTimelineReader>(new StubStoredEmailTimelineReader());
        services.AddSingleton<ISynchronizationFreshnessReader>(new StubSynchronizationFreshnessReader());
        services.AddSingleton<IStoredEmailSummaryReader>(new StubStoredEmailSummaryReader());
        services.AddSingleton<IEmailContentStore>(new StubEmailContentStore());
        services.AddSingleton(Substitute.For<IEmailContentRenderer>());
        services.AddSingleton(Substitute.For<IEmailContentRepairRequestStore>());
        services.AddSingleton<IMailAccountCatalog>(new StubMailAccountCatalog("personal"));
        services.AddSingleton<MailboxTimelineReader>();
        services.AddSingleton<EmailContentReader>();
        services.AddMailMcpServer();

        using var provider = services.BuildServiceProvider();

        return [.. provider.GetServices<McpServerTool>()];
    }

    /// <summary>Gets the descriptor one tool is advertised with.</summary>
    /// <param name="toolName">The protocol name of the tool.</param>
    /// <returns>The advertised descriptor.</returns>
    public static Tool AdvertisedTool(string toolName) =>
        Tools().Single(tool => tool.ProtocolTool.Name == toolName).ProtocolTool;
}
