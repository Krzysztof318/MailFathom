// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Emails.Search;
using MailFathom.Domain.Access;

namespace MailFathom.Application.Agent.Search;

/// <summary>Searches a person's Agent history the way mail is searched: by words and by meaning, fused by rank.</summary>
/// <remarks>
/// <para>
/// Nothing about the ranking is invented here. The query is placed through <see cref="ActiveEmbeddingSpace" />, which
/// mail search places its query through, and the two rankings are combined by <see cref="ReciprocalRankFusion" />,
/// which reads only where each ranking put a conversation and nothing of the numbers behind it. What is this search's
/// own is what settles a tie between two fused scores: the conversation's own recency, as the timeline settles a tie in
/// mail, so one query returns one sequence.
/// </para>
/// <para>
/// <strong>An embedding provider that cannot be reached costs ranking quality, never the answer.</strong> The search
/// then returns the lexical ordering and says so, and a conversation whose messages were never embedded ranks lexically
/// in a hybrid search too, since fusion scores whatever either ranking returned.
/// </para>
/// </remarks>
public sealed class AgentConversationSearch
{
    /// <summary>The greatest number of conversations one search returns.</summary>
    public const int MaximumResults = 50;

    /// <summary>How deep each ranking is read before the two are fused.</summary>
    /// <remarks>
    /// Several times the deepest answer, because fusion can only reorder what the rankings returned: a conversation
    /// second in one ranking and absent from the other is still worth a place, and one cut from a ranking read no deeper
    /// than the answer could not be found by agreement at all. Against a person's own conversations, of which there are
    /// at most <see cref="AgentConversationBounds.MaximumConversations" />, it is a bound on what one query reads rather
    /// than a cost worth tuning.
    /// </remarks>
    internal const int RankingDepth = 200;

    private readonly ActiveEmbeddingSpace space;
    private readonly IAgentConversationSearchIndex index;

    /// <summary>Initializes the search over this deployment's index and its active vector space.</summary>
    /// <param name="space">Places the query beside the stored vectors, or reports why it cannot.</param>
    /// <param name="index">Reads the two rankings.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="space" /> or <paramref name="index" /> is <see langword="null" />.</exception>
    public AgentConversationSearch(ActiveEmbeddingSpace space, IAgentConversationSearchIndex index)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(index);

        this.space = space;
        this.index = index;
    }

    /// <summary>Searches one person's conversations.</summary>
    /// <param name="user">Whose history to search, which is the only one read.</param>
    /// <param name="queryText">The validated query.</param>
    /// <param name="limit">The greatest number of conversations to return, from one to <see cref="MaximumResults" />.</param>
    /// <param name="cancellationToken">Cancels the search.</param>
    /// <returns>The conversations found, best first, and the mode that ordered them.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="queryText" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is outside its bound.</exception>
    public async Task<AgentConversationSearchResult> SearchAsync(
        UserId user,
        EmailSearchQueryText queryText,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, MaximumResults);

        var placement = await this.space.PlaceAsync([queryText.Value], cancellationToken);
        var lexical = await this.index.ReadLexicalRankingAsync(user, queryText, RankingDepth, cancellationToken);

        if (placement is not { Profile: { } profile, Vectors: [var queryVector] })
        {
            return new AgentConversationSearchResult(
                [.. lexical.Take(limit)],
                EmailSearchRetrievalMode.Lexical,
                placement.Capability);
        }

        var semantic = await this.index.ReadNearestRankingAsync(
            user,
            profile,
            queryVector,
            RankingDepth,
            cancellationToken);

        return new AgentConversationSearchResult(
            Fuse(lexical, semantic, limit),
            EmailSearchRetrievalMode.Hybrid,
            SemanticSearchCapability.Available);
    }

    /// <summary>Orders what either ranking found by fused score, the more recent conversation first among equals.</summary>
    /// <remarks>The identifier settles the rest, so two conversations moved in the same instant still come back in one order.</remarks>
    private static AgentConversationSearchHit[] Fuse(
        IReadOnlyList<AgentConversationSearchHit> lexical,
        IReadOnlyList<AgentConversationSearchHit> semantic,
        int limit)
    {
        var scores = ReciprocalRankFusion.Score(
            [.. lexical.Select(static hit => hit.Conversation)],
            [.. semantic.Select(static hit => hit.Conversation)]);
        var matched = MatchedAt(lexical, semantic);

        return
        [
            .. scores
                .Select(scored => (Hit: matched[scored.Key], Score: scored.Value))
                .OrderByDescending(static fused => fused.Score)
                .ThenByDescending(static fused => fused.Hit.LastActivityAt)
                .ThenBy(static fused => fused.Hit.Conversation.Value)
                .Select(static fused => fused.Hit)
                .Take(limit),
        ];
    }

    /// <summary>Chooses, for each conversation, the message a result points at.</summary>
    /// <remarks>
    /// The ranking that placed the conversation higher names it, and the lexical one where the two placed it equally,
    /// because a message carrying the words somebody typed is the more certain place to open a conversation at than one
    /// that is only near them in meaning.
    /// </remarks>
    private static Dictionary<AgentConversationId, AgentConversationSearchHit> MatchedAt(
        IReadOnlyList<AgentConversationSearchHit> lexical,
        IReadOnlyList<AgentConversationSearchHit> semantic)
    {
        var placed = new Dictionary<AgentConversationId, (AgentConversationSearchHit Hit, int Place)>();

        // A loop rather than a grouping: the decision reads what the dictionary already holds, and the lexical ranking
        // is walked first so a strict comparison is what leaves it in place on a tie.
        foreach (var (hit, place) in lexical.Select(Placed).Concat(semantic.Select(Placed)))
        {
            if (!placed.TryGetValue(hit.Conversation, out var held) || place < held.Place)
            {
                placed[hit.Conversation] = (hit, place);
            }
        }

        return placed.ToDictionary(static entry => entry.Key, static entry => entry.Value.Hit);
    }

    private static (AgentConversationSearchHit Hit, int Place) Placed(AgentConversationSearchHit hit, int place) =>
        (hit, place);
}
