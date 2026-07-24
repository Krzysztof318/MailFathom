// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>EF Core implementation for message metadata persistence.</summary>
// TODO: Remove this exclusion when the planned PostgreSQL integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by PostgreSQL integration tests.")]
public sealed class StoredEmailMetadataRepository(
    MailMcpDbContext dbContext,
    TimeProvider timeProvider) : IMessageMetadataRepository
{
    /// <inheritdoc />
    public async Task<StoredEmailId> UpsertMetadataAsync(
        ISession session,
        RemoteMessageMetadata metadata,
        CancellationToken cancellationToken)
    {
        var occurrenceId = metadata.OccurrenceId;
        var entity = dbContext.StoredEmails.Local.SingleOrDefault(
            candidate => candidate.MailFolder.MailboxAccountId == occurrenceId.AccountId.Value
                && candidate.MailFolder.RemoteName == occurrenceId.FolderName.Value
                && candidate.UidValidity == occurrenceId.UidValidity.Value
                && candidate.Uid == occurrenceId.Uid.Value)
            ?? await dbContext.StoredEmails
                .Include(candidate => candidate.MailFolder)
                .SingleOrDefaultAsync(
                    candidate => candidate.MailFolder.MailboxAccountId == occurrenceId.AccountId.Value
                        && candidate.MailFolder.RemoteName == occurrenceId.FolderName.Value
                        && candidate.UidValidity == occurrenceId.UidValidity.Value
                        && candidate.Uid == occurrenceId.Uid.Value,
                    cancellationToken);
        if (entity is null)
        {
            var folder = await MailFolderEntityResolver.GetOrAddAsync(
                dbContext,
                occurrenceId.AccountId,
                occurrenceId.FolderName,
                cancellationToken);
            entity = new StoredEmailEntity
            {
                Id = Guid.CreateVersion7(timeProvider.GetUtcNow()),
                MailFolder = folder,
                UidValidity = occurrenceId.UidValidity.Value,
                Uid = occurrenceId.Uid.Value,
                InternetMessageId = metadata.InternetMessageId,
                Subject = metadata.Subject,
                SentAt = metadata.SentAt,
                SizeOctets = metadata.SizeOctets,
            };
            dbContext.StoredEmails.Add(entity);
        }
        else
        {
            entity.InternetMessageId = metadata.InternetMessageId;
            entity.Subject = metadata.Subject;
            entity.SentAt = metadata.SentAt;
            entity.SizeOctets = metadata.SizeOctets;
        }

        return StoredEmailId.Create(entity.Id);
    }
}
