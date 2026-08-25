// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Release;
using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>The copies the database is still holding beside verified objects, and the freeing of a bounded batch of them.</summary>
/// <remarks>
/// A row carries the instant the move vouched for its object, because that is what the safety interval is measured from
/// and therefore the only reason a release leaves a copy where it is. Rows are freed in payload identity order, as the
/// real store's bounded read selects them, so a batch that runs out of bound leaves a stable remainder behind.
/// </remarks>
internal sealed class InMemoryRetainedContentReleaseStore : IRetainedContentReleaseStore
{
    private readonly List<RetainedRow> rows = [];
    private readonly List<Batch> batches = [];

    /// <summary>Gets every batch the release asked for, in the order it asked.</summary>
    internal IReadOnlyList<Batch> Batches => this.batches;

    /// <summary>Records one copy the database is holding beside an object the move verified.</summary>
    /// <param name="kind">The payload kind.</param>
    /// <param name="payloadId">The identity of the row.</param>
    /// <param name="byteLength">What the row records as the length of the payload it is holding.</param>
    /// <param name="verifiedAt">When the move vouched for the object this copy stands beside.</param>
    internal void Arrange(EmailContentKind kind, Guid payloadId, long byteLength, DateTimeOffset verifiedAt) =>
        this.rows.Add(new RetainedRow(kind, payloadId, byteLength, verifiedAt));

    /// <inheritdoc />
    public Task<StoredContentBacklog> CountRetainedPayloadsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new StoredContentBacklog(this.rows.Count, this.rows.Sum(row => row.ByteLength)));

    /// <inheritdoc />
    public Task<ReleasedContentPayloads> ReleaseAsync(
        EmailContentKind kind,
        DateTimeOffset verifiedOnOrBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        this.batches.Add(new Batch(kind, verifiedOnOrBefore, batchSize));

        List<RetainedRow> freed =
        [
            .. this.rows
                .Where(row => row.Kind == kind && row.VerifiedAt <= verifiedOnOrBefore)
                .OrderBy(row => row.PayloadId)
                .Take(batchSize),
        ];

        freed.ForEach(row => this.rows.Remove(row));

        return Task.FromResult(new ReleasedContentPayloads(freed.Count, freed.Sum(row => row.ByteLength)));
    }

    /// <summary>One batch the release asked for, with the bound and the cutoff it asked under.</summary>
    /// <param name="Kind">The payload kind the batch was asked for.</param>
    /// <param name="VerifiedOnOrBefore">The cutoff the safety interval produced.</param>
    /// <param name="BatchSize">What was left of the request's bound when this kind was reached.</param>
    internal sealed record Batch(EmailContentKind Kind, DateTimeOffset VerifiedOnOrBefore, int BatchSize);

    private sealed record RetainedRow(
        EmailContentKind Kind,
        Guid PayloadId,
        long ByteLength,
        DateTimeOffset VerifiedAt);
}
