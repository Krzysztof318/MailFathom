// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Contacts;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Keeps the contact book in PostgreSQL.</summary>
/// <remarks>
/// <para>
/// Every operation here writes through the context enlisted in the caller's session, erasure included, so nothing can
/// land outside the transaction its caller opened. Nothing logs: every column but the identity and the origin is
/// personal data about a third party, so what a failure carries is the identifier and what a caller learns is the
/// outcome it was handed.
/// </para>
/// <para>
/// Which contact may hold an address is enforced by the unique index over the comparison form rather than by a read
/// before the insert. Two callers claiming one address both read nothing, so only the constraint closes that window —
/// and losing it is a race the retry above resolves into the answer that names the holder.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ContactStore : IContactStore
{
    /// <inheritdoc />
    public Task AddAsync(IPersistenceSession session, Contact contact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(contact);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        writeContext.Contacts.Add(ContactMapping.ToEntity(contact));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> ReplaceAsync(
        IPersistenceSession session,
        Contact contact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(contact);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        // Tracked rather than projected, so the amendment is applied to the row as it stands now and the concurrency
        // token travels with it: a contact erased between this read and the commit makes the write affect no row, which
        // the session reports as a conflict rather than reinserting the person.
        var held = await writeContext.Contacts
            .Include(record => record.Addresses)
            .FirstOrDefaultAsync(record => record.Id == contact.Id.Value, cancellationToken);

        if (held is null)
        {
            return false;
        }

        held.DisplayName = contact.DisplayName.Value;
        held.DisplayNameSortKey = contact.DisplayName.SortKey;
        held.PreferredNormalizedAddress = contact.PreferredAddress.NormalizedAddress;
        held.Note = contact.Note?.Value;

        // The origin is written here even though an amendment never changes it, because promotion is the one write that
        // does and it reaches the row through this method like any other. Copying every column the record states rather
        // than the ones a particular caller is expected to have moved is what keeps that true of the next such write.
        held.Origin = contact.Origin;
        held.AmendedAt = contact.AmendedAt;

        ReplaceAddresses(writeContext, held, contact);

        return true;
    }

    /// <inheritdoc />
    public async Task<ContactErasure> EraseAsync(
        IPersistenceSession session,
        ContactId contactId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var contactValue = contactId.Value;

        // Deleted rather than counted and then cascaded, so the number reported is the number of rows this statement
        // removed. Counting first would report what a separate statement saw, which a write committed between the two
        // makes a different set. The foreign key still cascades and is still what guarantees no address outlives its
        // person; this only takes the same rows first, inside the caller's transaction, to be able to report them.
        var erasedAddresses = await writeContext.ContactAddresses
            .Where(address => address.ContactId == contactValue)
            .ExecuteDeleteAsync(cancellationToken);

        var erasedContacts = await writeContext.Contacts
            .Where(record => record.Id == contactValue)
            .ExecuteDeleteAsync(cancellationToken);

        return new ContactErasure(contactId, erasedContacts > 0, erasedAddresses);
    }

    /// <summary>Brings the stored address rows to exactly the set the amended contact names.</summary>
    /// <remarks>
    /// An address the record still names keeps its row, so the identifier a future derived record could hang on survives
    /// an amendment that only changed the name beside it. What was dropped is deleted rather than left orphaned, which
    /// is also what frees the address for another contact to claim.
    /// </remarks>
    private static void ReplaceAddresses(MailFathomDbContext writeContext, ContactEntity held, Contact contact)
    {
        var named = contact.Addresses.ToDictionary(
            address => address.NormalizedAddress,
            StringComparer.Ordinal);

        foreach (var stored in held.Addresses.ToArray())
        {
            if (named.Remove(stored.NormalizedAddress, out var address))
            {
                stored.Address = address.Address;
            }
            else
            {
                held.Addresses.Remove(stored);
                writeContext.ContactAddresses.Remove(stored);
            }
        }

        foreach (var added in named.Values)
        {
            held.Addresses.Add(ContactMapping.ToAddressEntity(contact, added));
        }
    }
}
