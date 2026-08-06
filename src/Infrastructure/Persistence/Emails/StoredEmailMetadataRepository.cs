// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core implementation for email metadata persistence.</summary>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailMetadataRepository(TimeProvider timeProvider, EmailChunkWriter chunkWriter)
    : IEmailMetadataRepository
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

            // Cut in the same session as the text it derives from, so a committed message is never one whose passages
            // a later reader has to wait for. Nothing here reaches a provider: chunking is a local derivation, and an
            // instance that never enables embeddings simply keeps passages nothing asks for yet.
            await chunkWriter.SaveAsync(dbContext, entity, extractedMetadata.Text, cancellationToken);
        }
        else
        {
            // An occurrence whose body nothing read still gets a document, built from the envelope alone. Leaving it
            // without one would make an oversized or unparseable message findable by nothing at all, which is the same
            // silent gap the encrypted marker exists to close for a message whose body cannot be decrypted.
            await EmailSearchDocumentWriter.SaveEnvelopeOnlyAsync(
                dbContext,
                entity,
                metadata.Subject,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }

        return StoredEmailId.Create(entity.Id);
    }
}
