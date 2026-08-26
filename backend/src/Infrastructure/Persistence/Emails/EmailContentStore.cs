// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Linq.Expressions;
using System.Runtime.InteropServices;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>Raw MIME content store over the database, the object endpoint, or a deployment's mixture of the two.</summary>
/// <remarks>
/// <para>
/// A write is two steps that happen at different moments. The payload is placed first, before the caller opens its unit
/// of work, because under the object backend that reaches the network and no database transaction may be held open
/// across it. The row is staged afterwards, inside the caller's transaction, from what the placement answered.
/// </para>
/// <para>
/// A read resolves the backend from the row rather than from configuration, which is what lets a deployment hold both
/// kinds of row indefinitely: the configured backend says where the next write goes and never what an existing row
/// means.
/// </para>
/// <para>
/// A row the move has carried and an operator has not yet released is held in both stores at once, and the object is the
/// authoritative one. Where the object cannot be answered for, this serves the copy the database still holds and says so
/// on the way out —
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0017-object-storage-content-backend-consistency-and-object-identity.md">ADR 0017</see> § 6,
/// because refusing over bytes the deployment has would be a self-inflicted outage. Once the copy is released the same
/// situation is answered with nothing, exactly as a missing database payload always was.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailContentStore(
    MailFathomDbContext dbContext,
    TimeProvider timeProvider,
    StoredEmailContentTelemetry telemetry,
    IEmailContentObjectStore? objectStore = null) : IEmailContentStore
{
    /// <inheritdoc />
    /// <remarks>
    /// The presence of an object store is what selects the backend, because it is registered only when the deployment
    /// selected one. That keeps the choice a composition decision rather than a branch this type re-derives from
    /// configuration it would otherwise have to be given.
    /// </remarks>
    public Task<PlacedEmailContent> PlaceContentAsync(
        EmailContentKind kind,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        if (rawMime.IsEmpty)
        {
            throw new ArgumentException("Raw MIME content to place cannot be empty.", nameof(rawMime));
        }

        return objectStore is null
            ? Task.FromResult(PlacedEmailContent.InDatabase(rawMime))
            : objectStore.PlaceAsync(kind, rawMime, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Projected to the columns rather than materialized as an entity, so a database-backed payload is neither tracked
    /// nor kept alive by the change tracker after the caller is done with it. The recorded length and digest are read in
    /// the same round trip as the row that describes them, because a second query could read them from a row a
    /// re-synchronization had rewritten in between and report a mismatch nothing is wrong with.
    /// <para>
    /// The read is spanned because this is where a request meets a whole message: the command's own span reports how
    /// long it took and never how much it moved, and those are the same question here.
    /// </para>
    /// </remarks>
    public async Task<StoredEmailContent?> FindStoredContentAsync(
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        using var read = telemetry.BeginRead();

        var storedContent = await dbContext.EmailMessageContents
            .AsNoTracking()
            .Where(content => content.StoredEmailId == storedEmailId.Value)
            .Select(content => new StoredEmailContentRow(
                content.Backend == ContentStorageBackend.Database ? content.RawMime : null,
                content.MimeByteLength,
                content.Sha256Hash,
                content.Backend,
                content.ObjectLocator,
                content.RawMime != null))
            .SingleOrDefaultAsync(cancellationToken);

        if (storedContent is null)
        {
            read.Absent();

            return null;
        }

        var resolved = await this.ResolveAsync(
            storedContent,
            token => dbContext.EmailMessageContents
                .AsNoTracking()
                .Where(content => content.StoredEmailId == storedEmailId.Value
                    && content.Backend == ContentStorageBackend.ObjectStorage)
                .Select(content => content.RawMime)
                .SingleOrDefaultAsync(token),
            cancellationToken);
        if (resolved is null)
        {
            read.Absent();

            return null;
        }

        read.Found(resolved.RawMime.Length);

        return resolved;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The write is measured rather than spanned, for the reason
    /// <see cref="StoredEmailContentTelemetry" /> records: it happens once per stored message inside a folder run that
    /// already has a span, and what an operator asks of it is a distribution rather than an individual. The measurement
    /// is published by the session instead of here, because whether this staging becomes a stored message is the
    /// session's answer rather than this method's.
    /// </remarks>
    public async Task SaveContentAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        EmailOccurrenceId occurrenceId,
        PlacedEmailContent placedContent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placedContent);

        var writingSession = EfCorePersistenceSessionAccessor.SessionOf(session);

        // Held by the session rather than published here: this body is the staging callback an optimistic-concurrency
        // retry runs again from the beginning, so a losing attempt would otherwise be counted as a stored message.
        using var write = telemetry.BeginWrite();
        writingSession.MeasureOnEnding(write);

        var dbContext = await writingSession.JoinAsync(cancellationToken);

        // The metadata row is added earlier in this same uncommitted session, so it is usually still pending. FindAsync
        // resolves it from the change tracker without a query in that case and falls back to the database otherwise.
        var storedEmail = await dbContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException("Raw MIME cannot be stored without its corresponding stored email metadata.");

        // FindAsync cannot eager-load. A row still pending in this session already carries the folder it was created with,
        // so the reference is loaded only when it is genuinely absent and the pending path stays free of an extra query.
        if (storedEmail.MailFolder is null)
        {
            await dbContext.Entry(storedEmail).Reference(email => email.MailFolder).LoadAsync(cancellationToken);
        }

        EnsureOccurrenceMatches(storedEmail, occurrenceId);

        var bytes = PayloadOf(placedContent);
        var storedAt = timeProvider.GetUtcNow();

        // Deliberately not FindAsync: that would materialize an existing bytea payload into memory and into the change
        // tracker. Only the change-tracker pass is taken here, and a miss falls through to the set-based update below.
        Expression<Func<EmailMessageContentEntity, bool>> matchesStoredEmail =
            candidate => candidate.StoredEmailId == storedEmailId.Value;
        var trackedEntity = dbContext.EmailMessageContents.Local.AsQueryable().SingleOrDefault(matchesStoredEmail);
        if (trackedEntity is not null)
        {
            // Measured against what this session already staged rather than against the database, which still holds
            // whatever was there before this transaction began: a statement reading the stored row would count the
            // payload this session is replacing a second time.
            await OwnerStoredContentLedger.MoveAsync(
                dbContext,
                storedEmail.OwnerId,
                storedEmail.MailboxAccountId,
                placedContent.ByteLength - trackedEntity.MimeByteLength,
                cancellationToken);

            trackedEntity.RawMime = bytes;
            trackedEntity.MimeByteLength = placedContent.ByteLength;
            trackedEntity.Sha256Hash = placedContent.Sha256Hash.ToArray();
            trackedEntity.Backend = placedContent.Backend;
            trackedEntity.ObjectLocator = placedContent.ObjectLocator;
            trackedEntity.StoredAt = storedAt;

            write.Stored(placedContent.ByteLength);

            return;
        }

        // What the owner's figure moves by is the difference between this payload and whatever is stored, which the
        // statement measures for itself. It runs before the write below for that reason, and inside the same
        // transaction, so a rolled-back store leaves the figure exactly where it found it.
        await OwnerStoredContentLedger.AdoptLengthAsync(
            dbContext,
            storedEmail.OwnerId,
            storedEmail.MailboxAccountId,
            storedEmailId.Value,
            placedContent.ByteLength,
            cancellationToken);

        // Re-synchronizing an occurrence that is already stored must not read its existing bytea payload back into memory or
        // into the change tracker, so the overwrite is issued as a set-based update inside the caller's open transaction.
        var updatedRowCount = await dbContext.EmailMessageContents
            .Where(matchesStoredEmail)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.RawMime, bytes)
                    .SetProperty(candidate => candidate.MimeByteLength, placedContent.ByteLength)
                    .SetProperty(candidate => candidate.Sha256Hash, placedContent.Sha256Hash.ToArray())
                    .SetProperty(candidate => candidate.Backend, placedContent.Backend)
                    .SetProperty(candidate => candidate.ObjectLocator, placedContent.ObjectLocator)
                    .SetProperty(candidate => candidate.StoredAt, storedAt),
                cancellationToken);

        if (updatedRowCount == 0)
        {
            dbContext.EmailMessageContents.Add(new EmailMessageContentEntity
            {
                StoredEmailId = storedEmailId.Value,
                StoredEmail = storedEmail,
                RawMime = bytes,
                MimeByteLength = placedContent.ByteLength,
                Sha256Hash = placedContent.Sha256Hash.ToArray(),
                Backend = placedContent.Backend,
                ObjectLocator = placedContent.ObjectLocator,
                StoredAt = storedAt,
            });
        }

        write.Stored(placedContent.ByteLength);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The existence check is what enforces "written once", and it is here rather than in the caller because a caller
    /// cannot close the window: the record and its message are inserted in one transaction, so the only writer that can
    /// meet an existing payload is one working from a record an earlier request already committed.
    /// </para>
    /// <para>
    /// A record still being inserted in this very session can carry no persisted content, so the database pass is
    /// skipped for one — the change-tracker pass is the whole answer there.
    /// </para>
    /// <para>
    /// Leaving an existing payload alone abandons the object this attempt's own placement wrote, which becomes an
    /// orphan nothing ever pointed at. That is the designed cost of placing before the record is known, and reclamation
    /// removes it on the same path it removes a crashed write.
    /// </para>
    /// <para>
    /// Unlike the incoming write, this publishes no measurement.
    /// <see cref="StoredEmailContentTelemetry" /> reports what synchronization stored for a mailbox, and counting a
    /// message this deployment is about to send into that would make the mailbox's content volume read as larger than
    /// the mail it holds.
    /// </para>
    /// </remarks>
    public async Task SaveOutgoingContentAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        PlacedEmailContent placedContent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(placedContent);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // The record is added earlier in this same uncommitted session on the enqueue path, so FindAsync resolves it
        // from the change tracker without a query there and falls back to the database otherwise.
        var outgoingEmail = await writeContext.OutgoingEmails.FindAsync([outgoingEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                "Raw MIME cannot be stored without the outgoing email record it belongs to.");

        Expression<Func<OutgoingEmailContentEntity, bool>> matchesRecord =
            candidate => candidate.OutgoingEmailId == outgoingEmail.Id;

        if (writeContext.OutgoingEmailContents.Local.AsQueryable().Any(matchesRecord))
        {
            return;
        }

        var isRecordPending = writeContext.Entry(outgoingEmail).State == EntityState.Added;
        if (!isRecordPending
            && await writeContext.OutgoingEmailContents.AnyAsync(matchesRecord, cancellationToken))
        {
            return;
        }

        writeContext.OutgoingEmailContents.Add(new OutgoingEmailContentEntity
        {
            OutgoingEmailId = outgoingEmail.Id,
            OutgoingEmail = outgoingEmail,
            RawMime = PayloadOf(placedContent),
            MimeByteLength = placedContent.ByteLength,
            Sha256Hash = placedContent.Sha256Hash.ToArray(),
            Backend = placedContent.Backend,
            ObjectLocator = placedContent.ObjectLocator,
            StoredAt = timeProvider.GetUtcNow(),
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// Projected for the reason the incoming read is, and spanned for none: an outgoing email is read once per delivery
    /// attempt rather than once per request meeting a whole mailbox, and what an operator asks about a send is which
    /// stage it is at rather than how long its bytes took to arrive.
    /// </remarks>
    public async Task<StoredEmailContent?> FindOutgoingContentAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var storedContent = await dbContext.OutgoingEmailContents
            .AsNoTracking()
            .Where(content => content.OutgoingEmailId == outgoingEmailId.Value)
            .Select(content => new StoredEmailContentRow(
                content.Backend == ContentStorageBackend.Database ? content.RawMime : null,
                content.MimeByteLength,
                content.Sha256Hash,
                content.Backend,
                content.ObjectLocator,
                content.RawMime != null))
            .SingleOrDefaultAsync(cancellationToken);

        return await this.ResolveAsync(
            storedContent,
            token => dbContext.OutgoingEmailContents
                .AsNoTracking()
                .Where(content => content.OutgoingEmailId == outgoingEmailId.Value
                    && content.Backend == ContentStorageBackend.ObjectStorage)
                .Select(content => content.RawMime)
                .SingleOrDefaultAsync(token),
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The same "written once" arrangement the outgoing message's has, for a payload nothing transmits: a draft is what
    /// every occasion of a declaration is composed from, so rewriting it under a running declaration would change what
    /// the next occasion sends without changing anything a reader of the declaration can see.
    /// </remarks>
    public async Task SaveRecurringSendDraftAsync(
        IPersistenceSession session,
        RecurringSendId recurringSendId,
        PlacedEmailContent placedContent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(placedContent);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // The declaration is added earlier in this same uncommitted session, so FindAsync resolves it from the change
        // tracker without a query there and falls back to the database otherwise.
        var declaration = await writeContext.RecurringSends.FindAsync([recurringSendId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                "A draft cannot be stored without the recurring send declaration it belongs to.");

        Expression<Func<RecurringSendDraftEntity, bool>> matchesDeclaration =
            candidate => candidate.RecurringSendId == declaration.Id;

        if (writeContext.RecurringSendDrafts.Local.AsQueryable().Any(matchesDeclaration))
        {
            return;
        }

        var isDeclarationPending = writeContext.Entry(declaration).State == EntityState.Added;
        if (!isDeclarationPending
            && await writeContext.RecurringSendDrafts.AnyAsync(matchesDeclaration, cancellationToken))
        {
            return;
        }

        writeContext.RecurringSendDrafts.Add(new RecurringSendDraftEntity
        {
            RecurringSendId = declaration.Id,
            RecurringSend = declaration,
            DraftMime = PayloadOf(placedContent),
            DraftByteLength = placedContent.ByteLength,
            Sha256Hash = placedContent.Sha256Hash.ToArray(),
            Backend = placedContent.Backend,
            ObjectLocator = placedContent.ObjectLocator,
            StoredAt = timeProvider.GetUtcNow(),
        });
    }

    /// <inheritdoc />
    /// <remarks>Read once per occasion rather than once per attempt, which is rarer still than the outgoing read; it is projected the same way for the same reason.</remarks>
    public async Task<StoredEmailContent?> FindRecurringSendDraftAsync(
        RecurringSendId recurringSendId,
        CancellationToken cancellationToken)
    {
        var storedDraft = await dbContext.RecurringSendDrafts
            .AsNoTracking()
            .Where(draft => draft.RecurringSendId == recurringSendId.Value)
            .Select(draft => new StoredEmailContentRow(
                draft.Backend == ContentStorageBackend.Database ? draft.DraftMime : null,
                draft.DraftByteLength,
                draft.Sha256Hash,
                draft.Backend,
                draft.ObjectLocator,
                draft.DraftMime != null))
            .SingleOrDefaultAsync(cancellationToken);

        return await this.ResolveAsync(
            storedDraft,
            token => dbContext.RecurringSendDrafts
                .AsNoTracking()
                .Where(draft => draft.RecurringSendId == recurringSendId.Value
                    && draft.Backend == ContentStorageBackend.ObjectStorage)
                .Select(draft => draft.DraftMime)
                .SingleOrDefaultAsync(token),
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The one raw-MIME write here that overwrites, and the difference from the outgoing one is deliberate: what an
    /// author is editing is one message, so the row is rewritten in place rather than kept per revision. The revision
    /// number is written in the same session by the draft store, which is what keeps the bytes and the number the row
    /// beside them carries from disagreeing.
    /// </para>
    /// <para>
    /// Under the object backend the row is repointed rather than the object rewritten, because every placement mints a
    /// key of its own. A commit that never happens therefore leaves the row pointing at the previous revision's object,
    /// which is intact, and a commit that does happen leaves the superseded object an orphan.
    /// </para>
    /// <para>
    /// Unlike the incoming write, this publishes no measurement, for the reason the outgoing write publishes none: what
    /// synchronization stored for a mailbox is a different quantity from what this deployment composed.
    /// </para>
    /// </remarks>
    public async Task SaveMailDraftContentAsync(
        IPersistenceSession session,
        MailDraftId draftId,
        PlacedEmailContent placedContent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(placedContent);

        var writeContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // The draft is added earlier in this same uncommitted session on the save path, so FindAsync resolves it from
        // the change tracker without a query there and falls back to the database otherwise.
        var draft = await writeContext.MailDrafts.FindAsync([draftId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                "Raw MIME cannot be stored without the draft record it is a revision of.");

        var bytes = PayloadOf(placedContent);
        var digest = placedContent.Sha256Hash.ToArray();
        var storedAt = timeProvider.GetUtcNow();

        // A draft still being inserted can carry no persisted message, so the database pass is skipped for one — the
        // change-tracker pass is the whole answer there.
        var isDraftPending = writeContext.Entry(draft).State == EntityState.Added;

        Expression<Func<MailDraftContentEntity, bool>> matchesDraft =
            candidate => candidate.MailDraftId == draft.Id;

        var trackedContent = writeContext.MailDraftContents.Local.AsQueryable().SingleOrDefault(matchesDraft);
        if (trackedContent is not null)
        {
            trackedContent.RawMime = bytes;
            trackedContent.MimeByteLength = placedContent.ByteLength;
            trackedContent.Sha256Hash = digest;
            trackedContent.Backend = placedContent.Backend;
            trackedContent.ObjectLocator = placedContent.ObjectLocator;
            trackedContent.StoredAt = storedAt;

            return;
        }

        // Every revision after the first meets a stored message, and reading it back would materialize the whole
        // previous payload into memory and into the change tracker to overwrite every column of it. Editing a draft is
        // an ordinary, repeated act, so the overwrite is issued as a set-based update inside the caller's open
        // transaction — the same reasoning the incoming write above records.
        var updatedRowCount = isDraftPending
            ? 0
            : await writeContext.MailDraftContents
                .Where(matchesDraft)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.RawMime, bytes)
                        .SetProperty(candidate => candidate.MimeByteLength, placedContent.ByteLength)
                        .SetProperty(candidate => candidate.Sha256Hash, digest)
                        .SetProperty(candidate => candidate.Backend, placedContent.Backend)
                        .SetProperty(candidate => candidate.ObjectLocator, placedContent.ObjectLocator)
                        .SetProperty(candidate => candidate.StoredAt, storedAt),
                    cancellationToken);

        if (updatedRowCount > 0)
        {
            return;
        }

        writeContext.MailDraftContents.Add(new MailDraftContentEntity
        {
            MailDraftId = draft.Id,
            MailDraft = draft,
            RawMime = bytes,
            MimeByteLength = placedContent.ByteLength,
            Sha256Hash = digest,
            Backend = placedContent.Backend,
            ObjectLocator = placedContent.ObjectLocator,
            StoredAt = storedAt,
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// Projected for the reason the other two reads are, and spanned for none: a draft's message is read when it is
    /// appended or promoted rather than once per request meeting a whole mailbox.
    /// </remarks>
    public async Task<StoredEmailContent?> FindMailDraftContentAsync(
        MailDraftId draftId,
        CancellationToken cancellationToken)
    {
        var storedContent = await dbContext.MailDraftContents
            .AsNoTracking()
            .Where(content => content.MailDraftId == draftId.Value)
            .Select(content => new StoredEmailContentRow(
                content.Backend == ContentStorageBackend.Database ? content.RawMime : null,
                content.MimeByteLength,
                content.Sha256Hash,
                content.Backend,
                content.ObjectLocator,
                content.RawMime != null))
            .SingleOrDefaultAsync(cancellationToken);

        return await this.ResolveAsync(
            storedContent,
            token => dbContext.MailDraftContents
                .AsNoTracking()
                .Where(content => content.MailDraftId == draftId.Value
                    && content.Backend == ContentStorageBackend.ObjectStorage)
                .Select(content => content.RawMime)
                .SingleOrDefaultAsync(token),
            cancellationToken);
    }

    /// <summary>Answers one row with the payload it points at, wherever that is, and falls back to the copy it retains.</summary>
    /// <param name="row">The columns the read projected, or <see langword="null" /> when no content is stored.</param>
    /// <param name="readRetainedPayload">Reads the payload column of this row, asked for only where the object could not be vouched for.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The content, or <see langword="null" /> when neither store could answer for it.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the row is object-backed and this deployment configured no endpoint.</exception>
    /// <remarks>
    /// <para>
    /// The object is the authoritative store for an object-backed row, so it is asked first and its answer is checked
    /// against the length and digest the row records — which is the same check the caller performs, run here for the one
    /// decision only this method can take: whether to reach for the copy beside it. An object that passes is handed over
    /// carrying <see cref="StoredEmailContent.WasVerifiedIntact" />, so the caller reads that answer instead of hashing
    /// the message a second time.
    /// </para>
    /// <para>
    /// It reaches for that copy only where there is one and the object failed, which is what keeps the second read off
    /// every ordinary path: a released row, and a row written straight to the bucket, ask the endpoint once and stop.
    /// </para>
    /// <para>
    /// A row with nothing to fall back to is answered as absent rather than raised, so the caller grades it exactly as
    /// it grades a missing database payload: an ordinary answer for incoming mail, a defect for a send or a draft, and a
    /// repair request either way.
    /// </para>
    /// </remarks>
    private async Task<StoredEmailContent?> ResolveAsync(
        StoredEmailContentRow? row,
        Func<CancellationToken, Task<byte[]?>> readRetainedPayload,
        CancellationToken cancellationToken)
    {
        if (row is null)
        {
            return null;
        }

        if (row.Backend is ContentStorageBackend.Database)
        {
            return row.ToStoredContent(row.RawMime ?? []);
        }

        if (objectStore is null)
        {
            // Not a failure a reader can act on and not one this type can repair: the deployment is holding mail in a
            // place it no longer describes. The readiness check is what reports it, and it reports it whether or not
            // anybody has tried to read one of these rows yet.
            throw new InvalidOperationException(
                "This payload is held in object storage and no object-storage endpoint is configured for this deployment.");
        }

        var payload = await objectStore.FindAsync(row.ObjectLocator!, row.MimeByteLength, cancellationToken);

        if (payload is not { } objectPayload)
        {
            return await this.FallBackAsync(
                row,
                readRetainedPayload,
                StoredContentFallbackReason.ObjectAbsent,
                cancellationToken);
        }

        var fromObject = row.ToStoredContent(objectPayload);

        if (fromObject.FindIntegrityDefect() is null)
        {
            return fromObject with { WasVerifiedIntact = true };
        }

        if (!row.CarriesDatabasePayload)
        {
            return fromObject;
        }

        // The object disagrees with its own row and a copy is still retained beside it. A release that landed between
        // the projection above and the read below leaves nothing to fall back to, and the object is then this row's only
        // answer — which the caller grades as the damaged copy it is.
        return await this.FallBackAsync(
            row,
            readRetainedPayload,
            StoredContentFallbackReason.ObjectMismatch,
            cancellationToken) ?? fromObject;
    }

    /// <summary>Serves the copy the database still holds for a moved payload, and records that it had to.</summary>
    /// <returns>The retained content, marked as such, or <see langword="null" /> when the copy is gone.</returns>
    private async Task<StoredEmailContent?> FallBackAsync(
        StoredEmailContentRow row,
        Func<CancellationToken, Task<byte[]?>> readRetainedPayload,
        StoredContentFallbackReason reason,
        CancellationToken cancellationToken)
    {
        if (!row.CarriesDatabasePayload)
        {
            return null;
        }

        if (await readRetainedPayload(cancellationToken) is not { } retainedPayload)
        {
            return null;
        }

        telemetry.FellBackToRetainedCopy(reason);

        return row.ToStoredContent(retainedPayload) with { WasServedFromRetainedCopy = true };
    }

    private static void EnsureOccurrenceMatches(StoredEmailEntity storedEmail, EmailOccurrenceId occurrenceId)
    {
        if (storedEmail.MailFolder.MailboxAccountId != occurrenceId.AccountId.Value
            || storedEmail.MailFolder.Alias != occurrenceId.FolderResolutionId.Alias.Value
            || storedEmail.MailFolder.ResolutionGeneration != occurrenceId.FolderResolutionId.Generation.Value
            || storedEmail.UidValidity != occurrenceId.UidValidity.Value
            || storedEmail.Uid != occurrenceId.Uid.Value)
        {
            throw new InvalidOperationException("Raw MIME occurrence identity does not match the corresponding stored email metadata.");
        }
    }

    /// <summary>Gets the bytes the row itself carries, which is nothing at all when the object backend holds them.</summary>
    private static byte[]? PayloadOf(PlacedEmailContent placedContent) =>
        placedContent.Backend is ContentStorageBackend.Database
            ? GetCompleteArray(placedContent.RawMime)
            : null;

    private static byte[] GetCompleteArray(ReadOnlyMemory<byte> rawMime)
    {
        if (MemoryMarshal.TryGetArray(rawMime, out var segment)
            && segment.Offset == 0
            && segment.Count == segment.Array!.Length)
        {
            return segment.Array;
        }

        return rawMime.ToArray();
    }
}
