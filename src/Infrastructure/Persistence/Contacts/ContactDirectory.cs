// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Reads the contact book from PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Every read uses the scoped context and joins no transaction, and every one of them is bounded: a contact carries at
/// most the addresses the domain admits, and a page carries at most what the query asked for. Both lookups are answered
/// from an index — the primary key and the unique index over the address comparison form — rather than from a scan.
/// </para>
/// <para>
/// A page narrowed by a search is the one read no index answers, because a contained match has no prefix to seek on. It
/// stays bounded by the page size like every other page, and the book it scans is an assembled record of the people one
/// owner wrote down rather than a table that grows with the mail. A book large enough for the scan to matter is what
/// would earn a trigram index and the migration that comes with it, which no deployment has asked for.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ContactDirectory(MailFathomDbContext readContext) : IContactDirectory
{
    /// <inheritdoc />
    public async Task<Contact?> FindAsync(ContactId contactId, CancellationToken cancellationToken)
    {
        var contactValue = contactId.Value;

        var entity = await readContext.Contacts
            .AsNoTracking()
            .Include(record => record.Addresses)
            .FirstOrDefaultAsync(record => record.Id == contactValue, cancellationToken);

        return entity is null ? null : ContactMapping.ToContact(entity);
    }

    /// <inheritdoc />
    public async Task<Contact?> FindByAddressAsync(EmailAddress address, CancellationToken cancellationToken)
    {
        var normalizedAddress = address.NormalizedAddress;

        var entity = await readContext.Contacts
            .AsNoTracking()
            .Include(record => record.Addresses)
            .FirstOrDefaultAsync(
                record => record.Addresses.Any(held => held.NormalizedAddress == normalizedAddress),
                cancellationToken);

        return entity is null ? null : ContactMapping.ToContact(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<EmailAddress, ContactId>> FindHoldersOfAsync(
        IReadOnlyCollection<EmailAddress> addresses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        // The bound the contract states, enforced here rather than trusted: the parameter list becomes one query
        // parameter per address, so a caller asking about more addresses than a person may hold would decide the cost
        // of this read.
        ArgumentOutOfRangeException.ThrowIfGreaterThan(addresses.Count, Contact.MaximumAddressCount, nameof(addresses));

        if (addresses.Count == 0)
        {
            return new Dictionary<EmailAddress, ContactId>();
        }

        var suppliedByNormalizedAddress = new Dictionary<string, EmailAddress>(StringComparer.Ordinal);

        foreach (var address in addresses)
        {
            suppliedByNormalizedAddress.TryAdd(address.NormalizedAddress, address);
        }

        var normalizedAddresses = suppliedByNormalizedAddress.Keys.ToArray();

        var held = await readContext.ContactAddresses
            .AsNoTracking()
            .Where(address => normalizedAddresses.Contains(address.NormalizedAddress))
            .Select(address => new { address.NormalizedAddress, address.ContactId })
            .ToArrayAsync(cancellationToken);

        return held.ToDictionary(
            row => suppliedByNormalizedAddress[row.NormalizedAddress],
            row => ContactId.Create(row.ContactId));
    }

    /// <inheritdoc />
    public async Task<ContactPage> ReadPageAsync(ContactQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var entities = await this.Filter(query)
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

    /// <summary>Applies the filter and the boundary a query names, leaving the ordering to the caller.</summary>
    private IQueryable<ContactEntity> Filter(ContactQuery query)
    {
        var records = readContext.Contacts.AsNoTracking();

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
