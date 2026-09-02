// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.TestSupport;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps the contact books in memory with the one rule the real store enforces in the database.</summary>
/// <remarks>
/// It is one class behind both ports because it is one book: a test arranging what the directory answers and then
/// asserting what the store kept would otherwise be arranging two halves that could disagree. The rule reproduced is
/// that one address belongs to one contact <em>within one owner's book</em>, which is what every outcome the book
/// publishes turns on and is exactly what the unique index over the owner and the address holds; the session is
/// accepted and unused, because what it guarantees is a transaction and there is none here.
/// </remarks>
/// <remarks>
/// The books are held in one dictionary keyed on the contact's identity alone, because that is the primary key
/// <c>ContactConfiguration</c> declares: <c>(Id, OwnerId)</c> is only an alternate key, so that an address row's
/// foreign key can carry the owner. Keying this double on the pair would let a test arrange one identity in two books
/// and pass, where PostgreSQL would refuse the second row.
/// </remarks>
internal sealed class InMemoryContactBookStore : IContactStore, IContactDirectory
{
    private readonly Dictionary<ContactId, HeldContact> heldById = [];

    /// <summary>Gets how many contacts the books hold between them.</summary>
    internal int ContactCount => this.heldById.Count;

    /// <summary>Gets every contact the books hold, for a test asserting on the records rather than on their number.</summary>
    internal IReadOnlyCollection<Contact> Contacts => [.. this.heldById.Values.Select(held => held.Contact)];

    /// <summary>Gets how many batched lookups the directory has answered, for a test asserting the cost of a read.</summary>
    /// <remarks>
    /// A real book is read by a query, and a caller looking one person up at a time cannot be told apart from one looking
    /// a set up in a single read by what either of them gets back. The number of lookups is the only observation that
    /// separates them, so it is the one this double publishes.
    /// </remarks>
    internal int BatchedLookupCount { get; private set; }

    /// <summary>Puts a contact into the deployment owner's book without going through a write, for arranging what was already held.</summary>
    internal void Hold(Contact contact) => this.Hold(SyntheticMailOwner.Deployment, contact);

    /// <summary>Puts a contact into one owner's book without going through a write.</summary>
    internal void Hold(MailOwnerId owner, Contact contact) => this.heldById[contact.Id] = new HeldContact(owner, contact);

    /// <summary>Gets every contact one owner's book holds, for a test asserting that a book is one person's.</summary>
    internal IReadOnlyCollection<Contact> ContactsOf(MailOwnerId owner) => [.. this.BookOf(owner)];

    /// <inheritdoc />
    public Task AddAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        Contact contact,
        CancellationToken cancellationToken)
    {
        if (this.HolderOf(owner, contact) is { } holder)
        {
            throw new InvalidOperationException($"The address is already held by contact {holder}.");
        }

        this.heldById[contact.Id] = new HeldContact(owner, contact);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ReplaceAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        Contact contact,
        CancellationToken cancellationToken)
    {
        if (this.HeldIn(owner, contact.Id) is null)
        {
            return Task.FromResult(false);
        }

        this.heldById[contact.Id] = new HeldContact(owner, contact);

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<ContactErasure> EraseAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        ContactId contactId,
        CancellationToken cancellationToken)
    {
        if (this.HeldIn(owner, contactId) is not { } held)
        {
            return Task.FromResult(new ContactErasure(contactId, WasHeld: false, AddressesErased: 0));
        }

        this.heldById.Remove(contactId);

        return Task.FromResult(new ContactErasure(contactId, WasHeld: true, held.Addresses.Count));
    }

    /// <inheritdoc />
    public Task<CollectedContactErasure> EraseCollectedAsync(
        IPersistenceSession session,
        MailOwnerId owner,
        CancellationToken cancellationToken)
    {
        var collected = this.BookOf(owner)
            .Where(contact => contact.Origin == ContactOrigin.Collected)
            .ToArray();

        foreach (var contact in collected)
        {
            this.heldById.Remove(contact.Id);
        }

        return Task.FromResult(new CollectedContactErasure(
            collected.Length,
            collected.Sum(contact => contact.Addresses.Count)));
    }

    /// <inheritdoc />
    public Task<Contact?> FindAsync(MailOwnerId owner, ContactId contactId, CancellationToken cancellationToken) =>
        Task.FromResult(this.HeldIn(owner, contactId));

    /// <inheritdoc />
    public Task<Contact?> FindByAddressAsync(
        MailOwnerId owner,
        EmailAddress address,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.BookOf(owner).FirstOrDefault(contact => contact.Holds(address)));

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<ContactId, Contact>> FindAllAsync(
        MailOwnerId owner,
        IReadOnlyCollection<ContactId> contactIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contactIds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            contactIds.Count,
            ContactQuery.MaximumPageSize,
            nameof(contactIds));

        this.BatchedLookupCount++;

        IReadOnlyDictionary<ContactId, Contact> held = contactIds
            .Distinct()
            .Where(contactId => this.HeldIn(owner, contactId) is not null)
            .ToDictionary(contactId => contactId, contactId => this.HeldIn(owner, contactId)!);

        return Task.FromResult(held);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<ContactDisplayName, ContactMatch>> MatchDisplayNamesAsync(
        MailOwnerId owner,
        IReadOnlyCollection<ContactDisplayName> displayNames,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(displayNames);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            displayNames.Count,
            ContactQuery.MaximumPageSize,
            nameof(displayNames));

        this.BatchedLookupCount++;

        IReadOnlyDictionary<ContactDisplayName, ContactMatch> matches = displayNames
            .Distinct()
            .ToDictionary(displayName => displayName, displayName => this.MatchOf(owner, displayName));

        return Task.FromResult(matches);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<EmailAddress, ContactId>> FindHoldersOfAsync(
        MailOwnerId owner,
        IReadOnlyCollection<EmailAddress> addresses,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            addresses.Count,
            Contact.MaximumAddressCount,
            nameof(addresses));

        this.BatchedLookupCount++;

        IReadOnlyDictionary<EmailAddress, ContactId> holders = addresses
            .Where(address => this.BookOf(owner).Any(contact => contact.Holds(address)))
            .Distinct()
            .ToDictionary(
                address => address,
                address => this.BookOf(owner).First(contact => contact.Holds(address)).Id);

        return Task.FromResult(holders);
    }

    /// <inheritdoc />
    public Task<ContactPage> ReadPageAsync(
        MailOwnerId owner,
        ContactQuery query,
        CancellationToken cancellationToken)
    {
        var ordered = this.BookOf(owner)
            .Where(contact => query.Origin is not { } origin || contact.Origin == origin)
            .OrderBy(contact => contact.DisplayName.SortKey, StringComparer.Ordinal)
            .ThenBy(contact => contact.Id.Value)
            .Where(contact => query.Cursor is not { } cursor || IsBeyond(contact, cursor))
            .Take(query.PageSize + 1)
            .ToArray();

        var page = ordered.Take(query.PageSize).ToArray();

        return Task.FromResult(new ContactPage(
            page,
            ordered.Length > query.PageSize && page.Length > 0
                ? ContactCursor.After(page[^1].DisplayName, page[^1].Id)
                : null));
    }

    private static bool IsBeyond(Contact contact, ContactCursor cursor)
    {
        var byName = string.CompareOrdinal(contact.DisplayName.SortKey, cursor.DisplayNameSortKey);

        return byName > 0 || (byName == 0 && contact.Id.Value > cursor.ContactId.Value);
    }

    /// <summary>Reads one owner's book, which is the whole of what any port method here may see.</summary>
    private IEnumerable<Contact> BookOf(MailOwnerId owner) =>
        this.heldById.Values
            .Where(held => held.Owner == owner)
            .Select(held => held.Contact);

    /// <summary>Reads one contact of one book, answering with nothing where the identity is filed under another owner.</summary>
    private Contact? HeldIn(MailOwnerId owner, ContactId contactId) =>
        this.heldById.TryGetValue(contactId, out var held) && held.Owner == owner ? held.Contact : null;

    /// <summary>One person, and the book they are filed in.</summary>
    private sealed record HeldContact(MailOwnerId Owner, Contact Contact);

    /// <summary>States who one name resolves to in one book, on the comparison form the listing index is built on.</summary>
    private ContactMatch MatchOf(MailOwnerId owner, ContactDisplayName displayName)
    {
        var carrying = this.BookOf(owner)
            .Where(contact => string.Equals(
                contact.DisplayName.SortKey,
                displayName.SortKey,
                StringComparison.Ordinal))
            .ToArray();

        return carrying.Length switch
        {
            0 => ContactMatch.None,
            1 => ContactMatch.Unique(carrying[0]),
            _ => ContactMatch.Several(carrying.Length),
        };
    }

    /// <summary>Names the other contact in this owner's book already holding one of this record's addresses, as the unique index would.</summary>
    private ContactId? HolderOf(MailOwnerId owner, Contact contact)
    {
        var holders = this.BookOf(owner)
            .Where(held => held.Id != contact.Id && contact.Addresses.Any(held.Holds))
            .ToArray();

        return holders.Length == 0 ? null : holders[0].Id;
    }
}
