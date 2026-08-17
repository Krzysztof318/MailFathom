// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Common;
using MailFathom.Mcp.Observability;
using MailFathom.Mcp.Serialization;
using MailFathom.Mcp.Tools;
using MailFathom.Versioning;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp;

/// <summary>Composes the MailFathom protocol surface into a host.</summary>
/// <remarks>
/// The registration lives with the tools it registers, so a host adds the surface without knowing which tools exist,
/// which serializer options they publish, or that a call-tool filter reports their outcomes. Mapping the transport
/// endpoint stays the host's decision, because whether the surface is reachable at all is a deployment posture rather
/// than a property of the tools.
/// </remarks>
public static class McpServiceCollectionExtensions
{
    /// <summary>Adds the MailFathom MCP server, its tools, and the reporting that wraps every tool call.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The builder, so a caller can extend the server registration.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="services" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The tools resolve their dependencies per call from the request's scope, so the application ports they read
    /// through may be registered with any lifetime the host chooses — which is also what lets a filter read the caller
    /// the request was admitted under.
    /// </remarks>
    /// <seealso cref="AskMailAdvertisement" />
    /// <seealso cref="McpToolAuthorization" />
    public static IMcpServerBuilder AddMailFathomServer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<McpToolCallTelemetry>();
        services.AddSingleton<McpToolCallReporter>();

        return services
            .AddMcpServer(serverOptions =>
            {
                serverOptions.ServerInfo = ProtocolSurfaceIdentity;
                serverOptions.ServerInstructions = ProtocolSurfaceInstructions;
            })
            // Stateless without a switch: every MailFathom tool answers one request from the local mailbox copy and sends
            // nothing back on its own, so a session would carry no state and only cost a client something to lose across
            // a restart. A tool that pushes notifications would change this surface rather than a deployment's settings.
            .WithHttpTransport(transportOptions => transportOptions.Stateless = true)
            // The SDK's filter delegate is the one signature here that mandates ValueTask, so the conversion happens at
            // that boundary and the reporter itself keeps the Task every other MailFathom method returns.
            .WithRequestFilters(requestFilters => requestFilters
                .AddCallToolFilter(next => (request, cancellationToken) =>
                    new ValueTask<CallToolResult>(RequiredReporter(request).ReportAsync(next, request, cancellationToken)))
                // Inside the reporter, so a call refused for want of a grant is recorded exactly as a call naming a tool
                // that does not exist already is, which is the whole of what the two have to look alike in.
                .AddCallToolFilter(next => (request, cancellationToken) =>
                    new ValueTask<CallToolResult>(
                        McpToolAuthorization.RefuseUnauthorizedToolAsync(next, request, cancellationToken)))
                // Registered before the availability filter and therefore outside it, so the deployment's own switch is
                // evaluated first and this narrows what that switch left — the order ADR 0012 records. Which of the two
                // takes a descriptor away changes no listing, because neither can put one back; the switch stays the
                // authority over whether a capability exists at all, and no grant makes an absent one appear.
                .AddListToolsFilter(next => (request, cancellationToken) =>
                    new ValueTask<ListToolsResult>(
                        McpToolAuthorization.WithoutUnauthorizedToolsAsync(next, request, cancellationToken)))
                // The one tool this surface does not always advertise, decided per listing so an operator who repairs a
                // provider needs no restart to have it offered again.
                .AddListToolsFilter(next => (request, cancellationToken) =>
                    new ValueTask<ListToolsResult>(
                        AskMailAdvertisement.WithoutUnavailableAnsweringAsync(next, request, cancellationToken))))
            .WithTools<ListAccountsTool>(McpToolContractSerialization.Options)
            .WithTools<ListEmailsTool>(McpToolContractSerialization.Options)
            .WithTools<GetEmailContentTool>(McpToolContractSerialization.Options)
            .WithTools<SearchEmailsTool>(McpToolContractSerialization.Options)
            .WithTools<AskMailTool>(McpToolContractSerialization.Options);
    }

    /// <summary>What the server reports about itself when a client initializes a session.</summary>
    /// <remarks>
    /// <para>
    /// Stated rather than left to the SDK's default, which names the entry assembly: a client would then learn the
    /// host's assembly name, which is a composition detail that says nothing about the protocol surface it is talking
    /// to. The product name is what a client recognizes, and the version comes from this assembly's own build-time
    /// metadata rather than from a literal here, so a build cannot report a version it was not stamped with.
    /// </para>
    /// <para>
    /// Only the semantic version is reported, without the source revision that
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0004-versioning-and-release-policy.md">ADR 0004</see> also stamps. This value
    /// answers a compatibility question — which tool contract a client is bound to — and the commit a build came from
    /// is provenance an operator reads from the startup record and the artifact's own labels.
    /// </para>
    /// </remarks>
    private static Implementation ProtocolSurfaceIdentity => new()
    {
        Name = nameof(MailFathom),
        Version = StampedAssemblyVersion.ReadFrom(typeof(McpServiceCollectionExtensions).Assembly).Version,
    };

    /// <summary>What the server tells a client about using it, which is where to read about the version it is talking to.</summary>
    /// <remarks>
    /// <para>
    /// A client that connected over MCP may be the only way its user meets MailFathom at all, so the session itself is
    /// what has to say where the documentation is: nothing else in that arrangement knows the running version, and an
    /// agent asked to consult it would otherwise reach whichever version a search engine ranked first. One sentence
    /// and an address is the whole of it. The protocol places no bound on what instructions may carry, and serving
    /// documentation through them would put a copy of the pages inside the handshake — this makes them findable and
    /// nothing more.
    /// </para>
    /// <para>
    /// The version is taken from what the handshake already reports rather than read a second time, so the pages named
    /// here cannot come to describe a different build from the one the client was told it is talking to. A build whose
    /// version cannot be read carries no instructions rather than an address that goes nowhere, which is the same
    /// reading every other surface applies to the same absence.
    /// </para>
    /// </remarks>
    private static string? ProtocolSurfaceInstructions =>
        DocumentationAddress.ForVersion(ProtocolSurfaceIdentity.Version) is { } address
            ? $"Documentation for the MailFathom version serving this session is at {address}."
            : null;

    /// <summary>Resolves the reporter from the scope the call arrived in.</summary>
    /// <remarks>
    /// Resolved per call rather than captured at registration, because the filter is built while the container is still
    /// being described. A call that arrives without a service provider cannot be reported on, and serving it unreported
    /// would leave undiagnosed failures reaching a client unlogged, so the composition fault is raised instead.
    /// </remarks>
    private static McpToolCallReporter RequiredReporter(RequestContext<CallToolRequestParams> request) =>
        request.Services?.GetRequiredService<McpToolCallReporter>()
        ?? throw new InvalidOperationException("A tool call arrived without a service provider, so its outcome could not be reported.");
}
