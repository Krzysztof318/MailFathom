// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Application.Emails;
using MailMcp.Application.Persistence;
using MailMcp.CodeCoverage;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Folders;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>EF Core state for the walk that re-derives extraction over emails stored before it existed.</summary>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailExtractionBackfillStore(MailMcpDbContext dbContext, TimeProvider timeProvider)
    : IStoredEmailExtractionBackfillStore
{
    /// <inheritdoc />
    public async Task<StoredEmailId?> FindResumePositionAsync(CancellationToken cancellationToken)
    {
        var position = await dbContext.BackfillPositions
            .AsNoTracking()
            .Where(candidate => candidate.Name == BackfillPositionEntity.StoredEmailExtractionName)
            .Select(candidate => (Guid?)candidate.LastProcessedStoredEmailId)
            .SingleOrDefaultAsync(cancellationToken);

        return position is { } lastProcessed ? StoredEmailId.Create(lastProcessed) : null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The predicate is what makes the walk shrink: an email gains a search document exactly when its extraction is
    /// committed, so a completed one never appears in a later batch even if the resume position is reset. A tombstoned
    /// email is skipped as well, because indexing text nothing may search for is work with no reader. Ordering by
    /// the primary key gives the keyset comparison an index to walk and a total order that no later write disturbs.
    /// Both the ordering and the comparison are evaluated by PostgreSQL, so the walk runs entirely under that server's
    /// <c>uuid</c> ordering and never has to agree with how the CLR compares two <see cref="Guid" /> values.
    /// </remarks>
    public async Task<IReadOnlyList<StoredEmailAwaitingExtraction>> GetEmailsAwaitingExtractionAsync(
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var resumeAfterId = resumeAfter?.Value;
        var candidates = await dbContext.StoredEmails
            .AsNoTracking()
            .Where(email => email.ContentAvailability == StoredEmailContentAvailability.Available
                && email.SearchDocument == null
                && email.RemoteExpungeObservedAt == null
                && (resumeAfterId == null || email.Id > resumeAfterId))
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(email => new
            {
                email.Id,
                email.MailFolder.MailboxAccountId,
                email.MailFolder.Alias,
                email.MailFolder.ResolutionGeneration,
                email.UidValidity,
                email.Uid,
            })
            .ToArrayAsync(cancellationToken);

        return
        [
            .. candidates.Select(candidate => new StoredEmailAwaitingExtraction(
                StoredEmailId.Create(candidate.Id),
                EmailOccurrenceId.Create(
                    MailAccountId.Create(candidate.MailboxAccountId),
                    new MailFolderResolutionId(
                        MailFolderAlias.Create(candidate.Alias),
                        MailFolderResolutionGeneration.Create(candidate.ResolutionGeneration)),
                    ImapUidValidity.Create(candidate.UidValidity),
                    ImapUid.Create(candidate.Uid)))),
        ];
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the email disappeared between the batch query and this write.</exception>
    public async Task ApplyExtractionAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        ExtractedEmailMetadata metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var storedEmail = await sessionContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException("Extraction cannot be applied to a stored email that no longer exists.");

        StoredEmailMetadataMapping.ApplyExtractedMetadata(storedEmail, metadata);

        await EmailSearchDocumentWriter.SaveAsync(
            sessionContext,
            storedEmail,
            metadata,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveResumePositionAsync(
        IPersistenceSession session,
        StoredEmailId position,
        CancellationToken cancellationToken)
    {
        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var recordedAt = timeProvider.GetUtcNow();

        // FindAsync resolves a row this session already staged from the change tracker, so a run that commits several
        // batches through one session updates one row rather than inserting a second under the same key.
        var storedPosition = await sessionContext.BackfillPositions.FindAsync(
            [BackfillPositionEntity.StoredEmailExtractionName],
            cancellationToken);

        if (storedPosition is null)
        {
            sessionContext.BackfillPositions.Add(new BackfillPositionEntity
            {
                Name = BackfillPositionEntity.StoredEmailExtractionName,
                LastProcessedStoredEmailId = position.Value,
                UpdatedAt = recordedAt,
            });

            return;
        }

        storedPosition.LastProcessedStoredEmailId = position.Value;
        storedPosition.UpdatedAt = recordedAt;
    }
}
