// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Scheduling;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Observability;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core raw MIME content store.</summary>
[RequiresIntegrationCoverage]
internal sealed class EmailContentStore(
    MailFathomDbContext dbContext,
    TimeProvider timeProvider,
    StoredEmailContentTelemetry telemetry) : IEmailContentStore
{
    /// <inheritdoc />
    /// <remarks>
    /// Projected to the three columns rather than materialized as an entity, so the payload is neither tracked nor
    /// kept alive by the change tracker after the caller is done with it. The recorded length and digest are read in
    /// the same round trip as the payload they describe, because a second query could read them from a row a
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
            .Select(content => new StoredEmailContentRow(content.RawMime, content.MimeByteLength, content.Sha256Hash))
            .SingleOrDefaultAsync(cancellationToken);

        if (storedContent is null)
        {
            read.Absent();

            return null;
        }

        read.Found(storedContent.RawMime.LongLength);

        return new StoredEmailContent(
            storedContent.RawMime.AsMemory(),
            storedContent.MimeByteLength,
            storedContent.Sha256Hash.AsMemory());
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
        RemoteEmailContent content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var writingSession = EfCorePersistenceSessionAccessor.SessionOf(session);

        // Held by the session rather than published here: this body is the staging callback an optimistic-concurrency
        // retry runs again from the beginning, so a losing attempt would otherwise be counted as a stored message.
        using var write = telemetry.BeginWrite();
        writingSession.MeasureOnEnding(write);

        var dbContext = writingSession.DbContext;

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

        EnsureOccurrenceMatches(storedEmail, content);

        var bytes = GetCompleteArray(content.RawMime);
        var byteLength = bytes.LongLength;
        var hash = SHA256.HashData(content.RawMime.Span);
        var storedAt = timeProvider.GetUtcNow();

        // Deliberately not FindAsync: that would materialize the existing bytea payload into memory and into the change
        // tracker. Only the change-tracker pass is taken here, and a miss falls through to the set-based update below.
        Expression<Func<EmailMessageContentEntity, bool>> matchesStoredEmail =
            candidate => candidate.StoredEmailId == storedEmailId.Value;
        var trackedEntity = dbContext.EmailMessageContents.Local.AsQueryable().SingleOrDefault(matchesStoredEmail);
        if (trackedEntity is not null)
        {
            trackedEntity.RawMime = bytes;
            trackedEntity.MimeByteLength = byteLength;
            trackedEntity.Sha256Hash = hash;
            trackedEntity.StoredAt = storedAt;

            write.Stored(byteLength);

            return;
        }

        // Re-synchronizing an occurrence that is already stored must not read its existing bytea payload back into memory or
        // into the change tracker, so the overwrite is issued as a set-based update inside the caller's open transaction.
        var updatedRowCount = await dbContext.EmailMessageContents
            .Where(matchesStoredEmail)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(candidate => candidate.RawMime, bytes)
                    .SetProperty(candidate => candidate.MimeByteLength, byteLength)
                    .SetProperty(candidate => candidate.Sha256Hash, hash)
                    .SetProperty(candidate => candidate.StoredAt, storedAt),
                cancellationToken);

        if (updatedRowCount == 0)
        {
            dbContext.EmailMessageContents.Add(new EmailMessageContentEntity
            {
                StoredEmailId = storedEmailId.Value,
                StoredEmail = storedEmail,
                RawMime = bytes,
                MimeByteLength = byteLength,
                Sha256Hash = hash,
                StoredAt = storedAt,
            });
        }

        write.Stored(byteLength);
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
    /// Unlike the incoming write, this publishes no measurement.
    /// <see cref="StoredEmailContentTelemetry" /> reports what synchronization stored for a mailbox, and counting a
    /// message this deployment is about to send into that would make the mailbox's content volume read as larger than
    /// the mail it holds.
    /// </para>
    /// </remarks>
    public async Task SaveOutgoingContentAsync(
        IPersistenceSession session,
        OutgoingEmailId outgoingEmailId,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (rawMime.IsEmpty)
        {
            throw new ArgumentException(
                "An outgoing email is stored with the MIME it will be transmitted as.",
                nameof(rawMime));
        }

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

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

        var bytes = GetCompleteArray(rawMime);

        writeContext.OutgoingEmailContents.Add(new OutgoingEmailContentEntity
        {
            OutgoingEmailId = outgoingEmail.Id,
            OutgoingEmail = outgoingEmail,
            RawMime = bytes,
            MimeByteLength = bytes.LongLength,
            Sha256Hash = SHA256.HashData(rawMime.Span),
            StoredAt = timeProvider.GetUtcNow(),
        });
    }

    /// <inheritdoc />
    /// <remarks>
    /// Projected to the three columns for the reason the incoming read is, and spanned for none: an outgoing email is
    /// read once per delivery attempt rather than once per request meeting a whole mailbox, and what an operator asks
    /// about a send is which stage it is at rather than how long its bytes took to leave the database.
    /// </remarks>
    public async Task<StoredEmailContent?> FindOutgoingContentAsync(
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var storedContent = await dbContext.OutgoingEmailContents
            .AsNoTracking()
            .Where(content => content.OutgoingEmailId == outgoingEmailId.Value)
            .Select(content => new StoredEmailContentRow(content.RawMime, content.MimeByteLength, content.Sha256Hash))
            .SingleOrDefaultAsync(cancellationToken);

        return storedContent is null
            ? null
            : new StoredEmailContent(
                storedContent.RawMime.AsMemory(),
                storedContent.MimeByteLength,
                storedContent.Sha256Hash.AsMemory());
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
        ReadOnlyMemory<byte> draftMime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (draftMime.IsEmpty)
        {
            throw new ArgumentException(
                "A recurring send is declared with the draft its occurrences are composed from.",
                nameof(draftMime));
        }

        var writeContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

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

        var bytes = GetCompleteArray(draftMime);

        writeContext.RecurringSendDrafts.Add(new RecurringSendDraftEntity
        {
            RecurringSendId = declaration.Id,
            RecurringSend = declaration,
            DraftMime = bytes,
            DraftByteLength = bytes.LongLength,
            Sha256Hash = SHA256.HashData(draftMime.Span),
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
            .Select(draft => new StoredEmailContentRow(draft.DraftMime, draft.DraftByteLength, draft.Sha256Hash))
            .SingleOrDefaultAsync(cancellationToken);

        return storedDraft is null
            ? null
            : new StoredEmailContent(
                storedDraft.RawMime.AsMemory(),
                storedDraft.MimeByteLength,
                storedDraft.Sha256Hash.AsMemory());
    }

    private static void EnsureOccurrenceMatches(
        StoredEmailEntity storedEmail,
        RemoteEmailContent content)
    {
        var occurrenceId = content.OccurrenceId;
        if (storedEmail.MailFolder.MailboxAccountId != occurrenceId.AccountId.Value
            || storedEmail.MailFolder.Alias != occurrenceId.FolderResolutionId.Alias.Value
            || storedEmail.MailFolder.ResolutionGeneration != occurrenceId.FolderResolutionId.Generation.Value
            || storedEmail.UidValidity != occurrenceId.UidValidity.Value
            || storedEmail.Uid != occurrenceId.Uid.Value)
        {
            throw new InvalidOperationException("Raw MIME occurrence identity does not match the corresponding stored email metadata.");
        }
    }

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
