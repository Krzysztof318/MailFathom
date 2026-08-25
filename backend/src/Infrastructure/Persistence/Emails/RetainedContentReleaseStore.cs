// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Release;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Emails;

/// <summary>The four content tables as the release reads and empties them: what is duplicated, and the freeing of it.</summary>
/// <remarks>
/// <para>
/// The four payload kinds are four tables with four key columns and two names for the payload column, and nothing above
/// this type knows any of that. What crosses the port is a kind, a cutoff, and a bound, which is the same shape for all
/// four — so the differences are four projections here rather than four use cases up there, exactly as they are for the
/// move.
/// </para>
/// <para>
/// No query here is indexed for the branch it filters on, deliberately and for the reason the move's are not: while
/// copies are being released most object-backed rows still carry one, so a partial index on that branch would be an
/// index over nearly the whole of the mail this deployment holds, written once and read by one operator's walk. Each
/// batch reads in key order and the set it filters shrinks with every batch.
/// </para>
/// <para>
/// Freeing a column returns the space to PostgreSQL rather than to the volume. What falls immediately is what a new
/// backup has to carry; the file system follows the database's own reclamation, which is the operator's maintenance and
/// not this type's business.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class RetainedContentReleaseStore(MailFathomDbContext dbContext) : IRetainedContentReleaseStore
{
    /// <inheritdoc />
    /// <remarks>
    /// Two aggregates per kind rather than one grouped query, because the pair is read on an operator's request and the
    /// simpler form is the one whose translation cannot surprise anybody. Nothing polls it.
    /// </remarks>
    public async Task<StoredContentBacklog> CountRetainedPayloadsAsync(CancellationToken cancellationToken)
    {
        var retained = StoredContentBacklog.Empty;

        foreach (var kind in Enum.GetValues<EmailContentKind>())
        {
            var payloads = this.Retained(kind);

            retained = new StoredContentBacklog(
                retained.PayloadCount + await payloads.CountAsync(cancellationToken),
                retained.ByteCount + await payloads.SumAsync(payload => payload.ByteLength, cancellationToken));
        }

        return retained;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Two statements rather than one, because <c>ExecuteUpdate</c> takes no bound of its own: the batch is read first,
    /// which is also what lets the volume be reported at all, and the freeing statement then names exactly the rows that
    /// read returned. Its predicate carries the backend and the payload again, so a row a concurrent write replaced
    /// between the two is left alone rather than emptied on a message it no longer describes.
    /// </para>
    /// <para>
    /// Two releases running at once is therefore exact in the count and approximate in the volume: the update reports
    /// how many rows it matched, so a row the other request freed first is not counted twice, while the byte total is
    /// summed over the batch that was read and does include it. Reporting both exactly would mean returning each freed
    /// row's length from the update itself, which no LINQ translation expresses and which would put four hand-written
    /// <c>UPDATE</c> statements in the one path whose defect is the loss of mail. A typed statement that cannot name the
    /// wrong table is worth more than a counter that is exact while two operators are disposing of the same copies at
    /// the same moment, so the volume is read as what a release covered rather than as an accountant's figure.
    /// </para>
    /// </remarks>
    public async Task<ReleasedContentPayloads> ReleaseAsync(
        EmailContentKind kind,
        DateTimeOffset verifiedOnOrBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var batch = await this.Retained(kind)
            .Where(payload => payload.ObjectVerifiedAt <= verifiedOnOrBefore)
            .OrderBy(payload => payload.PayloadId)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (batch.Count == 0)
        {
            return ReleasedContentPayloads.None;
        }

        var payloadIds = batch.Select(payload => payload.PayloadId).ToArray();
        var freedRowCount = await this.FreeAsync(kind, payloadIds, cancellationToken);

        return new ReleasedContentPayloads(freedRowCount, batch.Sum(payload => payload.ByteLength));
    }

    private static ArgumentOutOfRangeException UnknownKind(EmailContentKind kind) =>
        new(nameof(kind), kind, "The payload kind names no table of stored content.");

    /// <summary>Empties the payload column of the named rows, leaving each of them pointing at its object.</summary>
    private async Task<int> FreeAsync(
        EmailContentKind kind,
        Guid[] payloadIds,
        CancellationToken cancellationToken) => kind switch
        {
            EmailContentKind.IncomingMessage => await dbContext.EmailMessageContents
                .Where(content => payloadIds.Contains(content.StoredEmailId)
                    && content.Backend == ContentStorageBackend.ObjectStorage
                    && content.RawMime != null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(content => content.RawMime, (byte[]?)null),
                    cancellationToken),
            EmailContentKind.OutgoingMessage => await dbContext.OutgoingEmailContents
                .Where(content => payloadIds.Contains(content.OutgoingEmailId)
                    && content.Backend == ContentStorageBackend.ObjectStorage
                    && content.RawMime != null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(content => content.RawMime, (byte[]?)null),
                    cancellationToken),
            EmailContentKind.RecurringSendDraft => await dbContext.RecurringSendDrafts
                .Where(draft => payloadIds.Contains(draft.RecurringSendId)
                    && draft.Backend == ContentStorageBackend.ObjectStorage
                    && draft.DraftMime != null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(draft => draft.DraftMime, (byte[]?)null),
                    cancellationToken),
            EmailContentKind.MailDraft => await dbContext.MailDraftContents
                .Where(content => payloadIds.Contains(content.MailDraftId)
                    && content.Backend == ContentStorageBackend.ObjectStorage
                    && content.RawMime != null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(content => content.RawMime, (byte[]?)null),
                    cancellationToken),
            _ => throw UnknownKind(kind),
        };

    /// <summary>Reads one payload kind's rows that point at an object and still carry the copy the move left behind.</summary>
    /// <remarks>
    /// The projection is what lets the count and the batch be written once over four tables whose key columns and
    /// payload columns are named differently. It carries no payload, because both callers want to know which rows are
    /// there and how much they hold rather than what is in them.
    /// </remarks>
    private IQueryable<RetainedPayloadRow> Retained(EmailContentKind kind) => kind switch
    {
        EmailContentKind.IncomingMessage => dbContext.EmailMessageContents
            .AsNoTracking()
            .Where(content => content.Backend == ContentStorageBackend.ObjectStorage && content.RawMime != null)
            .Select(content => new RetainedPayloadRow
            {
                PayloadId = content.StoredEmailId,
                ByteLength = content.MimeByteLength,
                ObjectVerifiedAt = content.ObjectVerifiedAt,
            }),
        EmailContentKind.OutgoingMessage => dbContext.OutgoingEmailContents
            .AsNoTracking()
            .Where(content => content.Backend == ContentStorageBackend.ObjectStorage && content.RawMime != null)
            .Select(content => new RetainedPayloadRow
            {
                PayloadId = content.OutgoingEmailId,
                ByteLength = content.MimeByteLength,
                ObjectVerifiedAt = content.ObjectVerifiedAt,
            }),
        EmailContentKind.RecurringSendDraft => dbContext.RecurringSendDrafts
            .AsNoTracking()
            .Where(draft => draft.Backend == ContentStorageBackend.ObjectStorage && draft.DraftMime != null)
            .Select(draft => new RetainedPayloadRow
            {
                PayloadId = draft.RecurringSendId,
                ByteLength = draft.DraftByteLength,
                ObjectVerifiedAt = draft.ObjectVerifiedAt,
            }),
        EmailContentKind.MailDraft => dbContext.MailDraftContents
            .AsNoTracking()
            .Where(content => content.Backend == ContentStorageBackend.ObjectStorage && content.RawMime != null)
            .Select(content => new RetainedPayloadRow
            {
                PayloadId = content.MailDraftId,
                ByteLength = content.MimeByteLength,
                ObjectVerifiedAt = content.ObjectVerifiedAt,
            }),
        _ => throw UnknownKind(kind),
    };

    /// <summary>One duplicated content row as every one of the four tables can answer it, with the copy left where it is.</summary>
    private sealed class RetainedPayloadRow
    {
        public Guid PayloadId { get; init; }

        public long ByteLength { get; init; }

        /// <summary>Gets when the move vouched for this row's object, which the check constraint makes non-null here.</summary>
        public DateTimeOffset? ObjectVerifiedAt { get; init; }
    }
}
