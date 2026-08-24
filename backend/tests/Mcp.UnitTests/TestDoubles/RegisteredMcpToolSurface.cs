// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Contacts;
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
using MailFathom.Application.Persistence;
using MailFathom.Application.Retrieval.AskMail;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Access;
using MailFathom.Mcp.Tools.Categories;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NSubstitute;
using Xunit;

namespace MailFathom.Mcp.UnitTests.TestDoubles;

/// <summary>Composes the MailFathom protocol surface and hands back what it advertises.</summary>
/// <remarks>
/// The composition is shared by every descriptor test, because the descriptors are produced by the registration rather
/// than by any one tool: a test that built its own container would assert against a surface no host composes. The same
/// composition also serves a listing and a call for a stated caller, through <see cref="ComposedForCallerGranted" />,
/// so what a test drives through the filters the registration wrote is the pipeline a host has rather than one the test
/// assembled. The application ports are stubbed so that container can be built; a call driven through these filters
/// ends at a stand-in handler rather than at a tool.
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

    /// <summary>Composes the surface as a host does, for a caller granted exactly the permissions named.</summary>
    /// <param name="grantedPermissions">What the entry that admitted the caller resolved to.</param>
    /// <returns>The provider, which the caller owns and must dispose.</returns>
    /// <remarks>
    /// The container is handed back rather than a listing, because what a listing carries is decided per request by
    /// filters the registration composed. The deployment it describes answers questions, so a tool missing from what
    /// those filters produce is missing for want of a grant rather than for want of a provider.
    /// </remarks>
    public static ServiceProvider ComposedForCallerGranted(params MailFathomPermission[] grantedPermissions) =>
        Compose(
            AccessAuthorizations.ForCallerGranted(grantedPermissions),
            AnsweringDeployment.Capability(new StubMailQuestionAnswerer()));

    /// <summary>Composes the surface as a host does, for a deployment publishing the categories named.</summary>
    /// <param name="publishedCategories">What the deployment's own configuration selected.</param>
    /// <param name="requestedCategories">What the client wrote in the narrowing header, or <see langword="null" /> for a request that carries none.</param>
    /// <returns>The provider, which the caller owns and must dispose.</returns>
    /// <remarks>
    /// The caller is granted the whole mail surface and the deployment answers questions, so a tool missing from what
    /// the filters produce is missing for want of a published category rather than for want of a grant or a capability.
    /// A request carrying the header is composed with an HTTP context holding it, which is the one thing a listing
    /// reaches a header through; without one the accessor is absent entirely, which is the shape a surface composed
    /// outside a request has.
    /// </remarks>
    public static ServiceProvider ComposedPublishing(
        PublishedToolCategorySelection publishedCategories,
        string? requestedCategories = null)
    {
        var context = new DefaultHttpContext();

        if (requestedCategories is not null)
        {
            context.Request.Headers[McpToolCategoryHeader.Name] = requestedCategories;
        }

        return Compose(
            AccessAuthorizations.ForCallerGranted([.. MailFathomPermission.PublishedFor(ProtectedSurface.Mail)]),
            AnsweringDeployment.Capability(new StubMailQuestionAnswerer()),
            publishedCategories,
            requestedCategories is null ? null : context);
    }

    /// <summary>Runs a listing through the filters the registration composed, over a handler standing in for the SDK's own.</summary>
    /// <param name="provider">The composed surface.</param>
    /// <returns>The listing the pipeline produced.</returns>
    internal static Task<ListToolsResult> ListedToolsAsync(IServiceProvider provider)
    {
        var everyDescriptor = new ListToolsResult
        {
            Tools = [.. Tools().Select(static tool => tool.ProtocolTool)],
        };

        var pipeline = RequestFilters(provider).ListToolsFilters
            .Reverse()
            .Aggregate<McpRequestFilter<ListToolsRequestParams, ListToolsResult>, McpRequestHandler<ListToolsRequestParams, ListToolsResult>>(
                (_, _) => new ValueTask<ListToolsResult>(everyDescriptor),
                static (next, filter) => filter(next));

        var request = new RequestContext<ListToolsRequestParams>(
            Substitute.For<McpServer>(),
            new JsonRpcRequest { Method = "tools/list" },
            new ListToolsRequestParams())
        {
            Services = provider,
        };

        return pipeline(request, TestContext.Current.CancellationToken).AsTask();
    }

    /// <summary>Runs a call through the filters the registration composed, over a handler standing in for the tool.</summary>
    /// <param name="provider">The composed surface.</param>
    /// <param name="toolName">The name the call carries.</param>
    /// <param name="served">What the handler behind the filters answers with, so a test can prove the call reached it.</param>
    /// <returns>The result the pipeline produced.</returns>
    internal static Task<CallToolResult> CalledAsync(
        IServiceProvider provider,
        string toolName,
        CallToolResult? served = null)
    {
        var result = served ?? new CallToolResult { Content = [new TextContentBlock { Text = "served" }] };

        var pipeline = RequestFilters(provider).CallToolFilters
            .Reverse()
            .Aggregate<McpRequestFilter<CallToolRequestParams, CallToolResult>, McpRequestHandler<CallToolRequestParams, CallToolResult>>(
                (_, _) => new ValueTask<CallToolResult>(result),
                static (next, filter) => filter(next));

        var request = new RequestContext<CallToolRequestParams>(
            Substitute.For<McpServer>(),
            new JsonRpcRequest { Method = "tools/call" },
            new CallToolRequestParams { Name = toolName })
        {
            Services = provider,
        };

        return pipeline(request, TestContext.Current.CancellationToken).AsTask();
    }

    /// <summary>Reads the filters the registration wrote onto the server options, in the order it registered them.</summary>
    private static McpRequestFilters RequestFilters(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<McpServerOptions>>().Value.Filters.Request;

    private static ServiceProvider Compose(
        AccessAuthorization? authorization = null,
        MailAnsweringCapability? answeringCapability = null,
        PublishedToolCategorySelection? publishedCategories = null,
        HttpContext? httpContext = null)
    {
        var services = new ServiceCollection();

        // Registered as a service as well as a provider, and constructed by the container so it owns the disposal, so a
        // test composing the surface can read what the pipeline wrote without holding the recorder itself: what a
        // filter records is decided by the order the registration composed the filters in, which is what such a test
        // exists to pin.
        services.AddSingleton<RecordingLoggerProvider>();
        services.AddSingleton<ILoggerProvider>(
            container => container.GetRequiredService<RecordingLoggerProvider>());
        services.AddLogging();

        // The descriptors are fixed at registration and do not vary by caller, so the grant here is the whole of this
        // surface unless a test states a narrower one: what a narrower grant withholds is decided per request.
        services.AddSingleton(
            authorization
            ?? AccessAuthorizations.ForCallerGranted([.. MailFathomPermission.PublishedFor(ProtectedSurface.Mail)]));
        // A fake rather than the system clock because the reporter on the call path reads it: a composed call would
        // otherwise be timed against the wall clock and publish a real duration onto the process-wide histogram.
        services.AddSingleton<TimeProvider>(new FakeTimeProvider());
        services.AddSingleton<IStoredEmailTimelineReader>(new StubStoredEmailTimelineReader());
        services.AddSingleton<ISynchronizationFreshnessReader>(new StubSynchronizationFreshnessReader());
        services.AddSingleton<IStoredEmailSummaryReader>(new StubStoredEmailSummaryReader());
        services.AddSingleton<IEmailSearchIndexReader>(new StubEmailSearchIndexReader());
        services.AddSingleton(EmailSearchSnippetBounds.Default);
        services.AddSingleton<IEmailContentStore>(new StubEmailContentStore());
        services.AddSingleton(Substitute.For<IEmailContentRenderer>());
        services.AddSingleton(Substitute.For<IEmailContentRepairRequestStore>());
        // One stub answering both catalogs, because this surface is a deployment serving one owner: what it serves and
        // what that owner owns are the same set, and a tool test here is about the tool rather than about the difference.
        var accountCatalog = new StubMailAccountCatalog("personal");
        services.AddSingleton<IDeploymentMailAccountCatalog>(accountCatalog);
        services.AddSingleton<ICallerMailAccountCatalog>(accountCatalog);
        services.AddSingleton<MailboxScopeResolver>();
        services.AddSingleton(Substitute.For<IMailboxReadTelemetry>());
        services.AddSingleton(Substitute.For<IAuthorizationRefusalTelemetry>());
        services.AddSingleton<MailAccountDirectoryReader>();
        services.AddSingleton<MailboxTimelineReader>();
        services.AddSingleton<EmailContentReader>();
        services.AddSingleton<MailboxSearchReader>();
        // The answering half of a deployment that declared no chat endpoint, which is what makes the descriptors
        // observable at all: what a tool is advertised with is fixed at registration, while whether ask_mail appears in
        // a listing is decided per request and is proved against the filters that decide it.
        services.AddSingleton(
            answeringCapability
            ?? new MailAnsweringCapability(
                LexicalOnlySemanticSearch(),
                Substitute.For<IAiProviderHealthReader>(),
                new FakeTimeProvider(),
                questionAnswerer: null));
        services.AddSingleton<MailboxQuestionReader>();
        services.AddSingleton(MailAnswerBounds.Default);
        services.AddSingleton(Substitute.For<IContactDirectory>());
        services.AddSingleton(new PersistenceConcurrencyOptions());
        services.AddSingleton(Substitute.For<IPersistenceSessionFactory>());
        services.AddSingleton<OptimisticConcurrencyRetryPolicy>();
        services.AddSingleton<ContactBook>();
        services.AddSingleton<ContactBookReader>();
        services.AddSingleton<ContactBookWriter>();
        services.AddSingleton(Substitute.For<IContactStore>());

        // Registered only where a test states a request, so the surface composed without one is the shape a host that
        // serves no HTTP transport has: the filters read no header and narrow by the deployment's selection alone.
        if (httpContext is not null)
        {
            services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = httpContext });
        }

        services.AddMailFathomServer(publishedCategories);

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
