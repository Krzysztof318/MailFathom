// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel;
using MailFathom.Application.Contacts;
using MailFathom.Mcp.Tools.Contacts;

namespace MailFathom.Mcp.Tools.Results;

/// <summary>Publishes one page of the contact book.</summary>
/// <remarks>
/// The record is the tool's structured output, so its shape is the advertised output schema and its descriptions travel
/// with it. Paging is stated by the presence of <see cref="NextCursor" /> alone: a caller stops when it is absent rather
/// than spending a request to discover an empty page.
/// </remarks>
[Description("One page of the contact book, ordered by name, with a cursor for the next page.")]
internal sealed record ListContactsToolResult
{
    /// <summary>Gets the contacts this page holds, ordered by name and then by identity.</summary>
    [Description("The contacts on this page, ordered by name and then by identifier. Empty when nobody matched.")]
    public required IReadOnlyList<PublishedContact> Contacts { get; init; }

    /// <summary>Gets the cursor that reads the next page, or <see langword="null" /> when this page is the last one.</summary>
    [Description("An opaque cursor for the next page. Pass it back unchanged as cursor. Null means this page ended the walk. It stays valid when the search or the origin filter changes, because the book is walked in one order whatever narrows it, and no filter makes continuing from it skip or repeat a contact. A rename does: the cursor is a position in the order rather than a snapshot of it, so somebody renamed between two pages may be served twice or not at all.")]
    public string? NextCursor { get; init; }

    /// <summary>Publishes a page the use case answered.</summary>
    /// <param name="page">The page to publish.</param>
    /// <returns>The wire representation of <paramref name="page" />.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="page" /> is <see langword="null" />.</exception>
    public static ListContactsToolResult From(ContactPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ListContactsToolResult
        {
            Contacts = [.. page.Contacts.Select(PublishedContact.From)],
            NextCursor = page.NextCursor?.Encode(),
        };
    }
}
