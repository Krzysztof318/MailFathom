// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.BrowseTimeline;

/// <summary>One page of a message list, and the two cursors that continue it in either direction.</summary>
/// <param name="Emails">The rows, in the order the request asked the list to be sorted in, holding no more than the effective page size.</param>
/// <param name="NextCursor">The cursor that reads the page after this one, or <see langword="null" /> when this page ended the list.</param>
/// <param name="PreviousCursor">The cursor that reads the page before this one, or <see langword="null" /> when this page began the list.</param>
/// <param name="PageSize">The page size the read actually ran under, which is the request's own or the default it took.</param>
/// <remarks>
/// <para>
/// Both cursors are the same kind of value and neither is a position the deployment remembers: each names a row of this
/// page and the filters and order the page was read under, so a client may hold one across a screen being left and
/// returned to, and continue from it against a deployment that has restarted in between.
/// </para>
/// <para>
/// An absent cursor is the end of the list in that direction rather than a hint, and it is established the same way at
/// both ends — by asking storage for one row beyond the page and finding none. What neither promises is that the page
/// it reads will be non-empty, since mail can be expunged between two requests; what both guarantee is that continuing
/// from one can neither skip a row nor repeat one.
/// </para>
/// <para>
/// A page that came back empty carries no cursor at either end, because a cursor names a row and there is none. A client
/// that asked for the page before the first row it holds keeps the cursor it already had rather than being handed one
/// back; a client that reached the far end has nothing left to ask for.
/// </para>
/// </remarks>
public sealed record BrowsedTimelinePage(
    IReadOnlyList<BrowsedEmail> Emails,
    string? NextCursor,
    string? PreviousCursor,
    int PageSize);
