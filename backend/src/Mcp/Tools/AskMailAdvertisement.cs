// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Retrieval.AskMail;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools;

/// <summary>Decides whether <c>ask_mail</c> appears in what this server advertises.</summary>
/// <remarks>
/// <para>
/// Every other MailFathom tool answers from the local mailbox copy alone and is therefore within reach of every
/// deployment. Answering a question needs two AI providers that an operator configures separately and that fail
/// separately, so this one is offered only while both are configured and working. A client that can see a tool will call it, and a tool that exists
/// only to answer "not configured" costs a round trip to learn something the tool list could have said.
/// </para>
/// <para>
/// It is decided per request rather than at registration, which is what makes the transition observable without a
/// restart: an operator who rotates a refused credential has the tool advertised again on the next listing, and one
/// whose endpoint stops answering has it withdrawn on the next listing after that.
/// </para>
/// <para>
/// Withholding a descriptor for want of a capability is not authorization and is not relied on as any. A client may call
/// a tool it was never offered, and the use case behind this one refuses a question the same way whether or not the
/// caller ever read a list. Whether a caller may reach the tool at all is <see cref="McpToolAuthorization" />'s
/// question, and the two compose: this switch is the authority over whether the capability exists, so no grant makes an
/// absent one appear.
/// </para>
/// </remarks>
internal static class AskMailAdvertisement
{
    /// <summary>Removes the <c>ask_mail</c> descriptor from a listing this deployment cannot currently serve.</summary>
    /// <param name="next">The listing pipeline this filter wraps.</param>
    /// <param name="request">The listing being served.</param>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The listing, with the descriptor removed while the capability is absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="next" /> or <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the listing arrives without a service provider, which leaves nothing able to decide the capability.</exception>
    /// <remarks>
    /// The inner pipeline runs first and the descriptor is removed from what it produced, rather than the capability
    /// being read and the listing skipped. The tools a listing carries are the SDK's to compose — a page of them, with a
    /// cursor — and a filter that decided not to call it would be reimplementing that.
    /// </remarks>
    public static async Task<ListToolsResult> WithoutUnavailableAnsweringAsync(
        McpRequestHandler<ListToolsRequestParams, ListToolsResult> next,
        RequestContext<ListToolsRequestParams> request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(request);

        var listing = await next(request, cancellationToken);

        if (!listing.Tools.Any(static tool => tool.Name == AskMailTool.ToolName))
        {
            return listing;
        }

        var capability = RequiredCapability(request);
        if (await capability.ReadAsync(cancellationToken) is MailAnsweringAvailability.Available)
        {
            return listing;
        }

        // The result is rewritten rather than mutated, because the listing the SDK produced is not this filter's to
        // change in place and a later page's cursor travels with it unaltered.
        return new ListToolsResult
        {
            Tools = [.. listing.Tools.Where(static tool => tool.Name != AskMailTool.ToolName)],
            NextCursor = listing.NextCursor,
            Meta = listing.Meta,
        };
    }

    /// <summary>Resolves the capability from the scope the listing arrived in.</summary>
    /// <remarks>
    /// Resolved per request rather than captured at registration, because the filter is built while the container is
    /// still being described and because the capability reads through a scoped persistence context. A listing that
    /// arrived without a service provider cannot be decided, and advertising the tool anyway would offer a capability
    /// nobody established this deployment has, so the composition fault is raised instead.
    /// </remarks>
    private static MailAnsweringCapability RequiredCapability(RequestContext<ListToolsRequestParams> request) =>
        request.Services?.GetRequiredService<MailAnsweringCapability>()
        ?? throw new InvalidOperationException(
            "A tool listing arrived without a service provider, so the answering capability could not be read.");
}
