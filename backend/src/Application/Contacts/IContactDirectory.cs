// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts;

/// <summary>Reads one owner's contact book: one person by identity or by an address they use, a set of them by identity or by name, and a page of the whole.</summary>
/// <remarks>
/// <para>
/// Every read names the owner whose book it is answering about, and a book is only ever read as a whole one person's:
/// an identity, an address, or a name that belongs to somebody else's book is answered as one the book does not hold,
/// which is the same answer the reader would have got before anybody wrote it down. The reads that would otherwise walk
/// the table lead with the owner in the index they are answered from — the address lookup and the listing — while the
/// identity lookups carry the owner as a predicate beside the key they were already seeking on.
/// </para>
/// <para>
/// Every read joins no transaction and returns complete contacts rather than an entity graph, which is why it is a port
/// of its own beside <see cref="IContactStore" /> rather than a set of methods on it. Every lookup is answered from an
/// index rather than from a scan, and the page is bounded and ordered so a walk of the book terminates.
/// </para>
/// </remarks>
public interface IContactDirectory
{
    /// <summary>Reads one contact by the identity the book gave it.</summary>
    /// <param name="owner">The owner whose book is read.</param>
    /// <param name="contactId">The contact to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The complete contact, or <see langword="null" /> when the book holds none.</returns>
    Task<Contact?> FindAsync(MailOwnerId owner, ContactId contactId, CancellationToken cancellationToken);

    /// <summary>Reads several contacts by the identities the book gave them.</summary>
    /// <param name="owner">The owner whose book is read.</param>
    /// <param name="contactIds">The contacts to read, at most one page of the book's worth.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>An entry for every identity the book holds, keyed by that identity; identities it holds none of are absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contactIds" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more identities are supplied than one page of the book holds.</exception>
    /// <remarks>
    /// One read for a whole set rather than one per identity, because a caller resolving the people an act named would
    /// otherwise make the number of queries its own input's to decide. A caller with more identities than the bound admits
    /// asks in bounded groups; the bound is the same one a page of the book is read under, so no read here is larger than
    /// one this port already answers.
    /// </remarks>
    Task<IReadOnlyDictionary<ContactId, Contact>> FindAllAsync(
        MailOwnerId owner,
        IReadOnlyCollection<ContactId> contactIds,
        CancellationToken cancellationToken);

    /// <summary>Reads the person who uses one address.</summary>
    /// <param name="owner">The owner whose book is read.</param>
    /// <param name="address">The address to resolve.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The complete contact holding that address, or <see langword="null" /> when nobody in the book does.</returns>
    /// <remarks>
    /// The lookup is by the address's comparison form, so a caller need not know which casing the book happens to have
    /// recorded. At most one contact can answer, which is a property of the store rather than of this method.
    /// </remarks>
    Task<Contact?> FindByAddressAsync(
        MailOwnerId owner,
        EmailAddress address,
        CancellationToken cancellationToken);

    /// <summary>Reads who each name resolves to, by the whole of that name rather than by part of it.</summary>
    /// <param name="owner">The owner whose book is read.</param>
    /// <param name="displayNames">The names to resolve, at most one page of the book's worth.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>An entry for every supplied name, keyed by the name as supplied, carrying the one contact under it or how many carry it; a name nobody carries reports none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="displayNames" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more names are supplied than one page of the book holds.</exception>
    /// <remarks>
    /// <para>
    /// The comparison is on each name's whole comparison form, which is what separates this from the contained match a
    /// page's search performs: a lookup that addresses a message resolves to one person or to nobody, and text that
    /// merely appears inside somebody's name is not that person being named. Both are answered from the listing index
    /// the book is ordered by.
    /// </para>
    /// <para>
    /// The count an ambiguous name answers with is exact, and the addresses of the people it counted are never read, so a
    /// name a hundred collected contacts happen to share costs one number rather than a page of somebody else's
    /// correspondents. A name resolving to one person answers with that person and with the count that decided it read
    /// together, so no caller can be handed a contact the book no longer holds uniquely.
    /// </para>
    /// <para>
    /// Every supplied name is answered rather than only the ones somebody carries, because <see cref="ContactMatch" />
    /// already states "nobody" and a caller reading a set has to tell that answer from a name the read never covered.
    /// </para>
    /// </remarks>
    Task<IReadOnlyDictionary<ContactDisplayName, ContactMatch>> MatchDisplayNamesAsync(
        MailOwnerId owner,
        IReadOnlyCollection<ContactDisplayName> displayNames,
        CancellationToken cancellationToken);

    /// <summary>Reads which contacts already hold each of the given addresses.</summary>
    /// <param name="owner">The owner whose book is read.</param>
    /// <param name="addresses">The addresses to look up, at most as many as one contact may hold.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>An entry for every address the book already holds, keyed by the address as supplied; addresses nobody holds are absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="addresses" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more addresses are supplied than one contact may hold.</exception>
    /// <remarks>
    /// One lookup for a whole record rather than one per address, because this is what a write asks before claiming a
    /// set of addresses and a query per address would make a contact's cost depend on how many mailboxes a person uses.
    /// </remarks>
    Task<IReadOnlyDictionary<EmailAddress, ContactId>> FindHoldersOfAsync(
        MailOwnerId owner,
        IReadOnlyCollection<EmailAddress> addresses,
        CancellationToken cancellationToken);

    /// <summary>Reads one bounded page of the book.</summary>
    /// <param name="owner">The owner whose book is read.</param>
    /// <param name="query">What to read and where to continue from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, with the boundary the following one reads beyond when the book holds more.</returns>
    Task<ContactPage> ReadPageAsync(MailOwnerId owner, ContactQuery query, CancellationToken cancellationToken);
}
