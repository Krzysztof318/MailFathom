// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;
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
    /// <remarks>The address and the contact are read as <see cref="StoredOutgoingRecipient" /> states.</remarks>
    private static OutgoingRecipient ToRecipient(Guid declarationId, RecurringSendRecipientEntity entity) =>
        StoredOutgoingRecipient.ToRecipient(
            "Recurring send",
            declarationId,
            entity.Ordinal,
            entity.Address,
            entity.Role,
            entity.ContactId);

    /// <summary>Reads back the message the last occasion produced, where there has been one.</summary>
    /// <remarks>
    /// An empty identifier is read as no occurrence rather than failing the read. The value decides whether this
    /// declaration stands down for one round, and a row written with one would otherwise stop a repetition for good.
    /// </remarks>
    private static OutgoingEmailId? ToOccurrence(Guid? storedId) =>
        storedId is { } occurrenceId && occurrenceId != Guid.Empty ? OutgoingEmailId.Create(occurrenceId) : null;
}
