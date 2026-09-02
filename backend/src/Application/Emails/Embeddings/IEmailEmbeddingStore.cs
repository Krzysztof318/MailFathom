// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Emails.Embeddings;

/// <summary>Reads which of a message's passages still lack a vector, and writes the vectors that answer.</summary>
/// <remarks>
/// The two halves are one port because they are two ends of one decision: what the read reports as outstanding is
/// exactly what the write makes no longer outstanding, and a second implementation that answered the first question
/// differently from the second would re-embed passages that already have vectors or leave passages without one forever.
/// </remarks>
public interface IEmailEmbeddingStore
{
    /// <summary>Reads a bounded page of one message's passages that carry no vector under the given profile.</summary>
    /// <param name="storedEmailId">The message whose passages are being embedded.</param>
    /// <param name="profileId">The profile the passages are being embedded into.</param>
    /// <param name="maxCount">The greatest number of passages to return, which the caller sets from what one provider call accepts.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The outstanding passages in reading order, or an empty list when the message is already current for this profile.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxCount" /> is not positive.</exception>
    /// <remarks>
    /// Ordered by the passage's position in its message, so a message embedded across several calls is filled in reading
    /// order and an interrupted run leaves a prefix rather than a scatter. The query joins no transaction: it reads
    /// committed state, which is what lets a provider call happen outside any open one.
    /// </remarks>
    Task<IReadOnlyList<EmailChunkAwaitingEmbedding>> GetChunksAwaitingEmbeddingAsync(
        StoredEmailId storedEmailId,
        EmbeddingProfileId profileId,
        int maxCount,
        CancellationToken cancellationToken);

    /// <summary>Stores the vectors one generation produced, inside the caller's open session.</summary>
    /// <param name="session">The session whose transaction this write joins.</param>
    /// <param name="profile">The profile the vectors belong to, whose dimension is written beside each of them.</param>
    /// <param name="embeddings">The passage and vector pairs to store.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A task that completes when the writes have been staged.</returns>
    /// <exception cref="ArgumentNullException">Thrown when any reference argument is <see langword="null" />.</exception>
    /// <remarks>
    /// The write is an upsert over the passage and profile together, so embedding a passage twice under one profile
    /// replaces its vector rather than adding a second one, and a run interrupted mid-message re-embeds only the
    /// passages it never reached. Committing the vectors together is what keeps a crash from leaving a message that
    /// looks embedded and is not: either the whole batch is durable or none of it is.
    /// </remarks>
    Task SaveEmbeddingsAsync(
        IPersistenceSession session,
        RegisteredEmbeddingProfile profile,
        IReadOnlyList<GeneratedChunkEmbedding> embeddings,
        CancellationToken cancellationToken);
}
