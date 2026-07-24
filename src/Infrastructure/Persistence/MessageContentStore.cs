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
        var hash = SHA256.HashData(content.RawMime.Span);
        var entity = dbContext.EmailMessageContents.Local.SingleOrDefault(candidate => candidate.StoredEmailId == storedEmailId.Value)
            ?? await dbContext.EmailMessageContents.SingleOrDefaultAsync(
                candidate => candidate.StoredEmailId == storedEmailId.Value,
                cancellationToken);
        if (entity is null)
        {
            dbContext.EmailMessageContents.Add(new EmailMessageContentEntity
            {
                StoredEmailId = storedEmailId.Value,
                StoredEmail = storedEmail,
                RawMime = bytes,
                MimeByteLength = bytes.LongLength,
                Sha256Hash = hash,
                StoredAt = timeProvider.GetUtcNow(),
            });
        }
        else
        {
            entity.RawMime = bytes;
            entity.MimeByteLength = bytes.LongLength;
            entity.Sha256Hash = hash;
            entity.StoredAt = timeProvider.GetUtcNow();
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
