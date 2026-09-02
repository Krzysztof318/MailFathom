// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Keeps the contact books in PostgreSQL, one owner's at a time.</summary>
/// <remarks>
/// <para>
/// Every operation here writes through the context enlisted in the caller's session, erasure included, so nothing can
/// land outside the transaction its caller opened. Nothing logs: every column but the identity and the origin is
/// personal data about a third party, so what a failure carries is the identifier and what a caller learns is the
/// outcome it was handed.
/// </para>
/// <para>
/// Which contact may hold an address is enforced by the unique index over the owner and the comparison form rather than
/// by a read before the insert. Two callers claiming one address both read nothing, so only the constraint closes that
/// window — and losing it is a race the retry above resolves into the answer that names the holder.
/// </para>
/// <para>
/// Every statement here carries the owner whose book is being written, beside the identity it was given. A contact
/// identifier that belongs to another owner's book therefore matches no row rather than reaching one, so a replacement
/// and an erasure are as scoped as a read is, and neither can be aimed across books by a caller that learned an
/// identifier elsewhere.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ContactStore : IContactStore
{
    /// <inheritdoc />
    public async Task AddAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        Contact contact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(contact);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        writeContext.Contacts.Add(ContactMapping.ToEntity(owner, contact));
    }

    /// <inheritdoc />
    public async Task<bool> ReplaceAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        Contact contact,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(contact);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var ownerValue = owner.Value;

        // Tracked rather than projected, so the amendment is applied to the row as it stands now and the concurrency
        // token travels with it: a contact erased between this read and the commit makes the write affect no row, which
        // the session reports as a conflict rather than reinserting the person.
        var held = await writeContext.Contacts
            .Include(record => record.Addresses)
            .FirstOrDefaultAsync(
                record => record.Id == contact.Id.Value && record.OwnerId == ownerValue,
                cancellationToken);

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

        ReplaceAddresses(writeContext, held, owner, contact);

        return true;
    }

    /// <inheritdoc />
    public async Task<ContactErasure> EraseAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        ContactId contactId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var contactValue = contactId.Value;
        var ownerValue = owner.Value;

        // Deleted rather than counted and then cascaded, so the number reported is the number of rows this statement
        // removed. Counting first would report what a separate statement saw, which a write committed between the two
        // makes a different set. The foreign key still cascades and is still what guarantees no address outlives its
        // person; this only takes the same rows first, inside the caller's transaction, to be able to report them.
        var erasedAddresses = await writeContext.ContactAddresses
            .Where(address => address.ContactId == contactValue && address.OwnerId == ownerValue)
            .ExecuteDeleteAsync(cancellationToken);

        var erasedContacts = await writeContext.Contacts
            .Where(record => record.Id == contactValue && record.OwnerId == ownerValue)
            .ExecuteDeleteAsync(cancellationToken);

        return new ContactErasure(contactId, erasedContacts > 0, erasedAddresses);
    }

    /// <inheritdoc />
    public async Task<CollectedContactErasure> EraseCollectedAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        var ownerValue = owner.Value;

        // The addresses go first, and by their contact's origin rather than by a list of identifiers this method read:
        // a set-based delete keeps a book of collected people out of memory, and taking the rows in the same order and
        // the same transaction the single-contact erasure does means the counts reported are the rows removed.
        var collected = writeContext.Contacts
            .Where(record => record.OwnerId == ownerValue && record.Origin == ContactOrigin.Collected);

        var erasedAddresses = await writeContext.ContactAddresses
            .Where(address =>
                address.OwnerId == ownerValue && collected.Any(record => record.Id == address.ContactId))
            .ExecuteDeleteAsync(cancellationToken);

        var erasedContacts = await collected.ExecuteDeleteAsync(cancellationToken);

        return new CollectedContactErasure(erasedContacts, erasedAddresses);
    }

    /// <summary>Brings the stored address rows to exactly the set the amended contact names.</summary>
    /// <remarks>
    /// An address the record still names keeps its row, so the identifier a future derived record could hang on survives
    /// an amendment that only changed the name beside it. What was dropped is deleted rather than left orphaned, which
    /// is also what frees the address for another contact to claim.
    /// </remarks>
    private static void ReplaceAddresses(
        MailFathomDbContext writeContext,
        ContactEntity held,
        MailOwnerId owner,
        Contact contact)
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
            held.Addresses.Add(ContactMapping.ToAddressEntity(owner, contact, added));
        }
    }
}
