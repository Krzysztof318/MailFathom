// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts;

/// <summary>Reads what one user's books hold: one person by identity or by an address they use, a set of them by identity or by name, and a page of the whole.</summary>
/// <remarks>
/// <para>
/// Every read names the scope it is answering about, and a scope is one user's: their own book and the collected book
/// of each account assigned to them. An identity, an address, or a name that belongs to a book outside it is answered
/// as one nobody holds, which is the same answer the reader would have got before anybody wrote it down. The reads
/// that would otherwise walk the table lead with the book in the index they are answered from — the address lookup and
/// the listing — while the identity lookups carry the scope as a predicate beside the key they were already seeking on.
/// </para>
/// <para>
/// <b>One person is answered once, from the first book of the scope that holds them.</b> Two books may hold a person
/// under one address — the user wrote somebody down that a mailbox had already collected, or two of their mailboxes
/// each collected the same correspondent — and every read here hides the later record rather than serving both. What
/// "later" means is <see cref="ContactBookScope" />'s order and nothing else, so a listing and a name match never
/// disagree about which of the two exists.
/// </para>
/// <para>
/// That precedence is taken over the record on every read but <see cref="FindByAddressAsync" />, which takes it over
/// the address: a caller there holds the address already, so the earliest book holding that address answers even where
/// a listing hides that record for a different address of the same person. An identity that read hands back is
/// therefore not one the record-level reads are obliged to answer for, and a caller that needs a record the whole
/// surface agrees on reads the person rather than the address.
/// </para>
/// <para>
/// The hiding happens in the query rather than over a page already read, so a page still holds exactly the size asked
/// for while the walk has more to serve, and a short page means the walk is over. Reading to the end of the book is
/// asking for pages until one carries no cursor, exactly as it was before.
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
    /// <param name="scope">The books read.</param>
    /// <param name="contactId">The contact to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The complete contact, or <see langword="null" /> when no book in the scope shows one.</returns>
    Task<Contact?> FindAsync(ContactBookScope scope, ContactId contactId, CancellationToken cancellationToken);

    /// <summary>Reads several contacts by the identities the book gave them.</summary>
    /// <param name="scope">The books read.</param>
    /// <param name="contactIds">The contacts to read, at most one page of the book's worth.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>An entry for every identity the scope shows, keyed by that identity; identities it shows none of are absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more identities are supplied than one page of the book holds.</exception>
    /// <remarks>
    /// One read for a whole set rather than one per identity, because a caller resolving the people an act named would
    /// otherwise make the number of queries its own input's to decide. A caller with more identities than the bound admits
    /// asks in bounded groups; the bound is the same one a page of the book is read under, so no read here is larger than
    /// one this port already answers.
    /// </remarks>
    Task<IReadOnlyDictionary<ContactId, Contact>> FindAllAsync(
        ContactBookScope scope,
        IReadOnlyCollection<ContactId> contactIds,
        CancellationToken cancellationToken);

    /// <summary>Reads the person who uses one address.</summary>
    /// <param name="scope">The books read.</param>
    /// <param name="address">The address to resolve.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The complete contact holding that address, or <see langword="null" /> when nobody in the scope does.</returns>
    /// <remarks>
    /// The lookup is by the address's comparison form, so a caller need not know which casing the book happens to have
    /// recorded. At most one contact answers: each book holds an address once, and where several books hold it the
    /// first of them in the scope's order is the one that answers.
    /// </remarks>
    Task<Contact?> FindByAddressAsync(
        ContactBookScope scope,
        EmailAddress address,
        CancellationToken cancellationToken);

    /// <summary>Reads who each name resolves to, by the whole of that name rather than by part of it.</summary>
    /// <param name="scope">The books read.</param>
    /// <param name="displayNames">The names to resolve, at most one page of the book's worth.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>An entry for every supplied name, keyed by the name as supplied, carrying the one contact under it or how many carry it; a name nobody carries reports none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more names are supplied than one page of the book holds.</exception>
    /// <remarks>
    /// <para>
    /// The comparison is on each name's whole comparison form, which is what separates this from the contained match a
    /// page's search performs: a lookup that addresses a message resolves to one person or to nobody, and text that
    /// merely appears inside somebody's name is not that person being named. Both are answered from the listing index
    /// the book is ordered by.
    /// </para>
    /// <para>
    /// The count is exact and counts only the records the scope shows, so a collected record hidden by one the user
    /// wrote down does not make their own name ambiguous. The addresses of the people an ambiguous name counted are
    /// never read, so a name a hundred collected contacts happen to share costs one number rather than a page of
    /// somebody else's correspondents. A name resolving to one person answers with that person and with the count that
    /// decided it read together, so no caller can be handed a contact the scope no longer shows uniquely.
    /// </para>
    /// <para>
    /// Every supplied name is answered rather than only the ones somebody carries, because <see cref="ContactMatch" />
    /// already states "nobody" and a caller reading a set has to tell that answer from a name the read never covered.
    /// </para>
    /// </remarks>
    Task<IReadOnlyDictionary<ContactDisplayName, ContactMatch>> MatchDisplayNamesAsync(
        ContactBookScope scope,
        IReadOnlyCollection<ContactDisplayName> displayNames,
        CancellationToken cancellationToken);

    /// <summary>Reads which contacts of one book already hold each of the given addresses.</summary>
    /// <param name="holder">The book read.</param>
    /// <param name="addresses">The addresses to look up, at most as many as one contact may hold.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>An entry for every address that book already holds, keyed by the address as supplied; addresses nobody in it holds are absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more addresses are supplied than one contact may hold.</exception>
    /// <remarks>
    /// One book rather than a scope, because this is what a write asks before claiming a set of addresses and the rule
    /// it is asking about — one address, one contact — holds within a book rather than across them. One lookup for a
    /// whole record rather than one per address, so a contact's cost does not depend on how many mailboxes a person uses.
    /// </remarks>
    Task<IReadOnlyDictionary<EmailAddress, ContactId>> FindHoldersOfAsync(
        ContactBookHolder holder,
        IReadOnlyCollection<EmailAddress> addresses,
        CancellationToken cancellationToken);

    /// <summary>Reads which of the given addresses some contact the scope shows already holds.</summary>
    /// <param name="scope">The books read.</param>
    /// <param name="addresses">The addresses to look up, at most as many as one contact may hold.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The supplied addresses that some book of the scope holds; the rest are absent.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when more addresses are supplied than one contact may hold.</exception>
    /// <remarks>
    /// The question is whether the user knows the address at all, so it answers with the addresses rather than with
    /// whoever holds each — and the precedence between two books that both hold one does not arise, because being held
    /// in either is the whole of the answer.
    /// </remarks>
    Task<IReadOnlySet<EmailAddress>> FindHeldAddressesAsync(
        ContactBookScope scope,
        IReadOnlyCollection<EmailAddress> addresses,
        CancellationToken cancellationToken);

    /// <summary>Reads one bounded page of the books a scope reads.</summary>
    /// <param name="scope">The books read.</param>
    /// <param name="query">What to read and where to continue from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, with the boundary the following one reads beyond when more contacts follow.</returns>
    Task<ContactPage> ReadPageAsync(
        ContactBookScope scope,
        ContactQuery query,
        CancellationToken cancellationToken);
}
