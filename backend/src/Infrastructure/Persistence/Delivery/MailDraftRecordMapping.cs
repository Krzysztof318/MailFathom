// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Folders;
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
            Account = MailAccountIdentity.Create(
                MailOwnerId.Create(entity.OwnerId),
                MailAccountId.Create(entity.MailboxAccountId)),
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
            LastFailure = StoredFailureCode.ToErrorCode(entity.LastFailureCode),
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
            Placement = StoredRemotePlacement.Of(entity.PlacementUidValidity, entity.PlacementUid),
            InternetMessageId = entity.InternetMessageId,
            AppendedAt = entity.AppendedAt,
            SettledAt = entity.SettledAt,
        };

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
    /// The address and the contact are read as <see cref="StoredOutgoingRecipient" /> states. The provenance beside
    /// them is the draft's own column: a send meets the governance before its row exists, while a draft is written
    /// first and governed when it is promoted, so how each address came to be on it has to survive the wait.
    /// </remarks>
    private static MailDraftRecipient ToRecipient(Guid draftId, MailDraftRecipientEntity entity) =>
        new(
            StoredOutgoingRecipient.ToRecipient(
                "Mail draft",
                draftId,
                entity.Ordinal,
                entity.Address,
                entity.Role,
                entity.ContactId),
            entity.Provenance);
}
