// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
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
    public async Task SaveContentAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        RemoteEmailContent content,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var dbContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

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
