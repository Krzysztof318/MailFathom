// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps a contact book in memory with the one rule the real store enforces in the database.</summary>
/// <remarks>
/// It is one class behind both ports because it is one book: a test arranging what the directory answers and then
/// asserting what the store kept would otherwise be arranging two halves that could disagree. The rule reproduced is
/// that one address belongs to one contact, which is what every outcome the book publishes turns on; the session is
/// accepted and unused, because what it guarantees is a transaction and there is none here.
/// </remarks>
internal sealed class InMemoryContactBookStore : IContactStore, IContactDirectory
{
    private readonly Dictionary<ContactId, Contact> contactsById = [];

    /// <summary>Gets how many contacts the book holds.</summary>
    internal int ContactCount => this.contactsById.Count;

    /// <summary>Puts a contact into the book without going through a write, for arranging what was already held.</summary>
    internal void Hold(Contact contact) => this.contactsById[contact.Id] = contact;

    /// <inheritdoc />
    public Task AddAsync(IPersistenceSession session, Contact contact, CancellationToken cancellationToken)
    {
        if (this.HolderOf(contact) is { } holder)
        {
            throw new InvalidOperationException($"The address is already held by contact {holder}.");
        }

        this.contactsById[contact.Id] = contact;

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ReplaceAsync(IPersistenceSession session, Contact contact, CancellationToken cancellationToken)
    {
        if (!this.contactsById.ContainsKey(contact.Id))
        {
            return Task.FromResult(false);
        }

        this.contactsById[contact.Id] = contact;

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<ContactErasure> EraseAsync(
        IPersistenceSession session,
        ContactId contactId,
        CancellationToken cancellationToken)
    {
        if (!this.contactsById.TryGetValue(contactId, out var held))
        {
            return Task.FromResult(new ContactErasure(contactId, WasHeld: false, AddressesErased: 0));
        }

        this.contactsById.Remove(contactId);

        return Task.FromResult(new ContactErasure(contactId, WasHeld: true, held.Addresses.Count));
    }

    /// <inheritdoc />
    public Task<Contact?> FindAsync(ContactId contactId, CancellationToken cancellationToken) =>
        Task.FromResult(this.contactsById.GetValueOrDefault(contactId));

    /// <inheritdoc />
    public Task<Contact?> FindByAddressAsync(EmailAddress address, CancellationToken cancellationToken) =>
        Task.FromResult(this.contactsById.Values.FirstOrDefault(contact => contact.Holds(address)));

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<EmailAddress, ContactId>> FindHoldersOfAsync(
        IReadOnlyCollection<EmailAddress> addresses,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<EmailAddress, ContactId> holders = addresses
            .Where(address => this.contactsById.Values.Any(contact => contact.Holds(address)))
            .Distinct()
            .ToDictionary(
                address => address,
                address => this.contactsById.Values.First(contact => contact.Holds(address)).Id);

        return Task.FromResult(holders);
    }

    /// <inheritdoc />
    public Task<ContactPage> ReadPageAsync(ContactQuery query, CancellationToken cancellationToken)
    {
        var ordered = this.contactsById.Values
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

    /// <summary>Names the other contact already holding one of this record's addresses, as the unique index would.</summary>
    private ContactId? HolderOf(Contact contact)
    {
        var holders = this.contactsById.Values
            .Where(held => held.Id != contact.Id && contact.Addresses.Any(held.Holds))
            .ToArray();

        return holders.Length == 0 ? null : holders[0].Id;
    }
}
