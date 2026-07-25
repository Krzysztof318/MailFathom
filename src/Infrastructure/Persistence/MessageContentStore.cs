// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using MailMcp.Application.MessageContent;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>EF Core raw MIME content store.</summary>
// TODO: Remove this exclusion when the planned PostgreSQL integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by PostgreSQL integration tests.")]
public sealed class MessageContentStore(MailMcpDbContext dbContext, TimeProvider timeProvider) : IMessageContentStore
{
    /// <inheritdoc />
    public async Task SaveContentAsync(
        ISession session,
        StoredEmailId storedEmailId,
        RemoteMessageContent content,
        CancellationToken cancellationToken)
    {
        var storedEmail = dbContext.StoredEmails.Local.SingleOrDefault(email => email.Id == storedEmailId.Value)
            ?? await dbContext.StoredEmails
                .Include(email => email.MailFolder)
                .SingleOrDefaultAsync(email => email.Id == storedEmailId.Value, cancellationToken)
            ?? throw new InvalidOperationException("Raw MIME cannot be stored without its corresponding stored email metadata.");

        EnsureOccurrenceMatches(storedEmail, content);

        var bytes = GetCompleteArray(content.RawMime);
        var byteLength = bytes.LongLength;
        var hash = SHA256.HashData(content.RawMime.Span);
        var storedAt = timeProvider.GetUtcNow();

        var trackedEntity = dbContext.EmailMessageContents.Local.SingleOrDefault(candidate => candidate.StoredEmailId == storedEmailId.Value);
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
            .Where(candidate => candidate.StoredEmailId == storedEmailId.Value)
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
        RemoteMessageContent content)
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
