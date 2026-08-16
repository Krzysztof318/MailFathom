// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts;

/// <summary>Reads the contact book: one person by identity, the person behind an address, and a page of the whole.</summary>
/// <remarks>
/// Every read joins no transaction and returns complete contacts rather than an entity graph, which is why it is a port
/// of its own beside <see cref="IContactStore" /> rather than a set of methods on it. Both lookups are answered from an
/// index rather than from a scan, and the page is bounded and ordered so a walk of the book terminates.
/// </remarks>
public interface IContactDirectory
{
    /// <summary>Reads one contact by the identity the book gave it.</summary>
    /// <param name="contactId">The contact to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The complete contact, or <see langword="null" /> when the book holds none.</returns>
    Task<Contact?> FindAsync(ContactId contactId, CancellationToken cancellationToken);

    /// <summary>Reads the person who uses one address.</summary>
    /// <param name="address">The address to resolve.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The complete contact holding that address, or <see langword="null" /> when nobody in the book does.</returns>
    /// <remarks>
    /// The lookup is by the address's comparison form, so a caller need not know which casing the book happens to have
    /// recorded. At most one contact can answer, which is a property of the store rather than of this method.
    /// </remarks>
    Task<Contact?> FindByAddressAsync(EmailAddress address, CancellationToken cancellationToken);

    /// <summary>Reads which contacts already hold each of the given addresses.</summary>
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
        IReadOnlyCollection<EmailAddress> addresses,
        CancellationToken cancellationToken);

    /// <summary>Reads one bounded page of the book.</summary>
    /// <param name="query">What to read and where to continue from.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The page, with the boundary the following one reads beyond when the book holds more.</returns>
    Task<ContactPage> ReadPageAsync(ContactQuery query, CancellationToken cancellationToken);
}
