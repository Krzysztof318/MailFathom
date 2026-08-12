// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Embeddings.Backfill;
using MailFathom.Application.Emails.Extraction;
using MailFathom.Application.Folders;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>EF Core state for the sweep that gives pre-existing mail its passages and its vectors.</summary>
[RequiresIntegrationCoverage]
internal sealed class StoredEmailEmbeddingBackfillStore(
    MailFathomDbContext dbContext,
    TimeProvider timeProvider,
    EmailChunkWriter chunkWriter,
    IMailFolderParticipationReader folderParticipation)
    : IStoredEmailEmbeddingBackfillStore
{
    /// <inheritdoc />
    public async Task<StoredEmailId?> FindResumePositionAsync(CancellationToken cancellationToken)
    {
        var position = await dbContext.BackfillPositions
            .AsNoTracking()
            .Where(candidate => candidate.Name == BackfillPositionEntity.StoredEmailEmbeddingName)
            .Select(candidate => (Guid?)candidate.LastProcessedStoredEmailId)
            .SingleOrDefaultAsync(cancellationToken);

        return position is { } lastProcessed ? StoredEmailId.Create(lastProcessed) : null;
    }

    /// <inheritdoc />
    public Task<int> CountEmailsAwaitingEmbeddingAsync(
        EmbeddingProfileId profileId,
        CancellationToken cancellationToken) =>
        this.EmailsAwaitingEmbedding(profileId.Value).CountAsync(cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Ordering by the primary key gives the keyset comparison an index to walk and a total order that no later write
    /// disturbs, and both the ordering and the comparison are evaluated by PostgreSQL, so the walk runs under that
    /// server's <c>uuid</c> ordering rather than having to agree with how the CLR compares two <see cref="Guid" />
    /// values.
    /// </remarks>
    public async Task<IReadOnlyList<StoredEmailAwaitingEmbedding>> GetEmailsAwaitingEmbeddingAsync(
        StoredEmailId? resumeAfter,
        EmbeddingProfileId profileId,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var resumeAfterId = resumeAfter?.Value;
        var candidates = await this.EmailsAwaitingEmbedding(profileId.Value)
            .Where(email => resumeAfterId == null || email.Id > resumeAfterId)
            .OrderBy(email => email.Id)
            .Take(batchSize)
            .Select(email => new OutstandingEmailRow(email.Id, !email.Chunks.Any()))
            .ToArrayAsync(cancellationToken);

        return
        [
            .. candidates.Select(candidate => new StoredEmailAwaitingEmbedding(
                StoredEmailId.Create(candidate.Id),
                candidate.RequiresChunking)),
        ];
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Thrown when the email disappeared between the batch query and this write.</exception>
    /// <remarks>
    /// The text comes from the search document rather than from the raw MIME, which is what keeps this a local write: an
    /// earlier extraction already read the message and stored both the trimmed and the untrimmed reading, and cutting
    /// the stored reading again produces exactly the passages the same message would have been given had chunking
    /// existed when it arrived. A message whose extraction produced no text is left as it is, and the walk steps past
    /// it.
    /// </remarks>
    public async Task DeriveChunksAsync(
        IPersistenceSession session,
        StoredEmailId storedEmailId,
        CancellationToken cancellationToken)
    {
        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var storedEmail = await sessionContext.StoredEmails.FindAsync([storedEmailId.Value], cancellationToken)
            ?? throw new InvalidOperationException("Passages cannot be derived for a stored email that no longer exists.");

        var extraction = await sessionContext.EmailSearchDocuments
            .Where(document => document.StoredEmailId == storedEmailId.Value)
            .Select(document => new StoredExtractionRow(
                document.TextSource,
                document.BodyTextBeforeTrimming,
                document.BodyText))
            .SingleOrDefaultAsync(cancellationToken);

        if (extraction is null || RestoreExtractedText(extraction) is not { } text)
        {
            return;
        }

        await chunkWriter.SaveAsync(sessionContext, storedEmail, text, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveResumePositionAsync(
        IPersistenceSession session,
        StoredEmailId? position,
        CancellationToken cancellationToken)
    {
        var sessionContext = EfCorePersistenceSessionAccessor.DbContextOf(session);

        // FindAsync resolves a row this session already staged from the change tracker, so a run that commits several
        // positions through one session updates one row rather than inserting a second under the same key.
        var storedPosition = await sessionContext.BackfillPositions.FindAsync(
            [BackfillPositionEntity.StoredEmailEmbeddingName],
            cancellationToken);

        // The sweep ends by removing the row rather than by writing a sentinel into it, so "no position" has exactly one
        // representation and the reader that has never seen this backfill run cannot tell it apart from the one that
        // has finished a pass — which is correct, because both start at the beginning.
        if (position is not { } lastProcessed)
        {
            if (storedPosition is not null)
            {
                sessionContext.BackfillPositions.Remove(storedPosition);
            }

            return;
        }

        var recordedAt = timeProvider.GetUtcNow();

        if (storedPosition is null)
        {
            sessionContext.BackfillPositions.Add(new BackfillPositionEntity
            {
                Name = BackfillPositionEntity.StoredEmailEmbeddingName,
                LastProcessedStoredEmailId = lastProcessed.Value,
                UpdatedAt = recordedAt,
            });

            return;
        }

        storedPosition.LastProcessedStoredEmailId = lastProcessed.Value;
        storedPosition.UpdatedAt = recordedAt;
    }

    /// <summary>Selects the messages that are not current for one profile, whichever of the two reasons applies.</summary>
    /// <remarks>
    /// A message with a passage that carries no vector under this profile was stored before the profile existed, or was
    /// turned away by the live backlog's bound, or was left part-way through by a failed turn. A message with extracted
    /// text and no passages at all was stored before chunking existed, and nothing can be embedded for it until they are
    /// cut. A message an expunge has been observed for is in neither group: vectors nothing may retrieve are a provider
    /// bill with no reader.
    /// </remarks>
    /// <remarks>
    /// A folder configured not to embed is left out here as well as where its passages would have been cut, and the two
    /// answer different halves of one decision. The cut is what stops the passages existing; this is what stops the walk
    /// finding those messages outstanding on every sweep for the rest of the deployment's life, since a message with a
    /// body and no passages is exactly what the second group selects.
    /// </remarks>
    private IQueryable<StoredEmailEntity> EmailsAwaitingEmbedding(Guid profileId) => AccountScopedMailFolders.Excluding(
        dbContext.StoredEmails
            .AsNoTracking()
            .Where(StoredEmailTombstone.IsNotTombstoned)
            .Where(email => email.Chunks.Any(chunk =>
                    !chunk.Embeddings.Any(vector => vector.EmbeddingProfileId == profileId))
                || (!email.Chunks.Any()
                    && email.SearchDocument != null
                    && email.SearchDocument.BodyText != null)),
        folderParticipation.FoldersWithoutEmbeddings);

    /// <summary>Rebuilds the extraction the chunker reads from the two readings the search document stored.</summary>
    /// <remarks>
    /// Only the two sources that produced words can be restored, and both readings have to be there: the chunking rules
    /// choose between the trimmed and the untrimmed form, so restoring one of them and inventing the other would cut a
    /// backfilled message differently from the same message arriving today.
    /// </remarks>
    private static ExtractedEmailText? RestoreExtractedText(StoredExtractionRow extraction)
    {
        if (extraction.BodyTextBeforeTrimming is not { } originalText || extraction.BodyText is not { } trimmedText)
        {
            return null;
        }

        return extraction.TextSource switch
        {
            ExtractedEmailTextSource.PlainTextBodyPart => ExtractedEmailText.FromPlainTextBody(originalText, trimmedText),
            ExtractedEmailTextSource.DerivedFromHtmlBodyPart => ExtractedEmailText.DerivedFromHtmlBody(originalText, trimmedText),
            _ => null,
        };
    }

    /// <summary>One outstanding message, as the walk's projection returns it.</summary>
    private sealed record OutstandingEmailRow(Guid Id, bool RequiresChunking);

    /// <summary>The stored reading of one message's body, as the chunking projection returns it.</summary>
    private sealed record StoredExtractionRow(
        ExtractedEmailTextSource TextSource,
        string? BodyTextBeforeTrimming,
        string? BodyText);
}
