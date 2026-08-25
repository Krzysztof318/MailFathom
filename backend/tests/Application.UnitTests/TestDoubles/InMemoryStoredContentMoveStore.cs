// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Security.Cryptography;
using MailFathom.Application.EmailContent.Move;
using MailFathom.Application.EmailContent.Storage;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>The four content tables as the move sees them, with the payloads and what each row records about them.</summary>
/// <remarks>
/// Rows are held in the order they were arranged and are walked by identity, exactly as the real store walks a primary
/// key. A row keeps its recorded length and digest independently of its bytes, which is what lets a stored payload that
/// disagrees with its own row be arranged at all.
/// </remarks>
internal sealed class InMemoryStoredContentMoveStore : IStoredContentMoveStore
{
    private readonly List<StoredRow> rows = [];
    private readonly List<(EmailContentKind Kind, Guid PayloadId, string ObjectLocator)> repoints = [];

    /// <summary>Gets every repoint the move asked for, in the order it asked.</summary>
    internal IReadOnlyList<(EmailContentKind Kind, Guid PayloadId, string ObjectLocator)> Repoints => this.repoints;

    /// <summary>Gets or sets whether a repoint finds the row still database-backed.</summary>
    /// <remarks>Off is the race the real predicate loses: a concurrent write replaced the payload while the object was being written.</remarks>
    internal bool RepointSucceeds { get; set; } = true;

    /// <summary>Records one payload of one kind, as the row holds it and as the row describes it.</summary>
    /// <param name="kind">The payload kind.</param>
    /// <param name="payloadId">The identity of the row.</param>
    /// <param name="rawMime">The bytes the row holds, or <see langword="null" /> for a row the move will not find a payload in.</param>
    /// <param name="recordedByteLength">What the row records as the length, defaulting to the payload's own.</param>
    /// <param name="recordedSha256Hash">What the row records as the digest, defaulting to the payload's own.</param>
    internal void Arrange(
        EmailContentKind kind,
        Guid payloadId,
        byte[]? rawMime,
        long? recordedByteLength = null,
        byte[]? recordedSha256Hash = null)
    {
        this.rows.Add(new StoredRow(
            kind,
            payloadId,
            rawMime,
            recordedByteLength ?? rawMime?.LongLength ?? 0,
            recordedSha256Hash ?? SHA256.HashData(rawMime ?? [])));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<DatabaseBackedPayload>> GetPayloadsToMoveAsync(
        EmailContentKind kind,
        Guid? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DatabaseBackedPayload> batch =
        [
            .. this.rows
                .Where(row => row.Kind == kind && !row.Moved)
                .Where(row => resumeAfter is not { } position || row.PayloadId.CompareTo(position) > 0)
                .OrderBy(row => row.PayloadId)
                .Take(batchSize)
                .Select(row => new DatabaseBackedPayload(
                    row.Kind,
                    row.PayloadId,
                    row.RecordedByteLength,
                    row.RecordedSha256Hash)),
        ];

        return Task.FromResult(batch);
    }

    /// <inheritdoc />
    public Task<ReadOnlyMemory<byte>?> FindPayloadAsync(
        EmailContentKind kind,
        Guid payloadId,
        CancellationToken cancellationToken)
    {
        var row = this.rows.SingleOrDefault(candidate => candidate.Kind == kind && candidate.PayloadId == payloadId);

        return Task.FromResult(row is { Moved: false, RawMime: { } rawMime }
            ? new ReadOnlyMemory<byte>(rawMime)
            : (ReadOnlyMemory<byte>?)null);
    }

    /// <inheritdoc />
    public Task<bool> RepointAtObjectAsync(
        EmailContentKind kind,
        Guid payloadId,
        string objectLocator,
        CancellationToken cancellationToken)
    {
        this.repoints.Add((kind, payloadId, objectLocator));

        if (!this.RepointSucceeds)
        {
            return Task.FromResult(false);
        }

        var row = this.rows.Single(candidate => candidate.Kind == kind && candidate.PayloadId == payloadId);
        row.Moved = true;

        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<StoredContentBacklog> CountPayloadsAwaitingMoveAsync(CancellationToken cancellationToken)
    {
        var remaining = this.rows.Where(row => !row.Moved).ToList();

        return Task.FromResult(new StoredContentBacklog(
            remaining.Count,
            remaining.Sum(row => row.RecordedByteLength)));
    }

    /// <summary>One row of one content table, and whether the move has already carried it.</summary>
    private sealed class StoredRow(
        EmailContentKind kind,
        Guid payloadId,
        byte[]? rawMime,
        long recordedByteLength,
        byte[] recordedSha256Hash)
    {
        public EmailContentKind Kind { get; } = kind;

        public Guid PayloadId { get; } = payloadId;

        public byte[]? RawMime { get; } = rawMime;

        public long RecordedByteLength { get; } = recordedByteLength;

        public byte[] RecordedSha256Hash { get; } = recordedSha256Hash;

        public bool Moved { get; set; }
    }
}
