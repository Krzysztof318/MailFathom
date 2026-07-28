// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Emails;
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
        ExtractedEmailMetadata? extractedMetadata,
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
                MailboxAccountId = folder.MailboxAccountId,
                MailFolder = folder,
                UidValidity = occurrenceId.UidValidity.Value,
                Uid = occurrenceId.Uid.Value,
            };

            dbContext.StoredEmails.Add(entity);
        }

        StoredEmailMetadataMapping.ApplyRemoteSummary(entity, metadata, contentAvailability);

        if (extractedMetadata is not null)
        {
            StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, extractedMetadata);

            // The search document is written from the same extraction in the same session, so an email's indexed text
            // can never describe a different reading of its MIME than its own metadata columns do.
            await EmailSearchDocumentWriter.SaveAsync(
                dbContext,
                entity,
                extractedMetadata,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }

        return StoredEmailId.Create(entity.Id);
    }
}
