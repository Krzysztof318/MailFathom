// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Mail.Delivery.Drafts;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Delivery;

/// <summary>Keeps, in PostgreSQL, the account of every draft this deployment holds and every copy of one it appended.</summary>
/// <remarks>
/// <para>
/// The writes use the context enlisted in the caller's session, so a revision and the message it is a revision of
/// commit together, and a row saying a copy may be in a folder is only ever durable inside the transaction that decided
/// to put it there. The reads use the scoped context, because they join no transaction.
/// </para>
/// <para>
/// <see cref="RecordDivergenceAsync" /> and <see cref="RecordFailureAsync" /> are the exception, and are given no
/// session by the port for that reason. Each writes a column beside a draft whose author may be editing it in another
/// request, and neither says anything a revision has to be rolled back over.
/// </para>
/// <para>
/// The append is not guarded by a read-then-write. The copy's key is the draft and the revision together, so appending
/// one revision twice is refused by the database rather than by a check two callers can pass between — and a second
/// copy in the owner's drafts folder is a draft they read as two.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class MailDraftStore(MailFathomDbContext readContext) : IMailDraftStore
{
    /// <summary>The revision a draft's first stored message is, which every later edit counts up from.</summary>
    private const int FirstRevision = 1;

    /// <inheritdoc />
    public async Task<MailDraftRecord> OpenAsync(
        IPersistenceSession session,
        MailAccountIdentity account,
        OutgoingEmailRequester author,
        IReadOnlyList<MailDraftRecipient> recipients,
        string subject,
        long mimeByteLength,
        DateTimeOffset composedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(author);
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mimeByteLength);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        var entity = new MailDraftEntity
        {
            Id = Guid.CreateVersion7(composedAt),
            MailboxAccountId = account.Id.Value,

            // Written from the identity the caller's own catalog resolved, which is the account this draft belongs to.
            OwnerId = account.Owner.Value,
            RequesterOrigin = author.Origin,
            RequesterIdentity = author.Identity,
            Subject = subject,
            Revision = FirstRevision,
            MimeByteLength = mimeByteLength,
            ComposedAt = composedAt,
            RevisedAt = composedAt,
        };

        AddRecipients(entity, recipients);

        writeContext.MailDrafts.Add(entity);

        return MailDraftRecordMapping.ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task<MailDraftRecord> ReviseAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        IReadOnlyList<MailDraftRecipient> recipients,
        string subject,
        long mimeByteLength,
        DateTimeOffset revisedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(mimeByteLength);

        var entity = await RequireAsync(session, draftId, cancellationToken);

        if (entity.DiscardedAt is not null)
        {
            throw new InvalidOperationException(
                $"Mail draft {draftId} has been given up, so nothing revises it.");
        }

        // The revision advances before any command reaches the mail server, which is the whole of what makes a
        // replacement resumable: the row already says which copy is being replaced, so a process that dies between the
        // append and the removal leaves one draft in the folder rather than two or none.
        entity.Revision++;
        entity.MimeByteLength = mimeByteLength;
        entity.Subject = subject;
        entity.RevisedAt = revisedAt;

        // Replaced outright rather than amended, so the list is the composed message's own rather than an accumulation
        // of everybody the draft was ever addressed to.
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);
        writeContext.MailDraftRecipients.RemoveRange(entity.Recipients);
        entity.Recipients.Clear();
        AddRecipients(entity, recipients);

        return MailDraftRecordMapping.ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task<MailDraftRecord?> FindAsync(MailDraftId draftId, CancellationToken cancellationToken)
    {
        var entity = await this.ReadDrafts()
            .SingleOrDefaultAsync(draft => draft.Id == draftId.Value, cancellationToken);

        return entity is null ? null : MailDraftRecordMapping.ToRecord(entity);
    }

    /// <inheritdoc />
    public async Task<MailDraftRecord?> FindPromotedToAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var entity = await this.ReadDrafts()
            .SingleOrDefaultAsync(
                draft => draft.PromotedToOutgoingEmailId == outgoingEmailId.Value,
                cancellationToken);

        return entity is null ? null : MailDraftRecordMapping.ToRecord(entity);
    }

    /// <inheritdoc />
    /// <remarks>
    /// What the domain reads off the copies is asked of the database here rather than by materializing every draft and
    /// filtering afterwards, so an account whose drafts are all settled costs one bounded query. The four cases are
    /// the ones <see cref="MailDraftRecord.HasOutstandingServerWork" /> names: a draft given up owes a removal whatever
    /// else is true of it, a promoted draft owes its own give-up until delivery writes one, a revision nobody has
    /// appended owes an append, and a superseded copy still standing owes a removal. An append the server never
    /// answered is deliberately none of them — nothing appends it again, and nothing can remove what nobody can name.
    /// </remarks>
    public async Task<IReadOnlyList<MailDraftRecord>> ReadOutstandingAsync(
        MailAccountIdentity account,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);

        var ownerValue = account.Owner.Value;
        var accountValue = account.Id.Value;

        var entities = await this.ReadDrafts()
            .Where(draft => draft.OwnerId == ownerValue
                && draft.MailboxAccountId == accountValue
                && (draft.DiscardedAt != null
                    || draft.PromotedToOutgoingEmailId != null
                    || (!draft.Copies.Any(copy => copy.Stage == MailDraftCopyStage.Issued)
                        && (!draft.Copies.Any(copy => copy.Revision == draft.Revision)
                            || draft.Copies.Any(copy => copy.Revision != draft.Revision
                                && copy.Stage == MailDraftCopyStage.Standing)))))
            .OrderBy(draft => draft.RevisedAt)
            .ThenBy(draft => draft.Id)
            .Take(maxCount)
            .ToArrayAsync(cancellationToken);

        return [.. entities.Select(MailDraftRecordMapping.ToRecord)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Narrowed on the owner first and the account second, which is the order the index over this table leads with, so
    /// an owner's listing is a range scan whether or not it names an account. Drafts on their way out are left out at
    /// the database rather than filtered afterwards: what a person means by their drafts is what they can still edit.
    /// </remarks>
    public async Task<IReadOnlyList<MailDraftRecord>> ReadForOwnerAsync(
        MailOwnerId owner,
        MailAccountId? account,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);

        var ownerValue = owner.Value;
        var accountValue = account?.Value;

        var entities = await this.ReadDrafts()
            .Where(draft => draft.OwnerId == ownerValue
                && (accountValue == null || draft.MailboxAccountId == accountValue)
                && draft.DiscardedAt == null
                && draft.PromotedToOutgoingEmailId == null)
            .OrderByDescending(draft => draft.RevisedAt)
            .ThenBy(draft => draft.Id)
            .Take(maxCount)
            .ToArrayAsync(cancellationToken);

        return [.. entities.Select(MailDraftRecordMapping.ToRecord)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The one read here that joins the payload table, and it projects rather than materializing the entity so nothing
    /// but the three values a composition needs is loaded. The octets are copied into memory the composer owns,
    /// because the array EF Core materialized is this query's and the message is built after the context is done.
    /// </remarks>
    public async Task<IReadOnlyList<AuthoredEmailAttachment>> ReadAttachmentContentAsync(
        MailDraftId draftId,
        CancellationToken cancellationToken)
    {
        var files = await readContext.MailDraftAttachments
            .AsNoTracking()
            .Where(attachment => attachment.MailDraftId == draftId.Value && attachment.Content != null)
            .OrderBy(attachment => attachment.StagedAt)
            .ThenBy(attachment => attachment.Id)
            .Select(attachment => new
            {
                attachment.FileName,
                attachment.MediaType,
                Content = attachment.Content!.Content,
            })
            .ToArrayAsync(cancellationToken);

        return [.. files.Select(file => new AuthoredEmailAttachment(
            file.FileName,
            file.MediaType,
            file.Content))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The description and the octets are added as one graph through the draft's own collection, so a file is never
    /// committed against a draft that is not there and its payload is never committed without the row that says what
    /// it is called.
    /// </remarks>
    public async Task<MailDraftAttachment> StageAttachmentAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        AuthoredEmailAttachment file,
        DateTimeOffset stagedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(file);

        var entity = await RequireAsync(session, draftId, cancellationToken);

        // Moved so the draft's own row takes part in the write, which is what makes two uploads racing each other
        // conflict: adding a child row alone touches no concurrency token, so both would commit and the count bound
        // the application checked would be exceeded by exactly the pair that never saw each other. It is also what an
        // attachment is — a listing orders drafts by when each last changed, and attaching a file changed this one.
        entity.RevisedAt = stagedAt;

        var attachment = new MailDraftAttachmentEntity
        {
            Id = Guid.CreateVersion7(stagedAt),
            MailDraftId = entity.Id,
            MailDraft = entity,
            FileName = file.FileName,
            MediaType = file.MediaType,
            ByteLength = file.Content.Length,
            StagedAt = stagedAt,
        };

        attachment.Content = new MailDraftAttachmentContentEntity
        {
            MailDraftAttachmentId = attachment.Id,
            Attachment = attachment,
            Content = file.Content.ToArray(),
        };

        entity.Attachments.Add(attachment);

        return MailDraftRecordMapping.ToAttachment(attachment);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The draft is named beside the file, so an identifier belonging to another draft removes nothing rather than
    /// removing that draft's file. The octets go by the cascade declared on the payload's own foreign key.
    /// </remarks>
    public async Task<bool> UnstageAttachmentAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        MailDraftAttachmentId attachmentId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        var attachment = await writeContext.MailDraftAttachments
            .SingleOrDefaultAsync(
                candidate => candidate.Id == attachmentId.Value && candidate.MailDraftId == draftId.Value,
                cancellationToken);

        if (attachment is null)
        {
            return false;
        }

        writeContext.MailDraftAttachments.Remove(attachment);

        return true;
    }

    /// <inheritdoc />
    public async Task RecordAppendIssuedAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        MailFolderResolution destination,
        DateTimeOffset appendedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(destination);

        var entity = await RequireAsync(session, draftId, cancellationToken);

        if (entity.Copies.Any(copy => copy.Revision == entity.Revision))
        {
            throw new InvalidOperationException(
                $"Revision {entity.Revision} of mail draft {draftId} has already been appended.");
        }

        // Added through the draft's own collection, so the row is inserted as a child of the draft it accounts for and
        // can never be committed against a draft that is not there.
        entity.Copies.Add(new MailDraftCopyEntity
        {
            MailDraftId = entity.Id,
            MailDraft = entity,
            Revision = entity.Revision,
            FolderAlias = destination.Alias.Value,
            FolderPath = destination.RemotePath.Value,
            Stage = MailDraftCopyStage.Issued,
            AppendedAt = appendedAt,
        });
    }

    /// <inheritdoc />
    public async Task RecordAppendConfirmedAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        AppendedMailCopy copy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(copy);

        var entity = await RequireAsync(session, draftId, cancellationToken);

        var appended = entity.Copies.SingleOrDefault(candidate => candidate.Revision == entity.Revision)
            ?? throw new InvalidOperationException(
                $"Revision {entity.Revision} of mail draft {draftId} has no copy awaiting confirmation.");

        if (appended.Stage != MailDraftCopyStage.Issued)
        {
            throw new InvalidOperationException(
                $"The copy of revision {entity.Revision} of mail draft {draftId} is at stage {appended.Stage}, and a confirmation follows {MailDraftCopyStage.Issued}.");
        }

        appended.Stage = MailDraftCopyStage.Standing;
        appended.PlacementUidValidity = copy.Placement.UidValidity?.Value;
        appended.PlacementUid = copy.Placement.Uid?.Value;
        appended.InternetMessageId = copy.InternetMessageId;
    }

    /// <inheritdoc />
    public async Task RecordCopySettledAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        int revision,
        MailDraftCopyStage stage,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (stage is not (MailDraftCopyStage.Withdrawn or MailDraftCopyStage.Abandoned))
        {
            throw new ArgumentOutOfRangeException(
                nameof(stage),
                stage,
                "A copy of a draft is settled as withdrawn or as abandoned, and as nothing else.");
        }

        var entity = await RequireAsync(session, draftId, cancellationToken);

        var settled = entity.Copies.SingleOrDefault(candidate => candidate.Revision == revision)
            ?? throw new InvalidOperationException(
                $"Mail draft {draftId} holds no copy of revision {revision}.");

        settled.Stage = stage;
        settled.SettledAt ??= settledAt;
    }

    /// <inheritdoc />
    public async Task RecordDiscardedAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        DateTimeOffset discardedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var entity = await RequireAsync(session, draftId, cancellationToken);

        entity.DiscardedAt ??= discardedAt;
    }

    /// <inheritdoc />
    public async Task RecordPromotedAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var entity = await RequireAsync(session, draftId, cancellationToken);

        entity.PromotedToOutgoingEmailId ??= outgoingEmailId.Value;
    }

    /// <inheritdoc />
    public async Task RemoveAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        if (await writeContext.MailDrafts.FindAsync([draftId.Value], cancellationToken) is not { } entity)
        {
            return;
        }

        // Read before the removal, because the stored message's row goes by cascade and its key is the only pointer to
        // the object holding the revision the author is discarding.
        await ReleasedContentObjects.ReleaseForMailDraftAsync(session, draftId.Value, cancellationToken);

        // The copies, the recipients, the stored message, and every file staged against the draft go with it through
        // the cascades declared on their foreign keys, which is what makes erasing a draft one act rather than five.
        writeContext.MailDrafts.Remove(entity);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One statement rather than a loaded row, for the reason a filing failure is written as one: it writes columns no
    /// other writer touches and must not turn an overlap with the author's own next revision into a conflict somebody
    /// has to retry. A draft that is no longer there is left alone — the statement writes nothing, which is what an
    /// erasure that ran between the attempt and this looks like.
    /// </remarks>
    public Task RecordDivergenceAsync(
        MailDraftId draftId,
        MailDraftDivergenceReason reason,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken) =>
        readContext.MailDrafts
            .Where(draft => draft.Id == draftId.Value)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(draft => draft.DivergenceReason, reason)
                    .SetProperty(draft => draft.DivergenceObservedAt, observedAt),
                cancellationToken);

    /// <inheritdoc />
    /// <remarks>One statement, for the reason the divergence above is written as one.</remarks>
    public Task RecordFailureAsync(
        MailDraftId draftId,
        MailFathomErrorCode failure,
        CancellationToken cancellationToken) =>
        readContext.MailDrafts
            .Where(draft => draft.Id == draftId.Value)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(draft => draft.LastFailureCode, failure.Value),
                cancellationToken);

    /// <summary>Reads drafts with everything a record is rebuilt from, and without the message they hold.</summary>
    /// <remarks>
    /// <para>
    /// The stored MIME is deliberately not included. Listing what a mailbox holds must not pull every draft's bytes
    /// into memory, and whatever is going to append or transmit one reads it through the content store by identifier.
    /// </para>
    /// <para>
    /// Split rather than joined, because three collections off one row multiply: a draft naming the 256 recipients a
    /// request may name, carrying a copy per unsettled revision and the files an author attached, is one row of the
    /// draft repeated once per combination — and a listing of two hundred such drafts would read that product from the
    /// server to rebuild the same two hundred records. Four bounded statements cost four round trips and read each row
    /// once. What they give up is one statement's consistency: a revision committed between them can leave a record
    /// carrying one revision's recipients beside another's copies, which is a draft this owner is editing rather than
    /// mail, and every caller that acts on a draft re-reads it under the write's own transaction anyway.
    /// </para>
    /// </remarks>
    private IQueryable<MailDraftEntity> ReadDrafts() =>
        readContext.MailDrafts
            .AsNoTracking()
            .AsSplitQuery()
            .Include(draft => draft.Recipients)
            .Include(draft => draft.Copies)
            .Include(draft => draft.Attachments);

    /// <summary>Writes the recipients of one revision as the rows that belong to the draft.</summary>
    /// <remarks>
    /// Added through the navigation rather than through their own set, so they are inserted with the draft they belong
    /// to and a draft can never be committed with somebody else's recipients attached to it.
    /// </remarks>
    private static void AddRecipients(MailDraftEntity entity, IReadOnlyList<MailDraftRecipient> recipients)
    {
        foreach (var (recipient, ordinal) in recipients.Select((recipient, ordinal) => (recipient, ordinal)))
        {
            entity.Recipients.Add(new MailDraftRecipientEntity
            {
                MailDraftId = entity.Id,
                MailDraft = entity,
                Ordinal = ordinal,
                Address = recipient.Recipient.Address.Address,
                ContactId = recipient.Recipient.Contact?.Value,
                Role = recipient.Recipient.Role,
                Provenance = recipient.Provenance,
            });
        }
    }

    /// <summary>Resolves one draft in the caller's session, with the rows every write here reasons about.</summary>
    /// <remarks>
    /// <c>FindAsync</c> rather than a query, so a draft this same session inserted moments earlier is resolved from the
    /// change tracker: a draft is opened and appended inside one attempt, and the second write would otherwise be
    /// looking for a row that is not committed yet. A draft still being inserted carries its rows already and is
    /// recognized by its own state rather than by whether a collection happens to hold anything.
    /// </remarks>
    private static async Task<MailDraftEntity> RequireAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        CancellationToken cancellationToken)
    {
        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        var entity = await writeContext.MailDrafts.FindAsync([draftId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                $"No mail draft carries the identifier {draftId}.");

        var entry = writeContext.Entry(entity);

        if (entry.State == EntityState.Added)
        {
            return entity;
        }

        var recipients = entry.Collection(draft => draft.Recipients);
        if (!recipients.IsLoaded)
        {
            await recipients.LoadAsync(cancellationToken);
        }

        var copies = entry.Collection(draft => draft.Copies);
        if (!copies.IsLoaded)
        {
            await copies.LoadAsync(cancellationToken);
        }

        return entity;
    }
}
