// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>The four content tables as the move reads and rewrites them: what is still here, what one row holds, and where it points.</summary>
/// <remarks>
/// <para>
/// The four payload kinds are four tables with four key columns and two names for the payload column, and nothing above
/// this type knows any of that. What crosses the port is a kind, an identity, a length, and a digest, which is the same
/// shape for all four — so the differences are four projections here rather than four use cases up there.
/// </para>
/// <para>
/// No query here is ordered by anything but the primary key, and none of them is indexed for the backend they filter on.
/// That is deliberate: while a move is running most rows are still database-backed, so a partial index on that branch
/// would be an index over nearly the whole of the mail this deployment holds, written once and read by one walk. The
/// walk reads in key order and the filter shrinks with it.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class StoredContentMoveStore(MailFathomDbContext dbContext) : IStoredContentMoveStore
{
    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="kind" /> names no payload kind, or <paramref name="batchSize" /> is not positive.</exception>
    public async Task<IReadOnlyList<DatabaseBackedPayload>> GetPayloadsToMoveAsync(
        EmailContentKind kind,
        Guid? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var payloads = this.DatabaseBacked(kind);

        if (resumeAfter is { } position)
        {
            payloads = payloads.Where(payload => payload.PayloadId > position);
        }

        var batch = await payloads
            .OrderBy(payload => payload.PayloadId)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        return [.. batch.Select(payload => new DatabaseBackedPayload(
            kind,
            payload.PayloadId,
            payload.ByteLength,
            payload.Sha256Hash))];
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="kind" /> names no payload kind.</exception>
    /// <remarks>
    /// Projected to the payload column rather than materialized as an entity, so a message is neither tracked nor kept
    /// alive by the change tracker once the move has let go of it. The backend is part of the predicate rather than
    /// checked afterwards, which is what makes a row that stopped being the database's read as absent.
    /// </remarks>
    public async Task<ReadOnlyMemory<byte>?> FindPayloadAsync(
        EmailContentKind kind,
        Guid payloadId,
        CancellationToken cancellationToken)
    {
        var payload = kind switch
        {
            EmailContentKind.IncomingMessage => await dbContext.EmailMessageContents
                .AsNoTracking()
                .Where(content => content.StoredEmailId == payloadId
                    && content.Backend == ContentStorageBackend.Database)
                .Select(content => content.RawMime)
                .SingleOrDefaultAsync(cancellationToken),
            EmailContentKind.OutgoingMessage => await dbContext.OutgoingEmailContents
                .AsNoTracking()
                .Where(content => content.OutgoingEmailId == payloadId
                    && content.Backend == ContentStorageBackend.Database)
                .Select(content => content.RawMime)
                .SingleOrDefaultAsync(cancellationToken),
            EmailContentKind.RecurringSendDraft => await dbContext.RecurringSendDrafts
                .AsNoTracking()
                .Where(draft => draft.RecurringSendId == payloadId
                    && draft.Backend == ContentStorageBackend.Database)
                .Select(draft => draft.DraftMime)
                .SingleOrDefaultAsync(cancellationToken),
            EmailContentKind.MailDraft => await dbContext.MailDraftContents
                .AsNoTracking()
                .Where(content => content.MailDraftId == payloadId
                    && content.Backend == ContentStorageBackend.Database)
                .Select(content => content.RawMime)
                .SingleOrDefaultAsync(cancellationToken),
            _ => throw UnknownKind(kind),
        };

        return payload is null ? null : new ReadOnlyMemory<byte>(payload);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when <paramref name="objectLocator" /> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="kind" /> names no payload kind.</exception>
    /// <remarks>
    /// One statement per row, issued outside any transaction this walk opened, which is what keeps the endpoint call
    /// that preceded it out of one. The predicate carries the backend, so a row a concurrent write has already repointed
    /// or replaced is left alone and reported as not moved rather than overwritten.
    /// </remarks>
    public async Task<bool> RepointAtObjectAsync(
        EmailContentKind kind,
        Guid payloadId,
        string objectLocator,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectLocator);

        var repointedRowCount = kind switch
        {
            EmailContentKind.IncomingMessage => await dbContext.EmailMessageContents
                .Where(content => content.StoredEmailId == payloadId
                    && content.Backend == ContentStorageBackend.Database)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(content => content.Backend, ContentStorageBackend.ObjectStorage)
                        .SetProperty(content => content.ObjectLocator, objectLocator)
                        .SetProperty(content => content.RawMime, (byte[]?)null),
                    cancellationToken),
            EmailContentKind.OutgoingMessage => await dbContext.OutgoingEmailContents
                .Where(content => content.OutgoingEmailId == payloadId
                    && content.Backend == ContentStorageBackend.Database)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(content => content.Backend, ContentStorageBackend.ObjectStorage)
                        .SetProperty(content => content.ObjectLocator, objectLocator)
                        .SetProperty(content => content.RawMime, (byte[]?)null),
                    cancellationToken),
            EmailContentKind.RecurringSendDraft => await dbContext.RecurringSendDrafts
                .Where(draft => draft.RecurringSendId == payloadId
                    && draft.Backend == ContentStorageBackend.Database)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(draft => draft.Backend, ContentStorageBackend.ObjectStorage)
                        .SetProperty(draft => draft.ObjectLocator, objectLocator)
                        .SetProperty(draft => draft.DraftMime, (byte[]?)null),
                    cancellationToken),
            EmailContentKind.MailDraft => await dbContext.MailDraftContents
                .Where(content => content.MailDraftId == payloadId
                    && content.Backend == ContentStorageBackend.Database)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(content => content.Backend, ContentStorageBackend.ObjectStorage)
                        .SetProperty(content => content.ObjectLocator, objectLocator)
                        .SetProperty(content => content.RawMime, (byte[]?)null),
                    cancellationToken),
            _ => throw UnknownKind(kind),
        };

        return repointedRowCount > 0;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Two aggregates per kind rather than one grouped query, because the pair is read on an operator's request and the
    /// simpler form is the one whose translation cannot surprise anybody. Nothing polls it.
    /// </remarks>
    public async Task<StoredContentBacklog> CountPayloadsAwaitingMoveAsync(CancellationToken cancellationToken)
    {
        var backlog = StoredContentBacklog.Empty;

        foreach (var kind in Enum.GetValues<EmailContentKind>())
        {
            var payloads = this.DatabaseBacked(kind);

            backlog = new StoredContentBacklog(
                backlog.PayloadCount + await payloads.CountAsync(cancellationToken),
                backlog.ByteCount + await payloads.SumAsync(payload => payload.ByteLength, cancellationToken));
        }

        return backlog;
    }

    private static ArgumentOutOfRangeException UnknownKind(EmailContentKind kind) =>
        new(nameof(kind), kind, "The payload kind names no table of stored content.");

    /// <summary>Reads one payload kind's database-backed rows in the one shape the move works in.</summary>
    /// <remarks>
    /// The projection is what lets the walk, the batch, and the backlog be written once over four tables whose key
    /// columns and payload columns are named differently. It carries no payload, because every caller of it wants to
    /// know which rows are there rather than what they hold.
    /// </remarks>
    private IQueryable<ContentPayloadRow> DatabaseBacked(EmailContentKind kind) => kind switch
    {
        EmailContentKind.IncomingMessage => dbContext.EmailMessageContents
            .AsNoTracking()
            .Where(content => content.Backend == ContentStorageBackend.Database)
            .Select(content => new ContentPayloadRow
            {
                PayloadId = content.StoredEmailId,
                ByteLength = content.MimeByteLength,
                Sha256Hash = content.Sha256Hash,
            }),
        EmailContentKind.OutgoingMessage => dbContext.OutgoingEmailContents
            .AsNoTracking()
            .Where(content => content.Backend == ContentStorageBackend.Database)
            .Select(content => new ContentPayloadRow
            {
                PayloadId = content.OutgoingEmailId,
                ByteLength = content.MimeByteLength,
                Sha256Hash = content.Sha256Hash,
            }),
        EmailContentKind.RecurringSendDraft => dbContext.RecurringSendDrafts
            .AsNoTracking()
            .Where(draft => draft.Backend == ContentStorageBackend.Database)
            .Select(draft => new ContentPayloadRow
            {
                PayloadId = draft.RecurringSendId,
                ByteLength = draft.DraftByteLength,
                Sha256Hash = draft.Sha256Hash,
            }),
        EmailContentKind.MailDraft => dbContext.MailDraftContents
            .AsNoTracking()
            .Where(content => content.Backend == ContentStorageBackend.Database)
            .Select(content => new ContentPayloadRow
            {
                PayloadId = content.MailDraftId,
                ByteLength = content.MimeByteLength,
                Sha256Hash = content.Sha256Hash,
            }),
        _ => throw UnknownKind(kind),
    };

    /// <summary>One content row as every one of the four tables can answer it, with the payload left where it is.</summary>
    private sealed class ContentPayloadRow
    {
        public Guid PayloadId { get; init; }

        public long ByteLength { get; init; }

        public byte[] Sha256Hash { get; init; } = [];
    }
}
