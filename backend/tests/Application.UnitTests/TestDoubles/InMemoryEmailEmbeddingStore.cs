// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds a message's passages and whichever of them have a vector, the way the real store decides both.</summary>
/// <remarks>
/// Hand-written rather than substituted because what the tests are about is the relationship between the two halves of
/// the port: what the read reports as outstanding has to stop being outstanding once the write has stored it, and a
/// substitute configured to answer each call separately could not be wrong about that.
/// </remarks>
internal sealed class InMemoryEmailEmbeddingStore : IEmailEmbeddingStore
{
    private readonly Dictionary<StoredEmailId, List<EmailChunkAwaitingEmbedding>> passages = [];
    private readonly Dictionary<(EmailChunkId ChunkId, EmbeddingProfileId ProfileId), EmbeddingVector> vectors = [];
    private readonly List<EmailChunkId> vectorWriteOrder = [];

    /// <summary>Gets how many reads of outstanding passages have been served.</summary>
    public int ReadCount { get; private set; }

    /// <summary>Gets how many separate writes have been committed.</summary>
    public int WriteCount { get; private set; }

    /// <summary>Gets the passages that now carry a vector, in the order the writes stored them.</summary>
    public IReadOnlyList<EmailChunkId> EmbeddedPassages => this.vectorWriteOrder;

    /// <summary>Gets the vectors stored so far, by passage and profile.</summary>
    public IReadOnlyDictionary<(EmailChunkId ChunkId, EmbeddingProfileId ProfileId), EmbeddingVector> StoredVectors =>
        this.vectors;

    /// <summary>Removes a bounded batch of one generation's vectors, the way the superseded-vector sweep does.</summary>
    /// <returns>How many vectors the batch removed.</returns>
    public int RemoveVectors(EmbeddingProfileId profileId, int batchSize)
    {
        var batch = this.vectors.Keys
            .Where(key => key.ProfileId == profileId)
            .Take(batchSize)
            .ToArray();

        // A loop rather than a projection, because removing an entry is a side effect on the dictionary being read.
        foreach (var key in batch)
        {
            this.vectors.Remove(key);
        }

        return batch.Length;
    }

    /// <summary>Counts the vectors one generation currently holds.</summary>
    public int CountVectors(EmbeddingProfileId profileId) =>
        this.vectors.Keys.Count(key => key.ProfileId == profileId);

    /// <summary>Gives one message the passages a chunker would have derived for it.</summary>
    public void AddPassages(StoredEmailId storedEmailId, params IReadOnlyList<EmailChunkAwaitingEmbedding> chunks)
    {
        if (!this.passages.TryGetValue(storedEmailId, out var existing))
        {
            existing = [];
            this.passages[storedEmailId] = existing;
        }

        existing.AddRange(chunks);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EmailChunkAwaitingEmbedding>> GetChunksAwaitingEmbeddingAsync(
        StoredEmailId storedEmailId,
        EmbeddingProfileId profileId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);
        cancellationToken.ThrowIfCancellationRequested();

        this.ReadCount++;

        if (!this.passages.TryGetValue(storedEmailId, out var messagePassages))
        {
            return Task.FromResult<IReadOnlyList<EmailChunkAwaitingEmbedding>>([]);
        }

        IReadOnlyList<EmailChunkAwaitingEmbedding> outstanding =
        [
            .. messagePassages
                .Where(passage => !this.vectors.ContainsKey((passage.Id, profileId)))
                .Take(maxCount),
        ];

        return Task.FromResult(outstanding);
    }

    /// <inheritdoc />
    public Task SaveEmbeddingsAsync(
        IPersistenceSession session,
        RegisteredEmbeddingProfile profile,
        IReadOnlyList<GeneratedChunkEmbedding> embeddings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(embeddings);
        cancellationToken.ThrowIfCancellationRequested();

        this.WriteCount++;

        foreach (var embedding in embeddings)
        {
            if (this.vectors.TryAdd((embedding.ChunkId, profile.Id), embedding.Vector))
            {
                this.vectorWriteOrder.Add(embedding.ChunkId);

                continue;
            }

            this.vectors[(embedding.ChunkId, profile.Id)] = embedding.Vector;
        }

        return Task.CompletedTask;
    }
}
