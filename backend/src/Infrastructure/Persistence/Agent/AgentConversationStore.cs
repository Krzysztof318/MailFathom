// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MailFathom.Application.Agent.Conversations;
using MailFathom.Application.Discovery.Presentation;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using Npgsql;
using NpgsqlTypes;

namespace MailFathom.Infrastructure.Persistence.Agent;

/// <summary>Holds the deployment's Agent conversations in the two tables every replica of it reads and writes.</summary>
/// <remarks>
/// <para>
/// Bare commands over the data source rather than EF Core queries, for the reason the Discover run's journal is
/// written that way: each statement takes a decision PostgreSQL has to settle rather than a caller, the callers are
/// routes and a run rather than a unit of work, and none of them holds a persistence scope — a run outlives every
/// request it is reached over, so there is no request whose transaction it could enlist in. The tables are mapped
/// entities all the same, so the schema is one description and the migration is the ordinary additive one.
/// </para>
/// <para>
/// <strong>A statement decides everything it needs to, and all but one of them is a round trip.</strong> Appending advances the
/// conversation's own counter inside the insert, so the place a row is written at is the database's rather than a
/// number counted in whichever process is executing — and the row lock that advance takes is what serializes the two
/// writers a conversation genuinely has, a person typing while a run composes. Recording an answer to an offer is the
/// one that takes two, holding the conversation's row before it reads where the offer stands, which is what makes an
/// acceptance happen once. Reading takes the conversation and its tail together.
/// </para>
/// <para>
/// The identifiers in every statement come from the mapped entities' own constants, so the statements and the schema
/// are one description; every value is a parameter, so nothing a client presented and nothing a model composed is ever
/// put into text.
/// </para>
/// <para>
/// <strong>Nothing here reaches a log.</strong> A payload is somebody's question or a composed block quoting their
/// mail, so it travels between this store and the response that reads it back and nowhere else — not into a log line,
/// a span attribute, a metric, or a failure message. What a failure carries is which table could not be reached.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this store.")]
[RequiresIntegrationCoverage]
internal sealed class AgentConversationStore(NpgsqlDataSource dataSource) : IAgentConversationStore
{
    /// <summary>Starts a conversation, and says nothing happened where one already stands under that identifier.</summary>
    /// <remarks>
    /// The conflict is left to the key rather than checked for, because a repeated start is a client retrying over a
    /// dropped connection rather than a collision: the identifier was generated for this conversation, so the row that
    /// is already there is the same conversation and reporting it as started again would be the honest answer were it
    /// not indistinguishable from starting a second one.
    /// </remarks>
    private const string StartConversationStatement = $"""
        INSERT INTO "{AgentConversationEntity.TableName}"
            ("{AgentConversationEntity.IdColumnName}", "{AgentConversationEntity.UserIdColumnName}", "{AgentConversationEntity.StartedAtColumnName}", "{AgentConversationEntity.LastActivityAtColumnName}", "{AgentConversationEntity.SequenceColumnName}")
        VALUES (@id, @userId, @now, @now, 0)
        ON CONFLICT DO NOTHING;
        """;

    /// <summary>Writes one entry at the next place in its conversation, moving the answer being composed where the entry says so.</summary>
    /// <remarks>
    /// <para>
    /// The place is the conversation's own counter advanced inside this statement, so it is the database that says what
    /// a conversation has reached — and the row lock the advance takes is what stops a person's message and a run's
    /// block from both claiming one number. The key over the conversation and the place makes a second row under one
    /// number impossible rather than unlikely.
    /// </para>
    /// <para>
    /// Three conditions refuse a write. The conversation is full, which is a conversation to leave rather than a fault;
    /// an answer is opened while another is still being composed; or the entry belongs to an answer that is not the one
    /// being composed, which is exactly what a run that was stopped meets and is how the stop reaches it wherever it is
    /// executing.
    /// </para>
    /// </remarks>
    private const string AppendEntryStatement = $"""
        WITH advanced AS (
            UPDATE "{AgentConversationEntity.TableName}" c
            SET "{AgentConversationEntity.SequenceColumnName}" = c."{AgentConversationEntity.SequenceColumnName}" + 1,
                "{AgentConversationEntity.LastActivityAtColumnName}" = @now,
                "{AgentConversationEntity.ComposingMessageIdColumnName}" = CASE
                    WHEN @opensTheAnswer THEN @opensMessageId
                    WHEN @endsTheAnswer THEN NULL
                    ELSE c."{AgentConversationEntity.ComposingMessageIdColumnName}" END
            WHERE c."{AgentConversationEntity.IdColumnName}" = @id
              AND c."{AgentConversationEntity.SequenceColumnName}" < @mostEntries
              AND (NOT @opensTheAnswer OR c."{AgentConversationEntity.ComposingMessageIdColumnName}" IS NULL)
              AND (@composedInto IS NULL OR c."{AgentConversationEntity.ComposingMessageIdColumnName}" = @composedInto)
            RETURNING c."{AgentConversationEntity.IdColumnName}" AS conversation, c."{AgentConversationEntity.SequenceColumnName}" AS place
        )
        INSERT INTO "{AgentConversationEntryEntity.TableName}"
            ("{AgentConversationEntryEntity.ConversationIdColumnName}", "{AgentConversationEntryEntity.SequenceColumnName}", "{AgentConversationEntryEntity.KindColumnName}", "{AgentConversationEntryEntity.PayloadColumnName}", "{AgentConversationEntryEntity.WrittenAtColumnName}")
        SELECT a.conversation, a.place, @kind, CAST(@payload AS json), @now
        FROM advanced a
        RETURNING "{AgentConversationEntryEntity.SequenceColumnName}";
        """;

    /// <summary>Holds this person's conversation while an offer in it is being answered, and says whether it is theirs.</summary>
    /// <remarks>
    /// <para>
    /// A statement of its own, run first and in the same transaction, because a lock taken inside the recording
    /// statement would come after that statement's own snapshot was fixed. Taking the row here is what makes the second
    /// press read the first one's answer: it waits, and the statement that follows the wait takes a snapshot of its own.
    /// </para>
    /// <para>
    /// It is the conversation's row rather than an advisory key, an offer having a row it belongs to, and it doubles as
    /// the ownership check. Appending needs none of this, its whole decision being conditional on the same row it
    /// updates and therefore rechecked against the version that commits first.
    /// </para>
    /// </remarks>
    private const string HoldConversationStatement = $"""
        SELECT 1 FROM "{AgentConversationEntity.TableName}"
        WHERE "{AgentConversationEntity.IdColumnName}" = @id
          AND "{AgentConversationEntity.UserIdColumnName}" = @userId
        FOR UPDATE;
        """;

    /// <summary>Records where an offer this person was made now stands, if the move is one it can make from there.</summary>
    /// <remarks>
    /// <para>
    /// An acceptance is what permits an act with a side effect, so two presses arriving together must produce one
    /// acceptance rather than two. A pending offer may be accepted or declined, an accepted one may go on to have
    /// failed, and nothing else moves.
    /// </para>
    /// <para>
    /// Where the offer stands is read under <see cref="HoldConversationStatement" /> rather than under this statement's
    /// own lock, because this statement's snapshot is fixed before its lock could serialize anything: two presses
    /// arriving together would each read the offer as unanswered and each write an acceptance.
    /// </para>
    /// <para>
    /// The person is checked here and not in appending, because this is somebody acting on their own record while
    /// appending is the deployment writing into it. A conversation that is not theirs answers exactly as one that never
    /// existed does.
    /// </para>
    /// </remarks>
    private const string ResolveProposalStatement = $"""
        WITH standing AS (
            SELECT
                EXISTS (
                    SELECT 1 FROM "{AgentConversationEntryEntity.TableName}" p
                    WHERE p."{AgentConversationEntryEntity.ConversationIdColumnName}" = @id
                      AND p."{AgentConversationEntryEntity.SequenceColumnName}" = @proposedAt
                      AND p."{AgentConversationEntryEntity.KindColumnName}" = @proposalKind) AS offered,
                (
                    SELECT r."{AgentConversationEntryEntity.ProposalStateColumnName}"
                    FROM "{AgentConversationEntryEntity.TableName}" r
                    WHERE r."{AgentConversationEntryEntity.ConversationIdColumnName}" = @id
                      AND r."{AgentConversationEntryEntity.AnsweredProposalAtColumnName}" = @proposedAt
                    ORDER BY r."{AgentConversationEntryEntity.SequenceColumnName}" DESC
                    LIMIT 1) AS stands
        ),
        advanced AS (
            UPDATE "{AgentConversationEntity.TableName}" c
            SET "{AgentConversationEntity.SequenceColumnName}" = c."{AgentConversationEntity.SequenceColumnName}" + 1,
                "{AgentConversationEntity.LastActivityAtColumnName}" = @now
            FROM standing s
            WHERE c."{AgentConversationEntity.IdColumnName}" = @id
              AND c."{AgentConversationEntity.UserIdColumnName}" = @userId
              AND c."{AgentConversationEntity.SequenceColumnName}" < @mostEntries
              AND s.offered
              AND ((s.stands IS NULL AND @state IN (@accepted, @declined))
                   OR (s.stands = @accepted AND @state = @failed))
            RETURNING c."{AgentConversationEntity.IdColumnName}" AS conversation, c."{AgentConversationEntity.SequenceColumnName}" AS place
        )
        INSERT INTO "{AgentConversationEntryEntity.TableName}"
            ("{AgentConversationEntryEntity.ConversationIdColumnName}", "{AgentConversationEntryEntity.SequenceColumnName}", "{AgentConversationEntryEntity.KindColumnName}", "{AgentConversationEntryEntity.PayloadColumnName}", "{AgentConversationEntryEntity.AnsweredProposalAtColumnName}", "{AgentConversationEntryEntity.ProposalStateColumnName}", "{AgentConversationEntryEntity.WrittenAtColumnName}")
        SELECT a.conversation, a.place, @kind, CAST(@payload AS json), @proposedAt, @state, @now
        FROM advanced a
        RETURNING "{AgentConversationEntryEntity.SequenceColumnName}";
        """;

    /// <summary>Reads a conversation this person holds, with everything written past a stated point.</summary>
    /// <remarks>
    /// <para>
    /// One statement rather than two, because a reader wants the conversation's own standing beside its tail: what it
    /// is called, when it began, and whether an answer is still being composed are what a screen is drawn from, and
    /// asking for them separately would be two round trips to draw one thing. A conversation that is somebody else's
    /// or never existed returns no row at all — one answer for every way it is not this caller's to see — while one
    /// that has written nothing past the cursor returns one row carrying no entry, which is how the two are told apart.
    /// </para>
    /// <para>
    /// A cursor past what the conversation has reached reads as the beginning, which is the safe direction the contract
    /// states: nobody can hold such a value honestly, so it belongs to some other conversation, and replaying costs a
    /// few entries where honouring it would hand back a conversation missing everything before the number.
    /// </para>
    /// <para>
    /// The place is selected beside the payload because it is not in the payload: an entry is serialized before the
    /// statement derives its place, so the row is the only place a conversation's order is recorded and the caller
    /// stamps it back onto what it read.
    /// </para>
    /// </remarks>
    private const string ReadConversationStatement = $"""
        SELECT c."{AgentConversationEntity.TitleColumnName}",
               c."{AgentConversationEntity.StartedAtColumnName}",
               c."{AgentConversationEntity.ComposingMessageIdColumnName}" IS NOT NULL,
               e."{AgentConversationEntryEntity.PayloadColumnName}",
               e."{AgentConversationEntryEntity.SequenceColumnName}"
        FROM "{AgentConversationEntity.TableName}" c
        LEFT JOIN LATERAL (
            SELECT w."{AgentConversationEntryEntity.PayloadColumnName}", w."{AgentConversationEntryEntity.SequenceColumnName}"
            FROM "{AgentConversationEntryEntity.TableName}" w
            WHERE w."{AgentConversationEntryEntity.ConversationIdColumnName}" = c."{AgentConversationEntity.IdColumnName}"
              AND w."{AgentConversationEntryEntity.SequenceColumnName}" > CASE
                  WHEN @afterSequence > c."{AgentConversationEntity.SequenceColumnName}" THEN 0
                  ELSE @afterSequence END
            ORDER BY w."{AgentConversationEntryEntity.SequenceColumnName}"
            LIMIT @readLimit) e ON TRUE
        WHERE c."{AgentConversationEntity.IdColumnName}" = @id AND c."{AgentConversationEntity.UserIdColumnName}" = @userId
        ORDER BY e."{AgentConversationEntryEntity.SequenceColumnName}";
        """;

    /// <summary>Reads one person's conversation history, the one that moved most recently first.</summary>
    /// <remarks>The identifier breaks a tie, so two conversations written in the same instant come back in one order rather than whichever the plan happened to produce.</remarks>
    private const string ListConversationsStatement = $"""
        SELECT "{AgentConversationEntity.IdColumnName}",
               "{AgentConversationEntity.TitleColumnName}",
               "{AgentConversationEntity.StartedAtColumnName}",
               "{AgentConversationEntity.LastActivityAtColumnName}"
        FROM "{AgentConversationEntity.TableName}"
        WHERE "{AgentConversationEntity.UserIdColumnName}" = @userId
        ORDER BY "{AgentConversationEntity.LastActivityAtColumnName}" DESC, "{AgentConversationEntity.IdColumnName}"
        LIMIT @listLimit;
        """;

    /// <summary>Names a conversation, leaving the instant its history is ordered by where it was.</summary>
    private const string SetTitleStatement = $"""
        UPDATE "{AgentConversationEntity.TableName}"
        SET "{AgentConversationEntity.TitleColumnName}" = @title
        WHERE "{AgentConversationEntity.IdColumnName}" = @id;
        """;

    /// <summary>Removes a conversation this person holds, and everything it held with it through the cascade.</summary>
    private const string DeleteConversationStatement = $"""
        DELETE FROM "{AgentConversationEntity.TableName}"
        WHERE "{AgentConversationEntity.IdColumnName}" = @id AND "{AgentConversationEntity.UserIdColumnName}" = @userId;
        """;

    /// <inheritdoc />
    public async Task<bool> TryStartAsync(
        AgentConversationId id,
        MailUserId user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(StartConversationStatement);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("userId", user.Value);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <inheritdoc />
    public async Task<long?> AppendAsync(
        AgentConversationId id,
        AgentConversationEntry entry,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry is AgentProposalResolved)
        {
            throw new ArgumentException(
                "An answer to a proposal is recorded against where that proposal stands, which is what resolving one does.",
                nameof(entry));
        }

        await using var command = dataSource.CreateCommand(AppendEntryStatement);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("kind", entry.EntryName);
        command.Parameters.Add(Payload(entry));
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("opensTheAnswer", entry.OpensTheAnswer);
        command.Parameters.AddWithValue("endsTheAnswer", entry.EndsTheAnswer);
        command.Parameters.Add(Identity("opensMessageId", entry is AgentAnswerStarted started ? started.MessageId : null));
        command.Parameters.Add(Identity("composedInto", entry.ComposedInto));
        command.Parameters.AddWithValue("mostEntries", (long)AgentConversationBounds.MaximumEntries);

        return await command.ExecuteScalarAsync(cancellationToken) as long?;
    }

    /// <inheritdoc />
    public async Task<long?> TryResolveProposalAsync(
        AgentConversationId id,
        MailUserId user,
        long proposedAt,
        AgentProposalState state,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var resolution = new AgentProposalResolved(proposedAt, state) { ConversationId = id };

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var answering = await connection.BeginTransactionAsync(cancellationToken);

        await using (var hold = new NpgsqlCommand(HoldConversationStatement, connection, answering))
        {
            hold.Parameters.AddWithValue("id", id.Value);
            hold.Parameters.AddWithValue("userId", user.Value);

            if (await hold.ExecuteScalarAsync(cancellationToken) is null)
            {
                return null;
            }
        }

        await using var command = new NpgsqlCommand(ResolveProposalStatement, connection, answering);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("userId", user.Value);
        command.Parameters.AddWithValue("proposedAt", proposedAt);
        command.Parameters.AddWithValue("state", state.ToString());
        command.Parameters.AddWithValue("kind", resolution.EntryName);
        command.Parameters.Add(Payload(resolution));
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("proposalKind", AgentActionProposed.Kind);
        command.Parameters.AddWithValue("accepted", AgentProposalState.Accepted.ToString());
        command.Parameters.AddWithValue("declined", AgentProposalState.Declined.ToString());
        command.Parameters.AddWithValue("failed", AgentProposalState.Failed.ToString());
        command.Parameters.AddWithValue("mostEntries", (long)AgentConversationBounds.MaximumEntries);

        var place = await command.ExecuteScalarAsync(cancellationToken) as long?;

        await answering.CommitAsync(cancellationToken);

        return place;
    }

    /// <inheritdoc />
    public async Task<AgentConversationReading?> ReadAsync(
        AgentConversationId id,
        MailUserId user,
        long afterSequence,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, AgentConversationBounds.MaximumEntriesPerRead);

        await using var command = dataSource.CreateCommand(ReadConversationStatement);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("userId", user.Value);
        command.Parameters.AddWithValue("afterSequence", afterSequence);

        // One more than the page holds, so that whether a following one exists is observed rather than counted.
        command.Parameters.AddWithValue("readLimit", limit + 1);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var found = false;
        string? title = null;
        var startedAt = default(DateTimeOffset);
        var composing = false;
        List<AgentConversationEntry> read = [];

        while (await reader.ReadAsync(cancellationToken))
        {
            found = true;
            title = await reader.IsDBNullAsync(0, cancellationToken) ? null : reader.GetString(0);
            startedAt = reader.GetFieldValue<DateTimeOffset>(1);
            composing = reader.GetBoolean(2);

            if (!await reader.IsDBNullAsync(3, cancellationToken))
            {
                // Stamped from the row rather than read out of the payload: the place is derived inside the statement
                // that writes the row, so the entry was serialized before there was one to serialize and the payload's
                // own copy of both members is a default by construction.
                read.Add(Read(reader.GetString(3)) with { ConversationId = id, Sequence = reader.GetInt64(4) });
            }
        }

        if (!found)
        {
            return null;
        }

        var (page, moreFollows) = KeysetPageSplit.Of([.. read], limit);

        return new AgentConversationReading(title, startedAt, composing, page, moreFollows);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentConversationSummary>> ListAsync(
        MailUserId user,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, AgentConversationBounds.MaximumConversationsPerListing);

        await using var command = dataSource.CreateCommand(ListConversationsStatement);
        command.Parameters.AddWithValue("userId", user.Value);
        command.Parameters.AddWithValue("listLimit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        List<AgentConversationSummary> history = [];

        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(new AgentConversationSummary(
                AgentConversationId.Create(reader.GetGuid(0)),
                await reader.IsDBNullAsync(1, cancellationToken) ? null : reader.GetString(1),
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetFieldValue<DateTimeOffset>(3)));
        }

        return history;
    }

    /// <inheritdoc />
    public async Task<bool> TrySetTitleAsync(
        AgentConversationId id,
        PresentationText title,
        CancellationToken cancellationToken)
    {
        if (!title.IsSpecified)
        {
            throw new ArgumentException("A conversation is named with text rather than with the struct default.", nameof(title));
        }

        if (title.Value.Length > AgentConversationBounds.MaximumTitleLength)
        {
            throw new ArgumentException(
                $"A conversation's name holds at most {AgentConversationBounds.MaximumTitleLength} characters.",
                nameof(title));
        }

        await using var command = dataSource.CreateCommand(SetTitleStatement);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("title", title.Value);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <inheritdoc />
    public async Task<bool> TryDeleteAsync(
        AgentConversationId id,
        MailUserId user,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(DeleteConversationStatement);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("userId", user.Value);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Writes one entry into the document the payload column holds.</summary>
    private static NpgsqlParameter Payload(AgentConversationEntry entry) =>
        new("payload", NpgsqlDbType.Text)
        {
            Value = JsonSerializer.Serialize(entry, AgentConversationEntryJsonContext.Default.AgentConversationEntry),
        };

    /// <summary>Carries a message identity a statement compares against, or the absence of one.</summary>
    private static NpgsqlParameter Identity(string name, AgentMessageId? message) =>
        new(name, NpgsqlDbType.Uuid)
        {
            Value = message is { } named ? named.Value : DBNull.Value,
        };

    /// <summary>Reads one stored entry back into the contract it was written from.</summary>
    /// <exception cref="InvalidOperationException">Thrown when a row holds a document this build cannot read as an entry, which is a schema this build did not write.</exception>
    private static AgentConversationEntry Read(string payload) =>
        JsonSerializer.Deserialize(payload, AgentConversationEntryJsonContext.Default.AgentConversationEntry)
        ?? throw new InvalidOperationException(
            $"A row in {AgentConversationEntryEntity.TableName} does not hold an Agent conversation entry. "
            + "Neither the conversation nor what was said in it is in this message.");
}
