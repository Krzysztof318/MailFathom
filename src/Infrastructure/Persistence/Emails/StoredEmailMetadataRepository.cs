// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.Application.Synchronization;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Emails.Threads;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using MailFathom.Infrastructure.Persistence.Synchronization;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core implementation for email metadata persistence.</summary>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailMetadataRepository(
    TimeProvider timeProvider,
    SensitiveContentDerivationGuard derivationGuard,
    EmailThreadAssembly threadAssembly)
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
        var entity = await FindByOccurrenceAsync(dbContext, occurrenceId, cancellationToken);

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
                StoredAt = timeProvider.GetUtcNow(),
            };

            dbContext.StoredEmails.Add(entity);
        }

        StoredEmailMetadataMapping.ApplyRemoteSummary(entity, metadata, contentAvailability);

        if (extractedMetadata is not null)
        {
            StoredEmailMetadataMapping.ApplyExtractedMetadata(entity, extractedMetadata);

            // The search document is written from the same extraction in the same session, so an email's indexed text
            // can never describe a different reading of its MIME than its own metadata columns do. The stamp is written
            // with it, because the reading arrived here already redacted and the row has to say under what.
            await EmailSearchDocumentWriter.SaveAsync(
                dbContext,
                entity,
                extractedMetadata,
                timeProvider.GetUtcNow(),
                derivationGuard.Stamp,
                cancellationToken);
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
                derivationGuard.Stamp,
                cancellationToken);
        }

        // Placed in the transaction that commits the message, from the identifier columns written a moment ago, so no
        // message is ever readable outside the conversation it belongs to. It runs for an occurrence whose body was
        // never stored as well: the server's envelope still reported a Message-ID, and a message nothing can join is a
        // conversation of one rather than a message with no conversation.
        await threadAssembly.AssembleAsync(
            session,
            MailAccountId.Create(entity.MailboxAccountId),
            ThreadedEmails.Of(entity),
            entity.EmailThreadId is { } currentThreadId ? EmailThreadId.Create(currentThreadId) : null,
            cancellationToken);

        return StoredEmailId.Create(entity.Id);
    }

    /// <inheritdoc />
    public async Task<bool> TryCarryToOccurrenceAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrenceId);

        var dbContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var occupant = await FindByOccurrenceAsync(dbContext, occurrenceId, cancellationToken);

        // A row already sitting on the occurrence is either this same email, which a previous attempt of this commit
        // already carried, or a different one, which only the mailbox could say is the wrong occupant.
        if (occupant is not null)
        {
            return occupant.Id == storedEmailId.Value;
        }

        var entity = await dbContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                $"No stored email carries the identifier {storedEmailId}, so no occurrence can be carried onto it.");
        var folder = await MailFolderEntityResolver.GetRequiredAsync(
            dbContext,
            occurrenceId.AccountId,
            occurrenceId.FolderResolutionId,
            cancellationToken);

        entity.MailFolder = folder;
        entity.MailFolderId = folder.Id;
        entity.UidValidity = occurrenceId.UidValidity.Value;
        entity.Uid = occurrenceId.Uid.Value;

        // The stored flags were read in the folder the email has left, and the tombstone, if the source disappearance
        // was seen first, described an occurrence that no longer exists. All three are cleared so the destination
        // folder's own window is what says what holds there now: the email has a remote occurrence again, so it is no
        // longer one retained without one either.
        entity.RemoteFlagsObservedAt = null;
        entity.RemoteExpungeObservedAt = null;
        entity.IsRetainedAfterAuthoredDelete = false;

        return true;
    }

    /// <inheritdoc />
    public async Task RecordFiledFromOutgoingAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        OutgoingEmailId outgoingEmailId,
        CancellationToken cancellationToken)
    {
        var dbContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        // The email was inserted by this same session and may not be committed yet, which is exactly what FindAsync
        // resolves from the change tracker.
        var entity = await dbContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException(
                $"No stored email carries the identifier {storedEmailId}, so no outgoing record can be joined to it.");

        entity.FiledFromOutgoingEmailId = outgoingEmailId.Value;
    }

    /// <summary>Reads whatever row already occupies one occurrence, including one this session has staged and not committed.</summary>
    private static Task<StoredEmailEntity?> FindByOccurrenceAsync(
        MailFathomDbContext dbContext,
        EmailOccurrenceId occurrenceId,
        CancellationToken cancellationToken)
    {
        var alias = occurrenceId.FolderResolutionId.Alias.Value;
        var generation = occurrenceId.FolderResolutionId.Generation.Value;

        return TrackedEntityLookup.SinglePendingOrPersistedAsync(
            dbContext.StoredEmails,
            dbContext.StoredEmails.Include(candidate => candidate.MailFolder),
            candidate => candidate.MailFolder.MailboxAccountId == occurrenceId.AccountId.Value
                && candidate.MailFolder.Alias == alias
                && candidate.MailFolder.ResolutionGeneration == generation
                && candidate.UidValidity == occurrenceId.UidValidity.Value
                && candidate.Uid == occurrenceId.Uid.Value,
            cancellationToken);
    }
}
