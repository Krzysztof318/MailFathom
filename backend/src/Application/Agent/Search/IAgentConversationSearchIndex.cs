// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Search;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Agent.Search;

/// <summary>The two rankings a person's Agent history is searched by, and the vectors the second one reads.</summary>
/// <remarks>
/// <para>
/// <strong>What each ranking reads.</strong> The lexical one reads every message's own text and every block's fields;
/// the semantic one reads only what was embedded, which is a person's own words and the agent's prose. A block renders
/// an object that lives elsewhere and is searched there, so ranking a conversation by the meaning of a calendar entry it
/// happened to show would place it for something nobody said in it.
/// </para>
/// <para>
/// <strong>Both are scoped to one person and exact.</strong> The corpus is that person's own conversations, bounded by
/// the conversation cap rather than by a mailbox, so every distance is measured and no approximate index stands between
/// the query and the answer.
/// </para>
/// <para>
/// What is read and written here is somebody's own words, so nothing reaches a log, a span, a metric, or a failure
/// message — the query included.
/// </para>
/// </remarks>
public interface IAgentConversationSearchIndex
{
    /// <summary>Ranks one person's conversations by how well their words match the query.</summary>
    /// <param name="user">Whose conversations to rank, which are the only ones read.</param>
    /// <param name="queryText">The validated query.</param>
    /// <param name="limit">The greatest number of conversations to return, at least one.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The conversations with a matching entry, best first and more recent first among equals, each at its best-matching entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="queryText" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is below one.</exception>
    Task<IReadOnlyList<AgentConversationSearchHit>> ReadLexicalRankingAsync(
        UserId user,
        EmailSearchQueryText queryText,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Ranks one person's conversations by how near their embedded messages sit to the query.</summary>
    /// <param name="user">Whose conversations to rank, which are the only ones read.</param>
    /// <param name="profile">The space the query was placed in, which is the only one whose vectors are read.</param>
    /// <param name="queryVector">Where the query sits in that space.</param>
    /// <param name="limit">The greatest number of conversations to return, at least one.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The conversations holding a vector in that space, nearest first and more recent first among equals, each at its nearest entry.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile" /> or <paramref name="queryVector" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is below one.</exception>
    /// <remarks>A conversation with no vector in that space is absent here and present in the lexical ranking, which is what keeps one written before its messages were embedded findable.</remarks>
    Task<IReadOnlyList<AgentConversationSearchHit>> ReadNearestRankingAsync(
        UserId user,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Records where one written message sits in a space.</summary>
    /// <param name="conversation">The conversation holding the message.</param>
    /// <param name="user">The person whose conversation it has to be.</param>
    /// <param name="sequence">The place of the entry the text was read from.</param>
    /// <param name="profile">The space the vector was placed in.</param>
    /// <param name="vector">Where the message sits in that space.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes once the vector is recorded, or once nothing was, where the entry is gone or is not this person's.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="profile" /> or <paramref name="vector" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The vector hangs off the entry it was read from, so it goes when the conversation does, by the same cascade every
    /// entry goes by. A conversation deleted while its answer was being embedded is a vector with nothing to hang off,
    /// which is not a fault; recording one already recorded writes nothing.
    /// </remarks>
    Task SaveEmbeddingAsync(
        AgentConversationId conversation,
        UserId user,
        long sequence,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector vector,
        CancellationToken cancellationToken);
}
