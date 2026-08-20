// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Rebuilds the domain record one stored draft, its recipients, and its copies describe.</summary>
internal static class MailDraftRecordMapping
{
    /// <summary>Rebuilds the draft a row and everything under it states.</summary>
    /// <param name="entity">The stored row, with its recipient and copy rows loaded.</param>
    /// <returns>The draft that row states.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the row carries an address that no longer parses.</exception>
    /// <remarks>
    /// A draft naming nobody is read as an ordinary draft rather than refused, which is the one place this differs from
    /// the outgoing record's mapping: writing the message before deciding who reads it is what a draft is for, and the
    /// absence is refused where a promotion asks for an envelope.
    /// </remarks>
    internal static MailDraftRecord ToRecord(MailDraftEntity entity)
    {
        // Ordered here rather than trusted from the collection: the recipients are the order the composed message
        // writes its headers in, and a navigation loaded by EF Core carries no order of its own.
        var recipients = entity.Recipients
            .OrderBy(recipient => recipient.Ordinal)
            .Select(recipient => ToRecipient(entity.Id, recipient))
            .ToArray();

        return new MailDraftRecord
        {
            Id = MailDraftId.Create(entity.Id),
            AccountId = MailAccountId.Create(entity.MailboxAccountId),
            Author = OutgoingEmailRequester.Create(entity.RequesterOrigin, entity.RequesterIdentity),
            Recipients = recipients,
            MimeByteLength = entity.MimeByteLength,
            Revision = entity.Revision,
            ComposedAt = entity.ComposedAt,
            RevisedAt = entity.RevisedAt,
            DiscardedAt = entity.DiscardedAt,
            PromotedTo = entity.PromotedToOutgoingEmailId is { } promoted
                ? OutgoingEmailId.Create(promoted)
                : null,
            Copies = [.. entity.Copies.Select(ToCopy).OrderByDescending(copy => copy.Revision)],
            Divergence = ToDivergence(entity),
            LastFailure = ToFailure(entity.LastFailureCode),
        };
    }

    /// <summary>Rebuilds what one row says about a copy of the draft this deployment put into a folder.</summary>
    internal static MailDraftServerCopy ToCopy(MailDraftCopyEntity entity) =>
        new()
        {
            Revision = entity.Revision,
            FolderAlias = MailFolderAlias.Create(entity.FolderAlias),
            FolderPath = RemoteFolderPath.Create(entity.FolderPath),
            Stage = entity.Stage,
            Placement = ToPlacement(entity),
            InternetMessageId = entity.InternetMessageId,
            AppendedAt = entity.AppendedAt,
            SettledAt = entity.SettledAt,
        };

    /// <summary>Reads back where the server said it put the copy, which on most servers is nowhere it named.</summary>
    /// <remarks>
    /// The two columns are written together and read together. A row carrying one of them is a row no code path here
    /// can produce, and reading it as a placement would name a UID with no UID space to interpret it in — which is the
    /// one thing a removal must never do.
    /// </remarks>
    private static RemoteEmailPlacement ToPlacement(MailDraftCopyEntity entity) =>
        entity is { PlacementUidValidity: { } uidValidity, PlacementUid: { } uid }
            ? RemoteEmailPlacement.Reported(ImapUidValidity.Create(uidValidity), ImapUid.Create(uid))
            : RemoteEmailPlacement.NotReported();

    /// <summary>Reads back why the tracked copy stopped being one MailFathom may touch.</summary>
    /// <remarks>
    /// Both columns are written together, so a row carrying one of them describes a divergence this build cannot state
    /// and is read as none. The copy rows beside it are what keep the message reachable either way.
    /// </remarks>
    private static MailDraftDivergence? ToDivergence(MailDraftEntity entity) =>
        entity is { DivergenceReason: { } reason, DivergenceObservedAt: { } observedAt }
            ? new MailDraftDivergence(reason, observedAt)
            : null;

    /// <summary>Restores one person the draft is addressed to.</summary>
    /// <remarks>
    /// An address that no longer parses fails the read rather than being dropped, for the reason a send's does: a draft
    /// promoted with fewer recipients than its author wrote is a person who never receives the message and is told
    /// nothing about it.
    /// </remarks>
    private static OutgoingRecipient ToRecipient(Guid draftId, MailDraftRecipientEntity entity)
    {
        if (!EmailAddress.TryCreate(displayName: null, entity.Address, out var address))
        {
            // The address itself stays out of the message: it is personal data, and the ordinal names the row exactly.
            throw new InvalidOperationException(
                $"Mail draft {draftId} carries a recipient at position {entity.Ordinal} whose address names no mailbox.");
        }

        return OutgoingRecipient.Create(address, entity.Role, ContactOf(entity));
    }

    /// <summary>Reads back which contact the address was resolved from, where one was.</summary>
    /// <remarks>An empty identifier is read as no contact, for the reason a send's is: the value records how the
    /// address came to be on the draft and nothing addresses anybody by it.</remarks>
    private static ContactId? ContactOf(MailDraftRecipientEntity entity) =>
        entity.ContactId is { } contactId && contactId != Guid.Empty ? ContactId.Create(contactId) : null;

    /// <summary>Reads back the code of the failure the last attempt on the mailbox ended in.</summary>
    /// <remarks>
    /// A number this build does not recognize is a row written by one that allocated a code since. It is diagnostic
    /// detail rather than something acted on, so it is reported as absent instead of failing the read of a draft that
    /// is otherwise perfectly readable.
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
