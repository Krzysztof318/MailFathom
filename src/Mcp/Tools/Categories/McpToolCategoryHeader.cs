// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Frozen;
using Microsoft.AspNetCore.Http;

namespace MailFathom.Mcp.Tools.Categories;

/// <summary>The request header a client narrows its own view of this surface with.</summary>
/// <remarks>
/// <para>
/// <b>It takes away and never grants.</b> What the deployment published is the authority; this says which part of it a
/// particular client wants to see, so one endpoint can serve an agent that only reads beside one that does everything,
/// and a client that needs a fraction of the surface need not carry the rest in its model's context. A category the
/// configuration excluded is never published because a header asked for it, and no value here enables a capability,
/// widens a grant, or reveals that a withheld tool exists.
/// </para>
/// <para>
/// It is therefore <b>not an authorization mechanism</b> and nothing may be built on it as one. The value is written by
/// the caller, so it is untrusted input in the ordinary sense: it is read, bounded, and mostly ignored. A value naming
/// no published category is dropped rather than refused, because a client sending one has asked for nothing this
/// endpoint can act on and failing its request would turn a narrowing convenience into an outage.
/// </para>
/// <para>
/// The name is MailFathom's own and collides with nothing on the path a request takes. The Streamable HTTP transport
/// reads <c>Mcp-Session-Id</c>, <c>MCP-Protocol-Version</c>, and <c>Last-Event-ID</c>; the authentication methods read
/// <c>Authorization</c>; a reverse proxy writes the <c>X-Forwarded-*</c> family. None of those is this, and the product
/// prefix keeps a future one from becoming this. It carries no <c>X-</c> prefix, which RFC 6648 deprecates for exactly
/// the reason a header outliving its experiment then cannot be renamed. A browser client reaches it because the
/// endpoint's CORS policy names it among the request headers it permits.
/// </para>
/// </remarks>
public static class McpToolCategoryHeader
{
    /// <summary>The header a client writes the categories it wants in.</summary>
    /// <remarks>One header, whose value is a comma-separated list of category names; a client may also repeat the header rather than write one list, which is what an HTTP list header permits and what some clients make easier.</remarks>
    public const string Name = "MailFathom-Tool-Categories";

    /// <summary>The number of characters read across every occurrence of the header before the whole of it is ignored.</summary>
    /// <remarks>
    /// Comfortably above what naming every published category costs and far below what an unbounded read would let a
    /// caller spend. Exceeding it drops the header entirely rather than truncating it, because half a list is a
    /// selection nobody asked for, and dropping it leaves the deployment's own selection in force.
    /// </remarks>
    internal const int MaximumLength = 512;

    /// <summary>The number of names read before the rest are ignored.</summary>
    /// <remarks>Above the published set with room to spare, so a client naming every category and a few it misspelled still says everything it meant. Ignoring the rest can only narrow further, never widen, which is why the excess is dropped rather than refused.</remarks>
    internal const int MaximumNamedCategories = 16;

    private static readonly char[] Separator = [','];

    /// <summary>Reads the categories a request asked its own session to be narrowed to.</summary>
    /// <param name="request">The request being served, or <see langword="null" /> where the surface was reached outside one.</param>
    /// <returns>The published categories the request named, empty when it named none this surface publishes.</returns>
    /// <remarks>
    /// An empty answer is the ordinary case and means the deployment's own selection stands. It covers a request without
    /// the header, one whose header is blank, one that spent more than <see cref="MaximumLength" /> characters, and one
    /// naming only values no category answers to — every shape a caller can write that this surface cannot act on.
    /// </remarks>
    internal static IReadOnlySet<McpToolCategory> CategoriesNamedBy(HttpRequest? request)
    {
        if (request is null)
        {
            return FrozenSet<McpToolCategory>.Empty;
        }

        var written = request.Headers[Name];

        if (written.Count is 0 || written.Sum(static value => value?.Length ?? 0) > MaximumLength)
        {
            return FrozenSet<McpToolCategory>.Empty;
        }

        return written
            .SelectMany(static value => value?.Split(Separator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? [])
            .Take(MaximumNamedCategories)
            .Select(static name => McpToolCategory.TryParse(name, out var category) ? category : default)
            .Where(static category => category.IsSpecified)
            .ToHashSet();
    }
}
