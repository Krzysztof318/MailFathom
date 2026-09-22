// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Agent.Search;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Search;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Connections;
using MailFathom.Infrastructure.Persistence.Entities;
using Npgsql;
using Pgvector;

namespace MailFathom.Infrastructure.Persistence.Agent;

/// <summary>Ranks a person's Agent history by its words and by its vectors, and records the vectors.</summary>
/// <remarks>
/// <para>
/// Bare commands over the data source, for the reason <see cref="AgentConversationStore" /> is written that way: the
/// callers are a route and a run rather than a unit of work, and a run outlives every request it was reached over.
/// Every identifier comes from the mapped entities' constants and every value is a parameter, so nothing a person typed
/// is ever put into text; the one piece of text chosen here is the distance operator, which comes from a closed set.
/// </para>
/// <para>
/// <strong>Both rankings are exact and read one person's rows alone.</strong> A conversation is ranked at its single
/// best entry — <c>DISTINCT ON</c> the conversation, ordered by that entry's rank or distance — so one that repeats a
/// phrase is still one result, and the entry chosen is the one a screen opens the conversation at. A tie between two
/// entries of one conversation goes to the later, which is the one a person most recently read.
/// </para>
/// <para>
/// <strong>Nothing here reaches a log.</strong> The query and every vector are somebody's own words or derived from
/// them, and a failure carries which table could not be reached and nothing that was read or written.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this index.")]
[RequiresIntegrationCoverage]
internal sealed class AgentConversationSearchIndex(
    NpgsqlDataSource dataSource,
    PostgresTextSearchConfiguration textSearchConfiguration,
    TimeProvider timeProvider) : IAgentConversationSearchIndex
{
    /// <summary>What both rankings project: the conversation, its name and recency, and the entry that placed it.</summary>
    private const string RankedColumns = $"""
        c."{AgentConversationEntity.IdColumnName}" AS conversation,
        c."{AgentConversationEntity.TitleColumnName}" AS title,
        c."{AgentConversationEntity.LastActivityAtColumnName}" AS last_activity,
        e."{AgentConversationEntryEntity.SequenceColumnName}" AS place,
        CAST(e."{AgentConversationEntryEntity.PayloadColumnName}" ->> 'messageId' AS uuid) AS message
        """;

    /// <summary>Ranks by <c>ts_rank</c> over the generated column, the more recent conversation first among equals.</summary>
    private const string LexicalRankingStatement = $"""
        SELECT ranked.conversation, ranked.title, ranked.last_activity, ranked.place, ranked.message
        FROM (
            SELECT DISTINCT ON (c."{AgentConversationEntity.IdColumnName}")
                   {RankedColumns},
                   ts_rank(e."{AgentConversationEntryEntity.SearchVectorColumnName}", q.query) AS rank
            FROM "{AgentConversationEntity.TableName}" c
            JOIN "{AgentConversationEntryEntity.TableName}" e
              ON e."{AgentConversationEntryEntity.ConversationIdColumnName}" = c."{AgentConversationEntity.IdColumnName}"
            CROSS JOIN websearch_to_tsquery(CAST(@configuration AS regconfig), @query) AS q(query)
            WHERE c."{AgentConversationEntity.UserIdColumnName}" = @userId
              AND e."{AgentConversationEntryEntity.SearchVectorColumnName}" @@ q.query
            ORDER BY c."{AgentConversationEntity.IdColumnName}", rank DESC, e."{AgentConversationEntryEntity.SequenceColumnName}" DESC) ranked
        ORDER BY ranked.rank DESC, ranked.last_activity DESC, ranked.conversation
        LIMIT @limit;
        """;

    /// <summary>Records a vector against an entry of this person's conversation, and nothing where one is already recorded.</summary>
    private const string SaveEmbeddingStatement = $"""
        INSERT INTO "{AgentConversationEmbeddingEntity.TableName}" (
            "{AgentConversationEmbeddingEntity.ConversationIdColumnName}",
            "{AgentConversationEmbeddingEntity.SequenceColumnName}",
            "{AgentConversationEmbeddingEntity.EmbeddingProfileIdColumnName}",
            "{AgentConversationEmbeddingEntity.DimensionColumnName}",
            "{AgentConversationEmbeddingEntity.EmbeddingColumnName}",
            "{AgentConversationEmbeddingEntity.GeneratedAtColumnName}")
        SELECT e."{AgentConversationEntryEntity.ConversationIdColumnName}",
               e."{AgentConversationEntryEntity.SequenceColumnName}",
               @profileId, @dimension, @embedding, @now
        FROM "{AgentConversationEntryEntity.TableName}" e
        JOIN "{AgentConversationEntity.TableName}" c
          ON c."{AgentConversationEntity.IdColumnName}" = e."{AgentConversationEntryEntity.ConversationIdColumnName}"
        WHERE e."{AgentConversationEntryEntity.ConversationIdColumnName}" = @conversationId
          AND e."{AgentConversationEntryEntity.SequenceColumnName}" = @sequence
          AND c."{AgentConversationEntity.UserIdColumnName}" = @userId
        ON CONFLICT ON CONSTRAINT "{PersistenceConstraintNames.AgentConversationEmbeddingPrimaryKeyConstraintName}" DO NOTHING;
        """;

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentConversationSearchHit>> ReadLexicalRankingAsync(
        UserId user,
        EmailSearchQueryText queryText,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var command = dataSource.CreateCommand(LexicalRankingStatement);
        command.Parameters.AddWithValue("configuration", textSearchConfiguration.Value);
        command.Parameters.AddWithValue("query", queryText.Value);
        command.Parameters.AddWithValue("userId", user.Value);
        command.Parameters.AddWithValue("limit", limit);

        return await ReadHitsAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentConversationSearchHit>> ReadNearestRankingAsync(
        UserId user,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        await using var command = dataSource.CreateCommand(NearestRankingStatement(profile.Identity.DistanceMetric));
        command.Parameters.AddWithValue("target", new Vector(queryVector.Components));
        command.Parameters.AddWithValue("profileId", profile.Id.Value);
        command.Parameters.AddWithValue("userId", user.Value);
        command.Parameters.AddWithValue("limit", limit);

        return await ReadHitsAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SaveEmbeddingAsync(
        AgentConversationId conversation,
        UserId user,
        long sequence,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector vector,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(vector);

        await using var command = dataSource.CreateCommand(SaveEmbeddingStatement);
        command.Parameters.AddWithValue("profileId", profile.Id.Value);
        command.Parameters.AddWithValue("dimension", profile.Identity.Dimension);
        command.Parameters.AddWithValue("embedding", new Vector(vector.Components));
        command.Parameters.AddWithValue("now", timeProvider.GetUtcNow());
        command.Parameters.AddWithValue("conversationId", conversation.Value);
        command.Parameters.AddWithValue("sequence", sequence);
        command.Parameters.AddWithValue("userId", user.Value);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException refusal) when (refusal.SqlState == PostgresErrorCodes.ForeignKeyViolation)
        {
            // The conversation was deleted between the read that found the entry and this write, which leaves a vector
            // with nothing to hang off rather than a fault: the person removed the words it was placed from.
        }
    }

    /// <summary>Ranks by the space's own distance, nearest first and the more recent conversation first among equals.</summary>
    /// <remarks>
    /// The operator follows the space's metric for the reason the mail ranking's does: a distance under any other
    /// metric than the one the vectors were written for is a number with no meaning. Inner product is taken negated,
    /// which is how pgvector writes it, so every metric ranks ascending.
    /// </remarks>
    private static string NearestRankingStatement(EmbeddingDistanceMetric metric)
    {
        var distanceOperator = metric switch
        {
            EmbeddingDistanceMetric.Cosine => "<=>",
            EmbeddingDistanceMetric.InnerProduct => "<#>",
            EmbeddingDistanceMetric.EuclideanDistance => "<->",
            _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, "A space declares a known metric."),
        };

        return $"""
            SELECT ranked.conversation, ranked.title, ranked.last_activity, ranked.place, ranked.message
            FROM (
                SELECT DISTINCT ON (c."{AgentConversationEntity.IdColumnName}")
                       {RankedColumns},
                       v."{AgentConversationEmbeddingEntity.EmbeddingColumnName}" {distanceOperator} @target AS distance
                FROM "{AgentConversationEntity.TableName}" c
                JOIN "{AgentConversationEmbeddingEntity.TableName}" v
                  ON v."{AgentConversationEmbeddingEntity.ConversationIdColumnName}" = c."{AgentConversationEntity.IdColumnName}"
                JOIN "{AgentConversationEntryEntity.TableName}" e
                  ON e."{AgentConversationEntryEntity.ConversationIdColumnName}" = v."{AgentConversationEmbeddingEntity.ConversationIdColumnName}"
                 AND e."{AgentConversationEntryEntity.SequenceColumnName}" = v."{AgentConversationEmbeddingEntity.SequenceColumnName}"
                WHERE c."{AgentConversationEntity.UserIdColumnName}" = @userId
                  AND v."{AgentConversationEmbeddingEntity.EmbeddingProfileIdColumnName}" = @profileId
                ORDER BY c."{AgentConversationEntity.IdColumnName}", distance, e."{AgentConversationEntryEntity.SequenceColumnName}" DESC) ranked
            ORDER BY ranked.distance, ranked.last_activity DESC, ranked.conversation
            LIMIT @limit;
            """;
    }

    private static async Task<IReadOnlyList<AgentConversationSearchHit>> ReadHitsAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        List<AgentConversationSearchHit> hits = [];

        while (await reader.ReadAsync(cancellationToken))
        {
            hits.Add(new AgentConversationSearchHit(
                AgentConversationId.Create(reader.GetGuid(0)),
                await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                AgentMessageId.Create(reader.GetGuid(4)),
                reader.GetInt64(3)));
        }

        return hits;
    }
}
