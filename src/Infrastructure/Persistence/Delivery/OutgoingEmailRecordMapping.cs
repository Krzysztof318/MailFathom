// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Rebuilds the domain record one stored outgoing email and its recipient rows describe.</summary>
internal static class OutgoingEmailRecordMapping
{
    /// <summary>Rebuilds the record a row and its recipients describe.</summary>
    /// <param name="entity">The stored row, with its recipient rows loaded.</param>
    /// <returns>The record that row states.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the row names no recipients or carries an address that no longer parses.</exception>
    internal static OutgoingEmailRecord ToRecord(OutgoingEmailEntity entity)
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
                $"Outgoing email record {entity.Id} names no recipients, so nothing can be offered for it.");
        }

        return new OutgoingEmailRecord
        {
            Id = OutgoingEmailId.Create(entity.Id),
            AccountId = MailAccountId.Create(entity.MailboxAccountId),
            Requester = OutgoingEmailRequester.Create(entity.RequesterOrigin, entity.RequesterIdentity),
            Recipients = recipients,
            Stage = entity.Stage,
            MimeByteLength = entity.MimeByteLength,
            AttemptCount = entity.AttemptCount,
            RecordedAt = entity.RecordedAt,
            StageChangedAt = entity.StageChangedAt,
            AvailableAt = entity.AvailableAt,
            LastFailure = ToFailure(entity.LastFailureCode),
            LastReplyCode = entity.LastReplyCode,
            Filings = [.. entity.Filings.Select(ToFiling).OrderBy(filing => filing.Filing.Name, StringComparer.Ordinal)],
            LastFilingFailure = ToFailure(entity.LastFilingFailureCode),
        };
    }

    /// <summary>Rebuilds what one row says about a copy of the message this deployment put into a folder.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the row names a place or a folder this build cannot read.</exception>
    /// <remarks>
    /// A row naming a place this build does not know fails the read rather than being dropped, which is the opposite of
    /// how the failure code beside it is treated and for a reason: a code is diagnostic detail, while a filing that
    /// disappears from the record is a copy in somebody's mailbox that nothing afterwards accounts for — and the next
    /// pass would file a second one.
    /// </remarks>
    internal static OutgoingMailFilingRecord ToFiling(OutgoingEmailFilingEntity entity)
    {
        if (!OutgoingMailFiling.TryParseName(entity.Filing, out var filing))
        {
            throw new InvalidOperationException(
                $"Outgoing email record {entity.OutgoingEmailId} carries a filing named '{entity.Filing}', which is no place this build files into.");
        }

        return new OutgoingMailFilingRecord
        {
            OutgoingEmailId = OutgoingEmailId.Create(entity.OutgoingEmailId),
            Filing = filing,
            FolderAlias = MailFolderAlias.Create(entity.FolderAlias),
            FolderPath = RemoteFolderPath.Create(entity.FolderPath),
            Stage = entity.Stage,
            Placement = ToPlacement(entity),
            InternetMessageId = entity.InternetMessageId,
            AppendedAt = entity.AppendedAt,
            ObservedAt = entity.ObservedAt,
            WithdrawnAt = entity.WithdrawnAt,
        };
    }

    /// <summary>Reads back where the server said it put the copy, which on most servers is nowhere it named.</summary>
    /// <remarks>
    /// The two columns are written together and read together. A row carrying one of them is a row no code path here
    /// can produce, and reading it as a placement would put a UID into a join with no UID space to interpret it in.
    /// </remarks>
    private static RemoteEmailPlacement ToPlacement(OutgoingEmailFilingEntity entity) =>
        entity is { PlacementUidValidity: { } uidValidity, PlacementUid: { } uid }
            ? RemoteEmailPlacement.Reported(ImapUidValidity.Create(uidValidity), ImapUid.Create(uid))
            : RemoteEmailPlacement.NotReported();

    /// <summary>Restores one recipient and what the server last said about them.</summary>
    /// <remarks>
    /// An address that no longer parses fails the read rather than being dropped. A send offered to fewer people than
    /// it was authored for is a message somebody never receives and nothing afterwards would say so, which is a worse
    /// answer than a record that refuses to be read.
    /// </remarks>
    private static OutgoingRecipientOutcome ToOutcome(Guid recordId, OutgoingEmailRecipientEntity entity)
    {
        if (!EmailAddress.TryCreate(displayName: null, entity.Address, out var address))
        {
            // The address itself stays out of the message: it is personal data, and the ordinal names the row exactly.
            throw new InvalidOperationException(
                $"Outgoing email record {recordId} carries a recipient at position {entity.Ordinal} whose address names no mailbox.");
        }

        return OutgoingRecipientOutcome.Create(
            OutgoingRecipient.Create(address, entity.Role, ContactOf(entity)),
            entity.Status,
            entity.LastReplyCode,
            entity.AnsweredAt);
    }

    /// <summary>Reads back which contact the recipient's address was resolved from, where one was.</summary>
    /// <remarks>
    /// An empty identifier is read as no contact rather than failing the read. The value is a record of how the address
    /// came to be on the send and nothing offers the message by it, so a row written with one would cost the read of an
    /// otherwise perfectly deliverable send.
    /// </remarks>
    private static ContactId? ContactOf(OutgoingEmailRecipientEntity entity) =>
        entity.ContactId is { } contactId && contactId != Guid.Empty ? ContactId.Create(contactId) : null;

    /// <summary>Reads back the code of the failure the last attempt ended in.</summary>
    /// <remarks>
    /// A number this build does not recognize is a row written by one that allocated a code since. It is diagnostic
    /// detail rather than something acted on, so it is reported as absent instead of failing the read of a send that is
    /// otherwise perfectly readable.
    /// </remarks>
    private static MailFathomErrorCode? ToFailure(int? storedCode)
    {
        if (storedCode is not { } failureCode)
        {
            return null;
        }

        return MailFathomErrorCode.TryParse(failureCode, out var failure) ? failure : null;
    }
}
