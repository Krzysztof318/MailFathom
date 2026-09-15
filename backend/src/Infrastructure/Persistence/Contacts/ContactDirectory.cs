// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Reads the contact books from PostgreSQL, one reader's set of them at a time.</summary>
/// <remarks>
/// <para>
/// Every read uses the scoped context and joins no transaction, and every one of them is bounded: a contact carries at
/// most the addresses the domain admits, and a page carries at most what the query asked for. Every lookup is answered
/// from an index — the primary key, the unique index the book and the address comparison form lead, and the listing
/// index the book and the name's comparison form lead — rather than from a scan.
/// </para>
/// <para>
/// The book is the leading column of both of those indexes, so a read is a seek into each of the handful of books the
/// reader holds rather than a walk of every book the deployment has, narrowed afterwards. The identity lookups carry
/// the scope as a predicate beside the key rather than for a plan's sake: a contact of a book outside it is answered as
/// one this reader does not hold, which is what keeps an identifier learned elsewhere from reading somebody else's
/// record.
/// </para>
/// <para>
/// <b>One person is answered once.</b> A user's own book and the collected book of each of their mailboxes may all hold
/// a record for one address, so every read that serves people is taken over <see cref="VisibleIn" />, which hides a
/// record whose address a book earlier in the scope already holds. The precedence is the scope's own order and it
/// reaches the database as the position of a book in that list, so the same record wins on a listing and on a name
/// match. A scope of one book skips the test altogether, because no earlier book exists for anything to be hidden by.
/// </para>
/// <para>
/// <see cref="FindByAddressAsync" /> is the one read that takes the same precedence over the address instead of over
/// the record, and the difference is only visible where one person's records disagree about which addresses they hold.
/// A record hidden from a listing because an earlier book holds one of its addresses may hold another the earlier book
/// does not, and a caller resolving that second address has it in hand already — so answering nothing would deny an
/// address this deployment holds and the reader is assigned. It is not hidden the other way either: the record a
/// listing does show is the one that answers for the address they share.
/// </para>
/// <para>
/// A page narrowed by a search is the one read no index answers, because a contained match has no prefix to seek on. It
/// stays bounded by the page size like every other page, and the books it scans are assembled records of people rather
/// than tables that grow with the mail. A book large enough for the scan to matter is what would earn a trigram index
/// and the migration that comes with it, which no deployment has asked for.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ContactDirectory(MailFathomDbContext readContext) : IContactDirectory
{
    /// <inheritdoc />
    public async Task<Contact?> FindAsync(
        ContactBookScope scope,
        ContactId contactId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var contactValue = contactId.Value;

        var entity = await this.VisibleIn(scope)
            .Include(record => record.Addresses)
            .FirstOrDefaultAsync(record => record.Id == contactValue, cancellationToken);

        return entity is null ? null : ContactMapping.ToContact(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<ContactId, Contact>> FindAllAsync(
        ContactBookScope scope,
        IReadOnlyCollection<ContactId> contactIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(contactIds);

        // The bound the contract states, enforced here rather than trusted: the identities become the elements of one
        // query parameter, so a caller asking about more people than a page of the book holds would decide the size of
        // this read.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            contactIds.Count,
            ContactQuery.MaximumPageSize,
            nameof(contactIds));

        if (contactIds.Count == 0)
        {
            return new Dictionary<ContactId, Contact>();
        }

        var contactValues = contactIds.Select(contactId => contactId.Value).Distinct().ToArray();

        var entities = await this.VisibleIn(scope)
            .Include(record => record.Addresses)
            .Where(record => contactValues.Contains(record.Id))
            .ToArrayAsync(cancellationToken);

        return entities.ToDictionary(entity => ContactId.Create(entity.Id), ContactMapping.ToContact);
    }

    /// <inheritdoc />
    public async Task<Contact?> FindByAddressAsync(
        ContactBookScope scope,
        EmailAddress address,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var normalizedAddress = address.NormalizedAddress;
        var books = scope.Keys.ToArray();

        // The precedence is taken over the address rather than over the record, which is the one read where the two
        // differ. A listing hides a whole record whose address an earlier book holds, because a person is listed once;
        // here the caller already has the address, so the earliest book holding *it* answers even where that record is
        // one a listing would hide for a different address of the same person. Within a book the address is unique, so
        // the order over the scope settles the answer outright.
        //
        // The book is stated inside the address subquery as well as outside it, so the correlated read leads with the
        // unique index the book and the address form rather than probing the addresses of every contact in the scope.
        var entity = await readContext.Contacts.AsNoTracking()
            .Where(record => books.Contains(record.BookHolderId)
                && record.Addresses.Any(held =>
                    books.Contains(held.BookHolderId) && held.NormalizedAddress == normalizedAddress))
            .OrderBy(record => Array.IndexOf(books, record.BookHolderId))
            .Include(record => record.Addresses)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null ? null : ContactMapping.ToContact(entity);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Two statements answer the whole set, whatever it holds: one groups the names by their comparison form and counts
    /// the carriers of each, and one reads the contacts of the names exactly one person carries. The counts come back
    /// from the database rather than from pages this read would otherwise have to hold, which is what keeps a name a
    /// hundred collected contacts happen to share from costing a hundred records to answer with one number, and the
    /// addresses of the people a shared name matched are never loaded at all.
    /// </remarks>
    public async Task<IReadOnlyDictionary<ContactDisplayName, ContactMatch>> MatchDisplayNamesAsync(
        ContactBookScope scope,
        IReadOnlyCollection<ContactDisplayName> displayNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(displayNames);

        // The bound the contract states, enforced here rather than trusted, for the reason the identity lookup above
        // carries: the names become the elements of one query parameter.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            displayNames.Count,
            ContactQuery.MaximumPageSize,
            nameof(displayNames));

        if (displayNames.Count == 0)
        {
            return new Dictionary<ContactDisplayName, ContactMatch>();
        }

        var sortKeys = displayNames.Select(displayName => displayName.SortKey).Distinct().ToArray();

        var carrierCounts = await this.VisibleIn(scope)
            .Where(record => sortKeys.Contains(record.DisplayNameSortKey))
            .GroupBy(record => record.DisplayNameSortKey)
            .Select(carriers => new { SortKey = carriers.Key, CarrierCount = carriers.Count() })
            .ToArrayAsync(cancellationToken);

        var countBySortKey = carrierCounts.ToDictionary(
            row => row.SortKey,
            row => row.CarrierCount,
            StringComparer.Ordinal);

        var carriersBySortKey = await this.ReadCarriersOfAsync(
            scope,
            [.. carrierCounts.Where(row => row.CarrierCount == 1).Select(row => row.SortKey)],
            cancellationToken);

        return displayNames
            .Distinct()
            .ToDictionary(
                displayName => displayName,
                displayName => MatchOf(
                    countBySortKey.GetValueOrDefault(displayName.SortKey),
                    carriersBySortKey.GetValueOrDefault(displayName.SortKey, [])));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<EmailAddress, ContactId>> FindHoldersOfAsync(
        ContactBookHolder holder,
        IReadOnlyCollection<EmailAddress> addresses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(holder);
        ArgumentNullException.ThrowIfNull(addresses);

        var supplied = SuppliedByNormalizedAddress(addresses);

        if (supplied.Count == 0)
        {
            return new Dictionary<EmailAddress, ContactId>();
        }

        var normalizedAddresses = supplied.Keys.ToArray();
        var bookHolderId = holder.Key;

        var held = await readContext.ContactAddresses
            .AsNoTracking()
            .Where(address =>
                address.BookHolderId == bookHolderId && normalizedAddresses.Contains(address.NormalizedAddress))
            .Select(address => new { address.NormalizedAddress, address.ContactId })
            .ToArrayAsync(cancellationToken);

        return held.ToDictionary(
            row => supplied[row.NormalizedAddress],
            row => ContactId.Create(row.ContactId));
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<EmailAddress>> FindHeldAddressesAsync(
        ContactBookScope scope,
        IReadOnlyCollection<EmailAddress> addresses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(addresses);

        var supplied = SuppliedByNormalizedAddress(addresses);

        if (supplied.Count == 0)
        {
            return new HashSet<EmailAddress>();
        }

        var normalizedAddresses = supplied.Keys.ToArray();
        var books = scope.Keys.ToArray();

        // No visibility test, because being held in any of the books is the whole of the question: which of two records
        // for one address the reader is shown does not change that they know the address.
        var held = await readContext.ContactAddresses
            .AsNoTracking()
            .Where(address =>
                books.Contains(address.BookHolderId) && normalizedAddresses.Contains(address.NormalizedAddress))
            .Select(address => address.NormalizedAddress)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        return held.Select(normalized => supplied[normalized]).ToHashSet();
    }

    /// <inheritdoc />
    public async Task<ContactPage> ReadPageAsync(
        ContactBookScope scope,
        ContactQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        var entities = await this.Filter(scope, query)
            .Include(record => record.Addresses)
            .OrderBy(record => record.DisplayNameSortKey)
            .ThenBy(record => record.Id)

            // One more than the page holds, which is how the answer says whether a following page exists without a
            // second count query over the same filtered set.
            .Take(query.PageSize + 1)
            .ToArrayAsync(cancellationToken);

        var pageEntities = entities.Take(query.PageSize).ToArray();
        var contacts = pageEntities.Select(ContactMapping.ToContact).ToArray();

        return new ContactPage(
            contacts,
            entities.Length > query.PageSize && contacts.Length > 0
                ? ContactCursor.After(contacts[^1].DisplayName, contacts[^1].Id)
                : null);
    }

    /// <summary>States what a name resolved to, from the count of its carriers and the ones actually read.</summary>
    /// <remarks>
    /// The count and the contacts come from two statements, so a namesake written down between them leaves a name the
    /// count called unique carrying two people. The verdict for such a name is therefore taken from the read that
    /// produced the contact rather than from the count alone: two carriers refuse as ambiguous, and none — a contact
    /// renamed or erased — resolves to nobody. Either answer is one the count itself would have given a moment later, and
    /// neither hands back a contact the book no longer holds that name uniquely for.
    /// </remarks>
    private static ContactMatch MatchOf(int countedCarriers, ContactEntity[] readCarriers)
    {
        if (countedCarriers == 0)
        {
            return ContactMatch.None;
        }

        if (countedCarriers > 1)
        {
            return ContactMatch.Several(countedCarriers);
        }

        return readCarriers.Length switch
        {
            0 => ContactMatch.None,
            1 => ContactMatch.Unique(ContactMapping.ToContact(readCarriers[0])),
            _ => ContactMatch.Several(readCarriers.Length),
        };
    }

    /// <summary>Indexes the supplied addresses by their comparison form, keeping the first spelling of a repeated one.</summary>
    /// <remarks>
    /// The bound the contract states, enforced here rather than trusted: the parameter list becomes one query parameter
    /// per address, so a caller asking about more addresses than a person may hold would decide the cost of the read.
    /// </remarks>
    private static Dictionary<string, EmailAddress> SuppliedByNormalizedAddress(
        IReadOnlyCollection<EmailAddress> addresses)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(addresses.Count, Contact.MaximumAddressCount, nameof(addresses));

        var supplied = new Dictionary<string, EmailAddress>(StringComparer.Ordinal);

        foreach (var address in addresses)
        {
            supplied.TryAdd(address.NormalizedAddress, address);
        }

        return supplied;
    }

    /// <summary>Narrows to the records a scope shows, hiding one whose address a book earlier in the scope already holds.</summary>
    /// <remarks>
    /// The precedence reaches PostgreSQL as the position of each book in the scope's list, which is what lets one
    /// predicate state both halves of the rule at once — a record the user wrote down beats anything a mailbox
    /// collected, and two mailboxes that collected one address are settled by the order the deployment serves them.
    /// A scope holding one book skips the test: there is no earlier book for anything to be hidden by, and the
    /// alternative would put a correlated subquery on every read a caller makes of their own book alone.
    /// </remarks>
    private IQueryable<ContactEntity> VisibleIn(ContactBookScope scope)
    {
        var books = scope.Keys.ToArray();
        var records = readContext.Contacts.AsNoTracking().Where(record => books.Contains(record.BookHolderId));

        return books.Length == 1
            ? records
            : records.Where(record => !record.Addresses.Any(own => readContext.ContactAddresses.Any(other =>
                other.NormalizedAddress == own.NormalizedAddress
                && books.Contains(other.BookHolderId)
                && Array.IndexOf(books, other.BookHolderId) < Array.IndexOf(books, record.BookHolderId))));
    }

    /// <summary>Reads the contacts carrying each of the names exactly one person was counted under.</summary>
    /// <remarks>
    /// A name is grouped by its comparison form rather than read as one row, because the read is what decides whether the
    /// count still holds and a second carrier has to be visible to it.
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, ContactEntity[]>> ReadCarriersOfAsync(
        ContactBookScope scope,
        string[] sortKeys,
        CancellationToken cancellationToken)
    {
        if (sortKeys.Length == 0)
        {
            return new Dictionary<string, ContactEntity[]>(StringComparer.Ordinal);
        }

        var entities = await this.VisibleIn(scope)
            .Include(record => record.Addresses)
            .Where(record => sortKeys.Contains(record.DisplayNameSortKey))
            .ToArrayAsync(cancellationToken);

        return entities
            .GroupBy(entity => entity.DisplayNameSortKey, StringComparer.Ordinal)
            .ToDictionary(carriers => carriers.Key, carriers => carriers.ToArray(), StringComparer.Ordinal);
    }

    /// <summary>Narrows to the records a scope shows, applies the filter and the boundary a query names, and leaves the ordering to the caller.</summary>
    private IQueryable<ContactEntity> Filter(ContactBookScope scope, ContactQuery query)
    {
        var records = this.VisibleIn(scope);

        if (query.Origin is { } origin)
        {
            records = records.Where(record => record.Origin == origin);
        }

        // Contained-match over the two comparison forms the book already stores, which is why neither side of the
        // predicate has to case-fold anything at query time. It translates to strpos rather than to LIKE, so a wildcard
        // character in the caller's text matches itself instead of widening the search into a scan the caller chose.
        if (query.Search is { } search)
        {
            var soughtText = search.ComparisonForm;

            records = records.Where(record => record.DisplayNameSortKey.Contains(soughtText)
                || record.Addresses.Any(held => held.NormalizedAddress.Contains(soughtText)));
        }

        // The keyset boundary is the pair the order is taken on, so a contact whose name compares equal to the last one
        // of the previous page is served exactly once rather than skipped or repeated. Both comparisons are evaluated by
        // PostgreSQL over the same columns the index is built on, so the walk never depends on how the CLR would have
        // ordered either value.
        if (query.Cursor is { } cursor)
        {
            var boundaryKey = cursor.DisplayNameSortKey;
            var boundaryId = cursor.ContactId.Value;

            records = records.Where(record => record.DisplayNameSortKey.CompareTo(boundaryKey) > 0
                || (record.DisplayNameSortKey == boundaryKey && record.Id > boundaryId));
        }

        return records;
    }
}
