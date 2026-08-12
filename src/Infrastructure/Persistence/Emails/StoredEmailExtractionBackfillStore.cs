// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Persistence;
using MailFathom.Application.SensitiveContent.Derivation;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>EF Core state for the walk that re-derives extraction over emails stored before it existed.</summary>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailExtractionBackfillStore(
    MailFathomDbContext dbContext,
    TimeProvider timeProvider,
    EmailChunkWriter chunkWriter,
    SensitiveContentDerivationGuard derivationGuard,
    StoredEmailExtractionBackfillOptions options)
    : IStoredEmailExtractionBackfillStore
{
    /// <summary>Gets the stamp a rebuilding walk judges a stored document against, or nothing where it rebuilds none.</summary>
    /// <remarks>
    /// Both halves are required: an operator who asked for a rebuild on a deployment that scans nothing has asked for
    /// every derived row to be re-derived back to the text it already holds, which is a full re-extraction of the
    /// mailbox for no change at all. Reading the guard rather than the switch alone is what makes that a no-op.
    /// </remarks>
    private SensitiveContentDerivationStamp? RebuiltTowards =>
        options.RebuildsStaleDerivedData ? derivationGuard.Stamp : null;

    /// <inheritdoc />
    /// <remarks>
    /// A position reached under a different sensitive-content configuration is discarded rather than resumed from. The
    /// walk skips a message it cannot re-read — one whose raw MIME is gone, or that parses for no reader — and such a
    /// row keeps its old stamp forever, so a cursor left where the previous configuration's walk finished would sit past
    /// every message the new one has to revisit.
    /// </remarks>
    public async Task<StoredEmailId?> FindResumePositionAsync(CancellationToken cancellationToken)
    {
        var recorded = await dbContext.BackfillPositions
            .AsNoTracking()
            .Where(candidate => candidate.Name == BackfillPositionEntity.StoredEmailExtractionName)
            .Select(candidate => new RecordedPosition(
                candidate.LastProcessedStoredEmailId,
                candidate.SensitiveContentStamp))
            .SingleOrDefaultAsync(cancellationToken);

        if (recorded is null)
        {
            return null;
        }

        if (this.RebuiltTowards is { } current && !string.Equals(recorded.Stamp, current.Value, StringComparison.Ordinal))
        {
            return null;
        }

        return StoredEmailId.Create(recorded.LastProcessedStoredEmailId);
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
        var candidates = await this.Outstanding()
            .Where(email => resumeAfterId == null || email.Id > resumeAfterId)
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
            derivationGuard.Stamp,
            cancellationToken);

        // Cut from the same extraction, so an email this walk reaches arrives at the same state a newly synchronized
        // one does rather than at a state a second walk would have to complete.
        await chunkWriter.SaveAsync(sessionContext, storedEmail, metadata.Text, cancellationToken);
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
                SensitiveContentStamp = this.RebuiltTowards?.Value,
            });

            return;
        }

        storedPosition.LastProcessedStoredEmailId = position.Value;
        storedPosition.UpdatedAt = recordedAt;

        // The cursor and the configuration it was reached under move together. A walk that is not rebuilding still
        // advances the position past rows a rebuild has to revisit, so it clears the stamp rather than leaving one a
        // later rebuild would read as everything behind here being done under that configuration.
        storedPosition.SensitiveContentStamp = this.RebuiltTowards?.Value;
    }

    /// <inheritdoc />
    public Task<int> CountEmailsWithStaleDerivedDataAsync(
        SensitiveContentDerivationStamp current,
        CancellationToken cancellationToken)
    {
        var currentStamp = current.Value;

        // The same conditions the walk selects on — a message that is not tombstoned and whose raw MIME is stored —
        // beside a document that holds derived body text whose stamp is not the current one. A message with no document
        // at all is left out here and is not: it has never been derived, so it holds no under-redacted text, and it is
        // already outstanding for the reason the backfill has always existed.
        return dbContext.StoredEmails
            .AsNoTracking()
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .Where(email => email.ContentAvailability == StoredEmailContentAvailability.Available
                && email.SearchDocument != null
                && email.SearchDocument.TextSource != ExtractedEmailTextSource.BodyNotExtracted
                && email.SearchDocument.SensitiveContentStamp != currentStamp)
            .CountAsync(cancellationToken);
    }

    /// <summary>Selects the messages this walk still owes work on, under the configuration it is walking for.</summary>
    /// <remarks>
    /// <para>
    /// Two shapes rather than one predicate carrying a flag, because they are two different questions and the deployment
    /// asking each is different. Without a rebuild the walk owes work only where extraction never ran, which is the
    /// original question and the query a deployment that scans nothing goes on issuing unchanged. With one it also owes
    /// work where the derived text was written under a configuration this deployment no longer runs — including the
    /// absent stamp, which is a document derived before any scanner was switched on and is exactly the case an operator
    /// enabling one late is asking about.
    /// </para>
    /// <para>
    /// A document recording that extraction never ran is left out of the rebuilding branch, because re-reading it
    /// produces nothing to write: its message is the one whose stored MIME no reader can parse, so a walk would fetch
    /// it, fail to read it, and leave the stamp exactly where it was on every pass forever. Such a row holds no derived
    /// body text and therefore nothing written under an older configuration to correct.
    /// </para>
    /// </remarks>
    private IQueryable<StoredEmailEntity> Outstanding()
    {
        var outstanding = dbContext.StoredEmails
            .AsNoTracking()
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .Where(email => email.ContentAvailability == StoredEmailContentAvailability.Available);

        if (this.RebuiltTowards is not { } current)
        {
            return outstanding.Where(email => email.SearchDocument == null);
        }

        var currentStamp = current.Value;

        return outstanding.Where(email => email.SearchDocument == null
            || (email.SearchDocument.TextSource != ExtractedEmailTextSource.BodyNotExtracted
                && email.SearchDocument.SensitiveContentStamp != currentStamp));
    }

    /// <summary>Where a previous walk stopped, and the configuration it stopped under.</summary>
    private sealed record RecordedPosition(Guid LastProcessedStoredEmailId, string? Stamp);
}
