// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Paging;

/// <summary>Asks one page of an administrative listing walked in identifier order.</summary>
/// <remarks>
/// A page is always bounded, so a caller asking for nothing in particular is served the first
/// <see cref="DefaultPageSize" /> rows rather than every row the deployment holds, and a walk over every row is the
/// caller following the cursor rather than the deployment answering with all of them at once.
/// </remarks>
public sealed record AdministrativeListingQuery
{
    /// <summary>The page size a request that names none is served.</summary>
    public const int DefaultPageSize = 200;

    /// <summary>The greatest page size one request may ask for.</summary>
    /// <remarks>Each row is a few identifiers and a few bounded names, so this bounds what one answer weighs as well as how many rows it names.</remarks>
    public const int MaximumPageSize = 1000;

    private AdministrativeListingQuery(int pageSize, Guid? after)
    {
        this.PageSize = pageSize;
        this.After = after;
    }

    /// <summary>Gets how many rows the page holds at most.</summary>
    public int PageSize { get; }

    /// <summary>Gets the identifier the page continues after, or <see langword="null" /> for the first page.</summary>
    public Guid? After { get; }

    /// <summary>Builds a query from what a caller asked for.</summary>
    /// <param name="pageSize">How many rows the page may hold, or <see langword="null" /> for <see cref="DefaultPageSize" />.</param>
    /// <param name="after">The identifier a continued walk reads beyond, or <see langword="null" /> for the first page.</param>
    /// <returns>The query, or <see langword="null" /> when the page size is outside 1 to <see cref="MaximumPageSize" />.</returns>
    public static AdministrativeListingQuery? Create(int? pageSize, Guid? after)
    {
        var resolvedPageSize = pageSize ?? DefaultPageSize;

        return resolvedPageSize is < 1 or > MaximumPageSize
            ? null
            : new AdministrativeListingQuery(resolvedPageSize, after);
    }

    /// <summary>Cuts what a read of one row past this page returned into the page and where the next one continues.</summary>
    /// <typeparam name="TEntry">What one row of the listing reads as.</typeparam>
    /// <param name="readRows">The rows the read returned, in identifier order, taken as one more than <see cref="PageSize" />.</param>
    /// <param name="identify">Reads a row's identifier.</param>
    /// <returns>The page.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="readRows" /> or <paramref name="identify" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The row past the page is read only so its existence can be observed and is never presented, which is how the
    /// answer says whether a following page exists without counting the whole set. A read that reached past the page
    /// filled it, so the boundary row exists wherever a following page does.
    /// </remarks>
    public AdministrativeListingPage<TEntry> PageOf<TEntry>(IReadOnlyList<TEntry> readRows, Func<TEntry, Guid> identify)
    {
        ArgumentNullException.ThrowIfNull(readRows);
        ArgumentNullException.ThrowIfNull(identify);

        return readRows.Count > this.PageSize
            ? new AdministrativeListingPage<TEntry>([.. readRows.Take(this.PageSize)], identify(readRows[this.PageSize - 1]))
            : new AdministrativeListingPage<TEntry>(readRows, null);
    }
}
