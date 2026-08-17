// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>Asks for one bounded, keyset-paginated page of the contact book.</summary>
/// <remarks>
/// A page is always bounded and always ordered by the name's comparison form and then by the identity, so a caller that
/// supplies nothing is served the first <see cref="DefaultPageSize" /> contacts in that order rather than the whole book.
/// The order is what makes the walk complete: it is total, it never changes while a walk is in progress unless a contact
/// is renamed, and it is the order the supporting index is built in.
/// </remarks>
public sealed record ContactQuery
{
    /// <summary>The page size a request that names none is served.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>The greatest page size one request may ask for.</summary>
    /// <remarks>
    /// A contact carries its addresses and its note, so a page is bounded by what it returns rather than only by how
    /// many rows it names. This is the ceiling every surface over the book inherits.
    /// </remarks>
    public const int MaximumPageSize = 200;

    private ContactQuery(ContactOrigin? origin, ContactSearch? search, int pageSize, ContactCursor? cursor)
    {
        this.Origin = origin;
        this.Search = search;
        this.PageSize = pageSize;
        this.Cursor = cursor;
    }

    /// <summary>Gets the origin the page is narrowed to, or <see langword="null" /> for the whole book.</summary>
    /// <remarks>
    /// Reading one origin is the question "what did my instance pick up" and its inverse "what did I write down", which
    /// is the one filter the book needs before a surface exists over it.
    /// </remarks>
    public ContactOrigin? Origin { get; }

    /// <summary>Gets the text a contact must carry in its name or in one of its addresses, or <see langword="null" /> for no narrowing.</summary>
    /// <remarks>
    /// A contained match rather than a prefix or an exact one, because the question a caller asks the book is "who is
    /// this", written with whatever part of a name or an address they have. It narrows the page and never reorders it:
    /// the walk stays in the order the whole book is in, so a cursor cut under one search still names a valid boundary.
    /// </remarks>
    public ContactSearch? Search { get; }

    /// <summary>Gets how many contacts the page holds at most.</summary>
    public int PageSize { get; }

    /// <summary>Gets the boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</summary>
    public ContactCursor? Cursor { get; }

    /// <summary>Builds a validated query from what a caller asked for.</summary>
    /// <param name="origin">The origin to narrow to, or <see langword="null" /> for the whole book.</param>
    /// <param name="search">The text a contact must carry in its name or in one of its addresses, or <see langword="null" /> for no narrowing.</param>
    /// <param name="pageSize">How many contacts the page may hold, or <see langword="null" /> for <see cref="DefaultPageSize" />.</param>
    /// <param name="cursor">The boundary a continued walk reads beyond, or <see langword="null" /> for the first page.</param>
    /// <returns>The query the store reads under.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="pageSize" /> is below one or above <see cref="MaximumPageSize" />, or when <paramref name="origin" /> names no declared value.</exception>
    public static ContactQuery Create(
        ContactOrigin? origin,
        ContactSearch? search,
        int? pageSize,
        ContactCursor? cursor)
    {
        var resolvedPageSize = pageSize ?? DefaultPageSize;

        ArgumentOutOfRangeException.ThrowIfLessThan(resolvedPageSize, 1, nameof(pageSize));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(resolvedPageSize, MaximumPageSize, nameof(pageSize));

        if (origin is { } named && !Enum.IsDefined(named))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), "A contact origin must name a declared value.");
        }

        return new ContactQuery(origin, search, resolvedPageSize, cursor);
    }
}
