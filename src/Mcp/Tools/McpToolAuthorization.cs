// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Observability;
using MailFathom.Domain.Access;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools;

/// <summary>Serves each caller the tools its own grant permits, and answers a call for any other as a tool that does not exist.</summary>
/// <remarks>
/// <para>
/// The protocol has nowhere on a tool descriptor to state a required permission, and it expressly allows a returned tool
/// set to vary by the authorization presented on the request. So the listing is where the decision is stated: a tool the
/// caller's grant does not permit is absent from it, and no extension field is invented to say so instead. What the
/// caller loses is the ability to plan a call that could only fail; what it never gains is the knowledge that a
/// capability exists which this deployment declined to offer it.
/// </para>
/// <para>
/// A call naming a tool the grant does not permit is therefore answered exactly as a call naming a tool that does not
/// exist — the same JSON-RPC error, the same code, the same wording, nothing about the caller, the credential, the
/// permission, or what a different caller would have been served. From the caller's side the two are one fact, and a
/// refusal it could tell apart would disclose the capability the listing just withheld. An operator diagnosing a client
/// that stopped working reads the deployment's own record instead.
/// </para>
/// <para>
/// This refuses cheaply and is not the authority. The use case behind each tool asks for the same permission on its own,
/// with the transport absent, so an entrypoint added later cannot widen the surface by forgetting a filter.
/// </para>
/// <para>
/// It composes with the availability rule <see cref="AskMailAdvertisement" /> applies rather than replacing it. A tool
/// may be unavailable, unauthorized, or both; the deployment's own switch stays the authority over whether a capability
/// exists at all, since nothing here can add a descriptor back to a listing. That switch is consulted first, and this
/// filter narrows what it left, which is the order ADR 0012 records.
/// </para>
/// <para>
/// Nothing caches a listing. The set varies by caller, so a shared one would let a caller be served another's answer;
/// this surface stores none, publishes no cache directive a proxy could act on, and answers every listing from the
/// request in hand.
/// </para>
/// </remarks>
internal static class McpToolAuthorization
{
    /// <summary>Removes from a listing every tool the caller's grant does not permit.</summary>
    /// <param name="next">The listing pipeline this filter wraps.</param>
    /// <param name="request">The listing being served.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The listing, narrowed to the tools this caller may call.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="next" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the listing arrives without a service provider, which leaves nothing able to say whose grant to apply.</exception>
    /// <remarks>
    /// The inner pipeline runs first and descriptors are removed from what it produced, for the reason the availability
    /// filter gives: the tools a listing carries are the SDK's to compose — a page of them, with a cursor — and a filter
    /// that decided not to call it would be reimplementing that. A descriptor naming a tool this surface does not publish
    /// is removed as well, because nothing declared what reaching it would require.
    /// </remarks>
    public static async Task<ListToolsResult> WithoutUnauthorizedToolsAsync(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next,
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(request);

        var listing = await next(request, cancellationToken);

        var authorization = RequiredAuthorization(request);
        var permitted = listing.Tools.Where(tool => IsPermitted(authorization, tool.Name)).ToArray();

        if (permitted.Length == listing.Tools.Count)
        {
            return listing;
        }

        // The result is rewritten rather than mutated, because the listing the SDK produced is not this filter's to
        // change in place and a later page's cursor travels with it unaltered.
        return new ListToolsResult
        {
            Tools = permitted,
            NextCursor = listing.NextCursor,
            Meta = listing.Meta,
        };
    }

    /// <summary>Answers a call the caller's grant does not permit as a call naming a tool that does not exist.</summary>
    /// <param name="next">The tool-call pipeline this filter wraps.</param>
    /// <param name="request">The call being served.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The tool's result, for a call the grant permits.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="next" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the call arrives without a service provider, which leaves nothing able to say whose grant to apply.</exception>
    /// <exception cref="McpProtocolException">Thrown when the caller's grant does not permit the tool it named, and when no tool on this surface declares what reaching that name requires.</exception>
    /// <remarks>
    /// The decision is the permission alone, so a name nothing declared a permission for is refused here rather than
    /// passed on to be refused by the server: a tool registered on this surface without an entry in
    /// <see cref="PublishedTools" /> would otherwise be withheld from every listing and then executed for any caller at
    /// all. Both refusals are worded as the server's own answer to a name it does not publish, because two answers a
    /// caller could tell apart would be a disclosure whichever of them was the narrower.
    /// </remarks>
    public static async Task<CallToolResult> RefuseUnauthorizedToolAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(request);

        var requestedToolName = request.Params?.Name;
        var authorization = RequiredAuthorization(request);

        if (!IsPermitted(authorization, requestedToolName))
        {
            PublishedTools.TryGetRequiredPermission(requestedToolName, out var declaredPermission);
            RecordRefusal(request, authorization, requestedToolName, declaredPermission);

            throw UnknownTool(requestedToolName);
        }

        try
        {
            return await next(request, cancellationToken);
        }
        catch (PrincipalNotAuthorizedException refusal)
        {
            // The use case is the authority and this filter is the cheap reading in front of it, so the two can
            // disagree — over a permission the tool and the use case name differently, or over the kind of principal
            // that reached the work. The refusal keeps the answer it already had; what is added is that a boundary the
            // caller learns nothing from is not also one the deployment learns nothing from.
            RecordRefusal(request, authorization, requestedToolName, refusal.RequiredPermission);

            throw;
        }
    }

    /// <summary>Records the refusal on the one channel that can report it, since the caller is told nothing.</summary>
    /// <remarks>
    /// <para>
    /// A call is recorded and a withheld descriptor is not. Nothing was refused by a listing, every narrowed caller
    /// would report one on every listing it asked for, and the omission has no operation to partition by — so counting
    /// it would bury the reading this record exists for, which is a credential that has begun asking for what it was
    /// never granted.
    /// </para>
    /// <para>
    /// The operation is reduced to a name this surface publishes before it is recorded, because the name a call carried
    /// is a string of the caller's choosing and a dimension taking one would let a client mint a time series apiece.
    /// The permission is unspecified for a name no tool declared anything for, and for a use case refusing over the
    /// kind of principal that reached it — which is the refusal no grant would have satisfied rather than a narrower
    /// one.
    /// </para>
    /// </remarks>
    private static void RecordRefusal(
        MessageContext request,
        AccessAuthorization authorization,
        string? requestedToolName,
        MailFathomPermission requiredPermission) =>
        RequiredService<IAuthorizationRefusalTelemetry>(request).RecordRefusal(
            ProtectedSurface.Mail,
            PublishedTools.MeasurableName(requestedToolName),
            requiredPermission,
            authorization.PrincipalIdentity);

    /// <summary>Reports whether the caller may be offered, and may call, the tool a descriptor or a request named.</summary>
    /// <remarks>A tool this surface does not publish answers <see langword="false" />: nothing declared what reaching it requires, and a boundary that let an undeclared tool through would make forgetting to grant one the way to publish it ungoverned.</remarks>
    private static bool IsPermitted(AccessAuthorization authorization, string? toolName) =>
        PublishedTools.TryGetRequiredPermission(toolName, out var requiredPermission)
        && authorization.Permits(requiredPermission);

    /// <summary>Writes the answer a call naming a tool this surface will not serve is refused with.</summary>
    /// <remarks>
    /// <para>
    /// The wording is copied from the SDK's own answer to an unknown tool, because it publishes no member to reach that
    /// answer through. Nothing verifies the two still match: <c>McpToolAuthorizationTests</c> compares what this method
    /// produces against a literal of its own rather than against the SDK, so a release that reworded its message would
    /// leave the suite green. Reaching the SDK's own dispatch needs a composed host over a real transport, which is the
    /// integration suite rather than a unit test.
    /// </para>
    /// <para>
    /// What that drift would cost is a divergence from the server's default phrasing and nothing more. No caller can
    /// tell two refusals apart on this surface whatever the SDK says, because the filter above decides on the
    /// permission alone: an unpermitted name and a name no tool answers to are the same case here and are both refused
    /// before the request reaches the server, so the SDK's unknown-tool path is not reachable through this pipeline.
    /// </para>
    /// </remarks>
    private static McpProtocolException UnknownTool(string? toolName) =>
        new($"Unknown tool: '{toolName}'", McpErrorCode.InvalidParams);

    /// <summary>Resolves the authorization from the scope the request arrived in.</summary>
    /// <remarks>
    /// Resolved per request rather than captured at registration, because the filter is built while the container is
    /// still being described and because the principal it reads belongs to the request. A request that arrived without a
    /// service provider cannot be decided, and serving it as though a grant had been checked would publish tools nobody
    /// established this caller holds, so the composition fault is raised instead.
    /// </remarks>
    private static AccessAuthorization RequiredAuthorization(MessageContext request) =>
        RequiredService<AccessAuthorization>(request);

    /// <summary>Resolves one service from the scope the request arrived in.</summary>
    private static TService RequiredService<TService>(MessageContext request)
        where TService : notnull =>
        request.Services is { } services
            ? services.GetRequiredService<TService>()
            : throw new InvalidOperationException(
                "A tool request arrived without a service provider, so the caller's grant could not be read.");
}
