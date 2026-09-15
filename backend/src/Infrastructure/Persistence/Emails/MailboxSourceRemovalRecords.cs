// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Records where the source still holds each message an erasure is about to remove locally.</summary>
/// <remarks>
/// <para>
/// The erasure deletes the only row that said where the message was on the source, so the drain would otherwise have
/// nothing left to expunge and the source would keep a copy of mail MailFathom was asked to destroy. The record is
/// written in the erasing transaction rather than issued from it, because a request to erase mail must not wait on a
/// mail server and must not fail because one is unreachable.
/// </para>
/// <para>
/// A row whose occurrence was already cleared has nothing on the source to name, and writes nothing. That is every
/// message of a mailbox the drain has already emptied, which is why an account far into holding pays nothing here.
/// </para>
/// <para>
/// It is shared rather than written per store because every path that erases a stored row owes it: an erased folder's
/// mail, one message erased by identity, and the delete a person authored in the client whose trash window has passed.
/// A path that wrote its own copy is a path that would stop matching the others without anything saying so.
/// </para>
/// </remarks>
internal static class MailboxSourceRemovalRecords
{
    /// <summary>Stages one record per erased row the source still holds an occurrence of.</summary>
    /// <param name="sessionContext">The context the erasure is committing through.</param>
    /// <param name="erased">The rows the erasure is about to remove, tracked or read in the same transaction.</param>
    /// <param name="recordedAt">When the erasure was asked for, which the drain reports rather than reacts to.</param>
    internal static void Stage(
        MailFathomDbContext sessionContext,
        IReadOnlyList<StoredEmailEntity> erased,
        DateTimeOffset recordedAt) =>
        sessionContext.MailboxSourceRemovals.AddRange(erased
            .Where(static row => row.UidValidity is not null && row.Uid is not null)
            .Select(row => new MailboxSourceRemovalEntity
            {
                Id = Guid.CreateVersion7(),
                MailboxAccountId = row.MailboxAccountId,
                MailFolderId = row.MailFolderId,
                UidValidity = row.UidValidity!.Value,
                Uid = row.Uid!.Value,
                RecordedAt = recordedAt,
            }));
}
