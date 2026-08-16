// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Rebuilds the domain record one stored outgoing message and its recipient rows describe.</summary>
internal static class OutgoingMessageRecordMapping
{
    /// <summary>Rebuilds the record a row and its recipients describe.</summary>
    /// <param name="entity">The stored row, with its recipient rows loaded.</param>
    /// <returns>The record that row states.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the row names no recipients or carries an address that no longer parses.</exception>
    internal static OutgoingMessageRecord ToRecord(OutgoingMessageEntity entity)
    {
        // Ordered here rather than trusted from the collection: the recipients are the order a composed message writes
        // its headers in, and a navigation loaded by EF Core carries no order of its own.
        var recipients = entity.Recipients
            .OrderBy(recipient => recipient.Ordinal)
            .Select(recipient => ToOutcome(entity.Id, recipient))
            .ToArray();

        if (recipients.Length == 0)
        {
            throw new InvalidOperationException(
                $"Outgoing message record {entity.Id} names no recipients, so nothing can be offered for it.");
        }

        return new OutgoingMessageRecord
        {
            Id = OutgoingMessageId.Create(entity.Id),
            AccountId = MailAccountId.Create(entity.MailboxAccountId),
            Requester = OutgoingMessageRequester.Create(entity.RequesterOrigin, entity.RequesterIdentity),
            Recipients = recipients,
            Stage = entity.Stage,
            MimeByteLength = entity.MimeByteLength,
            AttemptCount = entity.AttemptCount,
            RecordedAt = entity.RecordedAt,
            StageChangedAt = entity.StageChangedAt,
            LastFailure = ToFailure(entity),
            LastReplyCode = entity.LastReplyCode,
        };
    }

    /// <summary>Restores one recipient and what the server last said about them.</summary>
    /// <remarks>
    /// An address that no longer parses fails the read rather than being dropped. A send offered to fewer people than
    /// it was authored for is a message somebody never receives and nothing afterwards would say so, which is a worse
    /// answer than a record that refuses to be read.
    /// </remarks>
    private static OutgoingRecipientOutcome ToOutcome(Guid recordId, OutgoingMessageRecipientEntity entity)
    {
        if (!EmailAddress.TryCreate(displayName: null, entity.Address, out var address))
        {
            // The address itself stays out of the message: it is personal data, and the ordinal names the row exactly.
            throw new InvalidOperationException(
                $"Outgoing message record {recordId} carries a recipient at position {entity.Ordinal} whose address names no mailbox.");
        }

        return OutgoingRecipientOutcome.Create(
            OutgoingRecipient.Create(address, entity.Role),
            entity.Status,
            entity.LastReplyCode,
            entity.AnsweredAt);
    }

    /// <summary>Reads back the code of the failure the last attempt ended in.</summary>
    /// <remarks>
    /// A number this build does not recognize is a row written by one that allocated a code since. It is diagnostic
    /// detail rather than something acted on, so it is reported as absent instead of failing the read of a send that is
    /// otherwise perfectly readable.
    /// </remarks>
    private static MailFathomErrorCode? ToFailure(OutgoingMessageEntity entity)
    {
        if (entity.LastFailureCode is not { } failureCode)
        {
            return null;
        }

        return MailFathomErrorCode.TryParse(failureCode, out var failure) ? failure : null;
    }
}
