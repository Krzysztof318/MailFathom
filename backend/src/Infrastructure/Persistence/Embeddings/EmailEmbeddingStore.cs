// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>EF Core store of the vectors a message's passages carry under one profile.</summary>
[RequiresIntegrationCoverage]
internal sealed class EmailEmbeddingStore(MailFathomDbContext dbContext, TimeProvider timeProvider) : IEmailEmbeddingStore
{
    /// <inheritdoc />
    /// <remarks>
    /// The outstanding passages are decided by the absence of a vector row for this profile rather than by a column on
    /// the chunk, so nothing has to be reset when a profile is activated and no second place can disagree with the
    /// vectors that actually exist.
    /// </remarks>
    public async Task<IReadOnlyList<EmailChunkAwaitingEmbedding>> GetChunksAwaitingEmbeddingAsync(
        StoredEmailId storedEmailId,
        EmbeddingProfileId profileId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);

        var passages = await dbContext.EmailChunks
            .AsNoTracking()
            .Where(chunk => chunk.StoredEmailId == storedEmailId.Value)
            .Where(chunk => !chunk.Embeddings.Any(embedding => embedding.EmbeddingProfileId == profileId.Value))
            .OrderBy(chunk => chunk.Ordinal)
            .Take(maxCount)
            .Select(chunk => new OutstandingPassageRow(chunk.Id, chunk.Text))
            .ToArrayAsync(cancellationToken);

        return [.. passages.Select(passage => new EmailChunkAwaitingEmbedding(
            EmailChunkId.Create(passage.Id),
            passage.Text))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The existing rows are read first and updated in place, so re-embedding a passage under the profile already
    /// serving it replaces one row rather than violating the key it is unique on; the read is what keeps the ordinary
    /// repeat from being a race at all. A losing writer in a genuine race reads none of the winner's rows inside its
    /// own transaction and so still collides at commit, which is why
    /// <see cref="PersistenceConcurrencyConflicts" /> names this key: the collision is classified as a
    /// conflict, and the caller's policy retries it from a fresh read that finds the winner's rows and updates them.
    /// </remarks>
    public async Task SaveEmbeddingsAsync(
        IPersistenceSession session,
        RegisteredEmbeddingProfile profile,
        IReadOnlyList<GeneratedChunkEmbedding> embeddings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(embeddings);

        if (embeddings.Count == 0)
        {
            return;
        }

        var sessionDbContext = EfCorePersistenceSessionAccessor.DbContextOf(session);
        var profileId = profile.Id.Value;
        Guid[] chunkIds = [.. embeddings.Select(embedding => embedding.ChunkId.Value)];

        var storedVectors = await sessionDbContext.EmailEmbeddings
            .Where(embedding => embedding.EmbeddingProfileId == profileId)
            .Where(embedding => chunkIds.Contains(embedding.EmailChunkId))
            .ToDictionaryAsync(embedding => embedding.EmailChunkId, cancellationToken);

        var generatedAt = timeProvider.GetUtcNow();

        foreach (var embedding in embeddings)
        {
            var vector = new Vector(embedding.Vector.Components);

            if (storedVectors.TryGetValue(embedding.ChunkId.Value, out var storedVector))
            {
                storedVector.Dimension = profile.Identity.Dimension;
                storedVector.Embedding = vector;
                storedVector.GeneratedAt = generatedAt;

                continue;
            }

            sessionDbContext.EmailEmbeddings.Add(new EmailEmbeddingEntity
            {
                EmailChunkId = embedding.ChunkId.Value,
                EmbeddingProfileId = profileId,

                // Written rather than inherited from the profile navigation, which is deliberately not assigned: the
                // width beside the vector is half of what the check constraint compares, and letting EF's key fixup
                // supply it would mean the row no longer states what this write believed it was storing.
                Dimension = profile.Identity.Dimension,
                Embedding = vector,
                GeneratedAt = generatedAt,
            });
        }
    }

    /// <summary>One outstanding passage, as the projection returns it.</summary>
    private sealed record OutstandingPassageRow(Guid Id, string Text);
}
