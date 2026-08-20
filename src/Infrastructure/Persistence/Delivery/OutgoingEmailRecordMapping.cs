// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Scheduling;
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
            Principal = ToPrincipal(entity.Id, entity.PrincipalFingerprint),
            Recipients = recipients,
            Stage = entity.Stage,
            MimeByteLength = entity.MimeByteLength,
            AttemptCount = entity.AttemptCount,
            RecordedAt = entity.RecordedAt,
            StageChangedAt = entity.StageChangedAt,
            AvailableAt = entity.AvailableAt,
            DueAt = ToDueTime(entity),
            LastFailure = StoredFailureCode.ToErrorCode(entity.LastFailureCode),
            LastReplyCode = entity.LastReplyCode,
            Filings = [.. entity.Filings.Select(ToFiling).OrderBy(filing => filing.Filing.Name, StringComparer.Ordinal)],
            LastFilingFailure = StoredFailureCode.ToErrorCode(entity.LastFilingFailureCode),
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
            Placement = StoredRemotePlacement.Of(entity.PlacementUidValidity, entity.PlacementUid),
            InternetMessageId = entity.InternetMessageId,
            AppendedAt = entity.AppendedAt,
            ObservedAt = entity.ObservedAt,
            WithdrawnAt = entity.WithdrawnAt,
        };
    }

    /// <summary>Restores one recipient and what the server last said about them.</summary>
    /// <remarks>The address and the contact are read as <see cref="StoredOutgoingRecipient" /> states.</remarks>
    private static OutgoingRecipientOutcome ToOutcome(Guid recordId, OutgoingEmailRecipientEntity entity) =>
        OutgoingRecipientOutcome.Create(
            StoredOutgoingRecipient.ToRecipient(
                "Outgoing email record",
                recordId,
                entity.Ordinal,
                entity.Address,
                entity.Role,
                entity.ContactId),
            entity.Status,
            entity.LastReplyCode,
            entity.AnsweredAt);

    /// <summary>Reads back the time the message was written to leave at, where one was named.</summary>
    /// <remarks>
    /// The two columns are written together and read together, and a row carrying an instant with no zone is read as
    /// naming no due time at all. The zone is what makes the instant answerable — which nine in the morning was meant —
    /// so reading such a row as a due time would state a moment nobody can check against the schedule it came from.
    /// </remarks>
    private static ZonedInstant? ToDueTime(OutgoingEmailEntity entity) =>
        entity is { DueAt: { } dueAt, DueZoneId: { Length: > 0 } zoneId }
            ? ZonedInstant.Restore(dueAt, zoneId)
            : null;

    /// <summary>Reads back whoever asked for the send, where the row says.</summary>
    /// <remarks>
    /// A row written before the column existed reads as nobody, which matches no caller and so keeps such a send out of
    /// every caller's reach. A stored value that is not a fingerprint this system writes fails the read instead: the
    /// value's only use is that two of them are equal, so serving one that can never match would hide a send from
    /// exactly the caller entitled to it.
    /// </remarks>
    private static OutgoingEmailPrincipal? ToPrincipal(Guid recordId, string? fingerprint)
    {
        if (string.IsNullOrEmpty(fingerprint))
        {
            return null;
        }

        try
        {
            return OutgoingEmailPrincipal.Create(fingerprint);
        }
        catch (ArgumentException malformed)
        {
            // Named the way every other corruption this file refuses to read is named, so an operator reaches the row
            // rather than the constraint. The fingerprint itself stays out of the message: it stands for a person.
            throw new InvalidOperationException(
                $"Outgoing email record {recordId} carries a principal fingerprint this system did not write.",
                malformed);
        }
    }
}
