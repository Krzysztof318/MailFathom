// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools.Categories;

/// <summary>Serves each request the categories this deployment publishes, narrowed by what the client asked for.</summary>
/// <remarks>
/// <para>
/// Two decisions meet here and only one of them is a control. <see cref="PublishedToolCategorySelection" /> is what the
/// deployment configured and fixes what this endpoint may ever publish; <see cref="McpToolCategoryHeader" /> is what the
/// client wrote on its own request and can only take away from that. So the effective set is the intersection, computed
/// per request, and no header, however written, publishes a tool the deployment excluded.
/// </para>
/// <para>
/// A tool outside the effective set is absent from <c>tools/list</c> and a call naming it is answered exactly as a call
/// naming a tool that does not exist — the same refusal <see cref="McpToolAuthorization" /> raises, written once in
/// <see cref="UnpublishedTool" /> so a caller cannot tell the two apart. What a client loses is the ability to plan a
/// call that could only fail; what it never gains is the knowledge that this deployment declined to offer something.
/// </para>
/// <para>
/// It composes with the other two rules rather than replacing either, and it enables nothing: a category selection can
/// only remove descriptors, so the capability switches stay the authority over whether a tool exists at all and the
/// grant stays the authority over whether this caller may reach it. A tool is served when its capability is available,
/// its category is published, and the caller's grant permits it — and any one of the three saying no is enough.
/// </para>
/// <para>
/// Nothing here is recorded. A narrowed listing refused nothing, and a call to a tool this endpoint does not publish is
/// already reported by the call reporter that wraps every call; the authorization refusal counter is for a credential
/// asking for what it was never granted, which is a different reading an operator must not find this mixed into.
/// </para>
/// </remarks>
internal static class McpToolCategoryPublication
{
    /// <summary>Removes from a listing every tool outside the categories this request is served.</summary>
    /// <param name="next">The listing pipeline this filter wraps.</param>
    /// <param name="request">The listing being served.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The listing, narrowed to the categories in force.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="next" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the listing arrives without a service provider, which leaves nothing able to say what this deployment publishes.</exception>
    /// <remarks>
    /// The inner pipeline runs first and descriptors are removed from what it produced, for the reason the other two
    /// listing filters give: the tools a listing carries are the SDK's to compose — a page of them, with a cursor — and
    /// a filter that decided not to call it would be reimplementing that. A descriptor naming a tool this surface does
    /// not publish is removed as well, because nothing declared which category it would belong to.
    /// </remarks>
    public static async Task<ListToolsResult> WithoutUnpublishedCategoriesAsync(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next,
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(request);

        var listing = await next(request, cancellationToken);

        var published = EffectiveCategories(request);
        var offered = listing.Tools.Where(tool => IsPublished(published, tool.Name)).ToArray();

        if (offered.Length == listing.Tools.Count)
        {
            return listing;
        }

        // The result is rewritten rather than mutated, because the listing the SDK produced is not this filter's to
        // change in place and a later page's cursor travels with it unaltered.
        return new ListToolsResult
        {
            Tools = offered,
            NextCursor = listing.NextCursor,
            Meta = listing.Meta,
        };
    }

    /// <summary>Answers a call naming a tool outside the categories in force as a call naming a tool that does not exist.</summary>
    /// <param name="next">The tool-call pipeline this filter wraps.</param>
    /// <param name="request">The call being served.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The tool's result, for a call this endpoint publishes.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="next" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the call arrives without a service provider, which leaves nothing able to say what this deployment publishes.</exception>
    /// <exception cref="McpProtocolException">Thrown when the tool's category is not one this request is served, and when no tool on this surface declares a category for the name.</exception>
    public static async Task<CallToolResult> RefuseUnpublishedCategoryToolAsync(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next,
        RequestContext<CallToolRequestParams> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(request);

        var requestedToolName = request.Params?.Name;

        if (!IsPublished(EffectiveCategories(request), requestedToolName))
        {
            throw UnpublishedTool.Refusal(requestedToolName);
        }

        return await next(request, cancellationToken);
    }

    /// <summary>Reports whether the request is served the category a descriptor or a call named.</summary>
    /// <remarks>A tool this surface does not publish answers <see langword="false" />: nothing declared what kind of thing it is, and an endpoint narrowed to one category would otherwise go on offering it.</remarks>
    private static bool IsPublished(PublishedToolCategorySelection published, string? toolName) =>
        PublishedTools.TryGetCategory(toolName, out var category) && published.Publishes(category);

    /// <summary>Computes what this request is served, which is what the deployment publishes narrowed by what the client asked for.</summary>
    /// <remarks>
    /// Decided per request rather than at registration, because the header belongs to the request. The request itself is
    /// reached through the HTTP context of the scope the call arrived in, which is absent where the surface was composed
    /// without one — a host that serves no HTTP transport narrows by the deployment's selection alone rather than
    /// failing, since a client that cannot write a header has asked for nothing.
    /// </remarks>
    private static PublishedToolCategorySelection EffectiveCategories(MessageContext request)
    {
        var services = request.Services
            ?? throw new InvalidOperationException(
                "A tool request arrived without a service provider, so the categories this deployment publishes could not be read.");

        var published = services.GetRequiredService<PublishedToolCategorySelection>();

        return published.NarrowedBy(
            McpToolCategoryHeader.CategoriesNamedBy(services.GetService<IHttpContextAccessor>()?.HttpContext?.Request));
    }
}
