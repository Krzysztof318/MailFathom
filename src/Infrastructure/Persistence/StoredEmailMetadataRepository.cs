// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.CodeCoverage;
using MailMcp.Domain.Emails;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>EF Core implementation for email metadata persistence.</summary>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailMetadataRepository(TimeProvider timeProvider) : IEmailMetadataRepository
{
    /// <inheritdoc />
    public async Task<StoredEmailId> UpsertMetadataAsync(
        IPersistenceSession session,
        RemoteEmailMetadata metadata,
        StoredEmailContentAvailability contentAvailability,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var dbContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var occurrenceId = metadata.OccurrenceId;
        var alias = occurrenceId.FolderResolutionId.Alias.Value;
        var generation = occurrenceId.FolderResolutionId.Generation.Value;
        var entity = await TrackedEntityLookup.SinglePendingOrPersistedAsync(
            dbContext.StoredEmails,
            dbContext.StoredEmails.Include(candidate => candidate.MailFolder),
            candidate => candidate.MailFolder.MailboxAccountId == occurrenceId.AccountId.Value
                && candidate.MailFolder.Alias == alias
                && candidate.MailFolder.ResolutionGeneration == generation
                && candidate.UidValidity == occurrenceId.UidValidity.Value
                && candidate.Uid == occurrenceId.Uid.Value,
            cancellationToken);

        if (entity is null)
        {
            var folder = await MailFolderEntityResolver.GetRequiredAsync(
                dbContext,
                occurrenceId.AccountId,
                occurrenceId.FolderResolutionId,
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
                ContentAvailability = contentAvailability,
            };

            dbContext.StoredEmails.Add(entity);
        }
        else
        {
            entity.InternetMessageId = metadata.InternetMessageId;
            entity.Subject = metadata.Subject;
            entity.SentAt = metadata.SentAt;
            entity.SizeOctets = metadata.SizeOctets;
            entity.ContentAvailability = contentAvailability;
        }

        return StoredEmailId.Create(entity.Id);
    }
}
