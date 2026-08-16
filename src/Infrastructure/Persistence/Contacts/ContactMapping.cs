// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Contacts;

/// <summary>Turns one contact into the rows that keep it, and back.</summary>
/// <remarks>
/// The domain type is what the invariants live in, so this mapping never repairs a row it disagrees with: rebuilding a
/// contact goes through <see cref="Contact.Create" /> exactly as a write does, and a stored shape the domain refuses
/// fails here rather than becoming a record no writer could have produced.
/// </remarks>
internal static class ContactMapping
{
    /// <summary>Builds the rows one contact is kept as.</summary>
    /// <param name="contact">The contact to keep.</param>
    /// <returns>The row to insert, with its address rows already attached.</returns>
    internal static ContactEntity ToEntity(Contact contact)
    {
        var entity = new ContactEntity
        {
            Id = contact.Id.Value,
            DisplayName = contact.DisplayName.Value,
            DisplayNameSortKey = contact.DisplayName.SortKey,
            PreferredNormalizedAddress = contact.PreferredAddress.NormalizedAddress,
            Note = contact.Note?.Value,
            Origin = contact.Origin,
            RecordedAt = contact.RecordedAt,
            AmendedAt = contact.AmendedAt,
        };

        foreach (var address in contact.Addresses)
        {
            entity.Addresses.Add(ToAddressEntity(contact, address));
        }

        return entity;
    }

    /// <summary>Builds the row one of a contact's addresses is kept as.</summary>
    /// <param name="contact">The contact the address belongs to.</param>
    /// <param name="address">The address to keep.</param>
    /// <returns>The address row.</returns>
    internal static ContactAddressEntity ToAddressEntity(Contact contact, EmailAddress address) =>
        new()
        {
            // Version 7 over the contact's own arrival rather than a random value, so a contact's address rows are
            // clustered the way every other identifier this system mints is. Nothing reads the ordering between them.
            Id = Guid.CreateVersion7(contact.RecordedAt),
            ContactId = contact.Id.Value,
            Address = address.Address,
            NormalizedAddress = address.NormalizedAddress,
        };

    /// <summary>Rebuilds the contact one stored row states.</summary>
    /// <param name="entity">The stored row, with its addresses loaded.</param>
    /// <returns>The contact that row states.</returns>
    /// <exception cref="ArgumentException">Thrown when the stored rows do not form a contact the domain admits — no address, or a preferred address the contact does not hold.</exception>
    /// <remarks>
    /// A row naming a preferred address that is not among the contact's cannot be repaired by picking another, because
    /// which address a person's mail goes to by default is the owner's choice and inventing it would send mail somewhere
    /// nobody chose. It is the one part of the record no constraint can hold, which is why it is refused here.
    /// </remarks>
    internal static Contact ToContact(ContactEntity entity)
    {
        var addresses = entity.Addresses
            .Select(ToAddress)
            .ToArray();

        var preferred = addresses
            .Where(address => string.Equals(
                address.NormalizedAddress,
                entity.PreferredNormalizedAddress,
                StringComparison.Ordinal))
            .ToArray();

        if (preferred.Length != 1)
        {
            throw new ArgumentException("A stored contact names one preferred address it holds.", nameof(entity));
        }

        return Contact.Create(
            ContactId.Create(entity.Id),
            ContactDisplayName.Create(entity.DisplayName),
            addresses,
            preferred[0],
            entity.Note is null ? null : ContactNote.Create(entity.Note),
            entity.Origin,
            entity.RecordedAt,
            entity.AmendedAt);
    }

    /// <summary>Rebuilds one stored address, which the write path already validated.</summary>
    private static EmailAddress ToAddress(ContactAddressEntity entity) =>
        EmailAddress.TryCreate(displayName: null, entity.Address, out var address)
            ? address
            : throw new ArgumentException("A stored contact address is not a usable address.", nameof(entity));
}
