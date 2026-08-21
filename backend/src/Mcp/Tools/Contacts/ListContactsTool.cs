// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Contacts;
using MailFathom.Domain.Access;
using MailFathom.Domain.Failures;
using MailFathom.Mcp.Tools.Categories;
using MailFathom.Mcp.Tools.Results;
using ModelContextProtocol.Server;

namespace MailFathom.Mcp.Tools.Contacts;

/// <summary>Publishes the <c>list_contacts</c> tool over the <see cref="ContactBookReader" /> use case.</summary>
/// <param name="contactBookReader">Answers the listing from the contact book.</param>
/// <remarks>
/// <para>
/// The tool translates and nothing more. It converts the caller's arguments into the request the use case is expressed
/// in and maps the page onto the published contract. The page-size range, the search bound, the cursor's authenticity,
/// and the grant the caller has to hold are the use case's own, checked there so an entrypoint added later cannot bypass
/// them — which is why nothing in this class re-states a limit.
/// </para>
/// <para>
/// There is no mode that returns the whole book. A caller naming no page size is served the book's default, and one
/// asking for more than the ceiling is refused rather than quietly served the ceiling.
/// </para>
/// <para>
/// The arguments and the results are personal data about third parties. Nothing here writes either to a log, and the
/// failures the use case raises name the part of the request rather than what was in it.
/// </para>
/// </remarks>
[McpServerToolType]
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The MCP server materializes this tool type per tool call.")]
internal sealed class ListContactsTool(ContactBookReader contactBookReader)
{
    /// <summary>The name the tool is advertised and called under.</summary>
    /// <remarks>Snake case because it is the naming the Model Context Protocol tool ecosystem uses; the C# member naming stops at the boundary.</remarks>
    public const string ToolName = "list_contacts";

    /// <summary>The capability a caller must hold to be offered this tool and to reach the use case behind it.</summary>
    /// <remarks>Reading the book is its own grant, so a deployment can let an agent resolve who somebody is without letting it change or erase the record. Declaring it beside the name is what keeps <see cref="PublishedTools" /> able to answer for every tool this surface publishes.</remarks>
    public static MailFathomPermission RequiredPermission => MailFathomPermission.MailContactsRead;

    /// <summary>The kind of thing this tool is for, which is what a deployment publishes or withholds it by.</summary>
    /// <remarks>The book is an assembled record about identified third parties rather than mail that arrived, so a deployment decides separately whether this endpoint offers it. A category decides what this endpoint offers rather than who may reach it, so it turns nothing on: the tool appears only where the capability behind it is available and the caller's grant reaches it.</remarks>
    public static McpToolCategory Category => McpToolCategory.Contacts;

    /// <summary>Lists a bounded page of the contact book.</summary>
    /// <param name="search">Text a contact must carry in its name or in one of its addresses.</param>
    /// <param name="origin">The origin to narrow the page to.</param>
    /// <param name="pageSize">How many contacts to return, or none to take the default.</param>
    /// <param name="cursor">The cursor a previous page returned.</param>
    /// <param name="cancellationToken">Cancels the read when the caller disconnects or the host shuts down.</param>
    /// <returns>The page, with the cursor of the next one.</returns>
    /// <exception cref="MailFathomException">
    /// Raised by the use case for a page size, a search, a cursor, or a grant it refuses. The call-tool filter turns every
    /// one of them into the coded result a client reads, so this tool neither catches nor re-describes any.
    /// </exception>
    [McpServerTool(
        Name = ToolName,
        Title = "List contacts",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true)]
    [Description(
        "Lists people from MailFathom's own contact book, ordered by name, with the addresses each of them uses. Reads "
        + "local state only: it never contacts a mail server and changes nothing. Narrow the page with search, which "
        + "matches text anywhere in a name or an address without regard to case, and with origin. Returns at most 200 "
        + "contacts per call and 50 by default, with an opaque cursor for the next page; there is no way to ask for the "
        + "whole book in one call. To resolve one address to the person using it, call get_contact with that address "
        + "rather than searching for it here.")]
    public async Task<ListContactsToolResult> ListContactsAsync(
        [Description("Return only contacts carrying this text in their name or in one of their addresses. Matched anywhere in the value and without regard to case, up to 320 characters. Wildcard characters match themselves. Omit to list the whole book, which an empty string does too.")]
        string? search = null,
        [Description("Return only contacts of this origin: asserted for the people somebody wrote down, collected for the addresses this deployment picked up from mail that arrived. Omit to list both.")]
        PublishedContactOrigin? origin = null,
        [Description("How many contacts to return, from 1 to 200. Omit to take the default of 50. A value outside the range is refused rather than clamped.")]
        int? pageSize = null,
        [Description("The nextCursor value from a previous call, to read the following page. It stays valid when search or origin changes, because the book is walked in one order whatever narrows it.")]
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var request = new ContactPageRequest
        {
            Origin = ContactOriginMapping.Recorded(origin),
            Search = search,
            PageSize = pageSize,
            Cursor = cursor,
        };

        var page = await contactBookReader.ReadPageAsync(request, cancellationToken);

        return ListContactsToolResult.From(page);
    }
}
