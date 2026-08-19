// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Rebuilds the declaration one stored recurring send and its recipient rows describe.</summary>
internal static class RecurringSendMapping
{
    /// <summary>Rebuilds the declaration a row and its recipients describe.</summary>
    /// <param name="entity">The stored row, with its recipient rows loaded.</param>
    /// <returns>The declaration that row states.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the row names no recipients or carries an address that no longer parses.</exception>
    internal static RecurringSend ToDeclaration(RecurringSendEntity entity)
    {
        // Ordered here rather than trusted from the collection: the recipients are the order every occasion's composed
        // message writes its headers in, and a navigation loaded by EF Core carries no order of its own.
        var recipients = entity.Recipients
            .OrderBy(recipient => recipient.Ordinal)
            .Select(recipient => ToRecipient(entity.Id, recipient))
            .ToArray();

        if (recipients.Length == 0)
        {
            throw new InvalidOperationException(
                $"Recurring send {entity.Id} names no recipients, so no occasion of it could be offered to anybody.");
        }

        return new RecurringSend
        {
            Id = RecurringSendId.Create(entity.Id),
            AccountId = MailAccountId.Create(entity.MailboxAccountId),
            Requester = OutgoingEmailRequester.Create(entity.RequesterOrigin, entity.RequesterIdentity),
            Recipients = recipients,
            Schedule = entity.Schedule,
            DraftByteLength = entity.DraftByteLength,
            DeclaredAt = entity.DeclaredAt,
            CancelledAt = entity.CancelledAt,
            LastOccurrenceAt = entity.LastOccurrenceAt,
            LastOccurrenceEmailId = ToOccurrence(entity.LastOccurrenceEmailId),
        };
    }

    /// <summary>Restores one recipient every occasion of the declaration is offered to.</summary>
    /// <remarks>
    /// An address that no longer parses fails the read rather than being dropped, for the reason an outgoing record's
    /// does: a repetition offered to fewer people than it was declared for is somebody who stops receiving mail with
    /// nothing afterwards saying so.
    /// </remarks>
    private static OutgoingRecipient ToRecipient(Guid declarationId, RecurringSendRecipientEntity entity)
    {
        if (!EmailAddress.TryCreate(displayName: null, entity.Address, out var address))
        {
            // The address itself stays out of the message: it is personal data, and the ordinal names the row exactly.
            throw new InvalidOperationException(
                $"Recurring send {declarationId} carries a recipient at position {entity.Ordinal} whose address names no mailbox.");
        }

        return OutgoingRecipient.Create(address, entity.Role, ContactOf(entity));
    }

    /// <summary>Reads back which contact the recipient's address was resolved from, where one was.</summary>
    /// <remarks>An empty identifier is read as no contact rather than failing the read, exactly as it is on an outgoing record: nothing addresses anybody by it.</remarks>
    private static ContactId? ContactOf(RecurringSendRecipientEntity entity) =>
        entity.ContactId is { } contactId && contactId != Guid.Empty ? ContactId.Create(contactId) : null;

    /// <summary>Reads back the message the last occasion produced, where there has been one.</summary>
    /// <remarks>
    /// An empty identifier is read as no occurrence rather than failing the read. The value decides whether this
    /// declaration stands down for one round, and a row written with one would otherwise stop a repetition for good.
    /// </remarks>
    private static OutgoingEmailId? ToOccurrence(Guid? storedId) =>
        storedId is { } occurrenceId && occurrenceId != Guid.Empty ? OutgoingEmailId.Create(occurrenceId) : null;
}
