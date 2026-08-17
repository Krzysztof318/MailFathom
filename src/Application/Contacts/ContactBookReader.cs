// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Contacts.Failures;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts;

/// <summary>Reads the contact book for a caller that was granted reading it.</summary>
/// <remarks>
/// <para>
/// The book itself is <see cref="ContactBook" /> and the rows are <see cref="IContactDirectory" />'s; what this use case
/// adds is the two things a caller-facing read owes and neither of those does — that the caller holds
/// <see cref="MailFathomPermission.MailContactsRead" />, and that every bound on a page is applied before the store is
/// reached. The permission is asked for here rather than only at the transport, so an entrypoint added later cannot
/// reach the book by arriving another way.
/// </para>
/// <para>
/// There is no unbounded read. A caller naming no page size is served the book's default rather than everything, and one
/// asking for more than the ceiling is refused rather than quietly served the ceiling — which is what stops a request
/// from deciding how much of a person's correspondents leaves the database at once.
/// </para>
/// <para>
/// Nothing here logs. A name, an address, and a note are personal data about a third party, and a page is a great many
/// of them at once.
/// </para>
/// </remarks>
public sealed class ContactBookReader
{
    private readonly IContactDirectory directory;
    private readonly AccessAuthorization authorization;

    /// <summary>Initializes the use case over the directory it reads and the authorization it asks first.</summary>
    /// <param name="directory">Answers what the book holds.</param>
    /// <param name="authorization">Answers which principal reached this use case.</param>
    /// <exception cref="ArgumentNullException">Thrown when a required collaborator is <see langword="null" />.</exception>
    public ContactBookReader(IContactDirectory directory, AccessAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(authorization);

        this.directory = directory;
        this.authorization = authorization;
    }

    /// <summary>Serves one bounded page of the book.</summary>
    /// <param name="request">What the caller asked the page for.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, with the boundary a following one reads beyond.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the reading grant.</exception>
    /// <exception cref="ContactQueryInvalidException">Thrown when the page size, the origin, or the search text is not one the book serves.</exception>
    /// <exception cref="ContactCursorMalformedException">Thrown when the cursor is not one this system issued.</exception>
    public Task<ContactPage> ReadPageAsync(ContactPageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        this.authorization.RequirePermission(MailFathomPermission.MailContactsRead);

        return this.directory.ReadPageAsync(QueryFrom(request), cancellationToken);
    }

    /// <summary>Reads one contact by the identity the book gave it.</summary>
    /// <param name="contactId">The contact to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The contact, or <see langword="null" /> when the book holds none.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the reading grant.</exception>
    public Task<Contact?> FindAsync(ContactId contactId, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailContactsRead);

        return this.directory.FindAsync(contactId, cancellationToken);
    }

    /// <summary>Reads the person who uses one address.</summary>
    /// <param name="address">The address to resolve.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The contact holding that address, or <see langword="null" /> when nobody in the book does.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the caller does not hold the reading grant.</exception>
    /// <remarks>
    /// The lookup a caller reaches for once it has an address out of mail, answered from the unique index over the
    /// address comparison form rather than from a search over the book. At most one contact can answer, which is the
    /// book's own uniqueness rule rather than a property of this method.
    /// </remarks>
    public Task<Contact?> FindByAddressAsync(EmailAddress address, CancellationToken cancellationToken)
    {
        this.authorization.RequirePermission(MailFathomPermission.MailContactsRead);

        return this.directory.FindByAddressAsync(address, cancellationToken);
    }

    /// <summary>Reads the query a request states, refusing every part of it the book does not serve.</summary>
    /// <remarks>
    /// The cursor is decoded before the query is built, so a caller presenting one this deployment did not issue is told
    /// that rather than served the first page — which would read as a walk that started over.
    /// </remarks>
    private static ContactQuery QueryFrom(ContactPageRequest request)
    {
        if (request.PageSize is { } pageSize && (pageSize < 1 || pageSize > ContactQuery.MaximumPageSize))
        {
            throw ContactQueryInvalidException.PageSizeOutOfRange(ContactQuery.MaximumPageSize);
        }

        if (request.Origin is { } origin && !Enum.IsDefined(origin))
        {
            throw ContactQueryInvalidException.NotAnOrigin();
        }

        ContactCursor? cursor = null;

        if (request.Cursor is not null && !ContactCursor.TryDecode(request.Cursor, out cursor))
        {
            throw new ContactCursorMalformedException();
        }

        return ContactQuery.Create(request.Origin, SearchIn(request), request.PageSize, cursor);
    }

    /// <summary>Reads the search a request states, treating blank text as no narrowing at all.</summary>
    /// <remarks>
    /// An absent search and a blank one are the same request — the whole book — for the reason an absent filter is
    /// everywhere else here: a caller writing an empty argument asked for no narrowing rather than for a person whose
    /// name is empty.
    /// </remarks>
    private static ContactSearch? SearchIn(ContactPageRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Search))
        {
            return null;
        }

        try
        {
            return ContactSearch.Create(request.Search);
        }
        catch (ArgumentException cause)
        {
            throw ContactQueryInvalidException.NotASearch(cause);
        }
    }
}
