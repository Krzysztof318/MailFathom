// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Repair;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.GetEmailContent;
using MailFathom.Application.Emails.ListEmails;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Observability;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Synchronization.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

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

    /// <summary>Gets the instructions the registration sends a client during the initialization handshake.</summary>
    /// <returns>The advertised instructions, or <see langword="null" /> when the registration advertises none.</returns>
    public static string? ServerInstructions()
    {
        using var provider = Compose();

        return provider.GetRequiredService<IOptions<McpServerOptions>>().Value.ServerInstructions;
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
        services.AddSingleton(Substitute.For<IMailboxReadTelemetry>());
        services.AddSingleton<MailAccountDirectoryReader>();
        services.AddSingleton<MailboxTimelineReader>();
        services.AddSingleton<EmailContentReader>();
        services.AddSingleton<MailboxSearchReader>();
        // The answering half of a deployment that declared no chat endpoint, which is what makes the descriptors
        // observable at all: what a tool is advertised with is fixed at registration, while whether ask_mail appears in
        // a listing is decided per request and is proved against the filter that decides it.
        services.AddSingleton(new MailAnsweringCapability(
            LexicalOnlySemanticSearch(),
            Substitute.For<IAiProviderHealthReader>(),
            new FakeTimeProvider(),
            questionAnswerer: null));
        services.AddSingleton<MailboxQuestionReader>();
        services.AddSingleton(MailAnswerBounds.Default);
        services.AddMailFathomServer();

        return services.BuildServiceProvider();
    }

    /// <summary>Builds the semantic half of a deployment that configured no embedding provider.</summary>
    private static SemanticEmailSearch LexicalOnlySemanticSearch() => new(
        Substitute.For<IActiveEmbeddingProfileReader>(),
        Substitute.For<IEmailVectorSearchIndexReader>(),
        Substitute.For<IAiProviderHealthReader>(),
        new FakeTimeProvider(),
        textEmbeddingGenerator: null);
}
