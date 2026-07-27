// Copyright © 2026 Krzysztof Kasprowicz

using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MailMcp.Application.EmailContent;
using MailMcp.Application.Persistence;
using MailMcp.CodeCoverage;
using MailMcp.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>EF Core raw MIME content store.</summary>
[RequiresIntegrationCoverage]
internal sealed class EmailContentStore(TimeProvider timeProvider) : IEmailContentStore
{
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
            || storedEmail.MailFolder.RemoteName != occurrenceId.FolderName.Value
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
