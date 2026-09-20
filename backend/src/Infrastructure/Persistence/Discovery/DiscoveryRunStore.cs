// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using MailFathom.Application.Discovery.Streaming;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Persistence.Entities;
using Npgsql;
using NpgsqlTypes;

namespace MailFathom.Infrastructure.Persistence.Discovery;

/// <summary>Holds the deployment's Discover runs in the two tables every replica of it reads and writes.</summary>
/// <remarks>
/// <para>
/// Bare commands over the data source rather than EF Core queries, for the reason the signal tickets are written that
/// way: each statement takes a decision PostgreSQL has to settle rather than a caller, the callers are routes and a
/// run rather than a unit of work, and none of them holds a persistence scope — a run outlives every request it is
/// reached over, so there is no request whose transaction it could enlist in.
/// </para>
/// <para>
/// <strong>Each statement is one round trip and decides everything it needs to.</strong> Opening sweeps, counts one
/// person's runs, and inserts together, so the bound is the deployment's rather than a number each replica finds room
/// under separately. Appending derives the sequence from what the run already holds inside the insert, so a run's
/// order is the database's rather than a counter in whichever process happens to be executing it. Reading stamps the
/// run as used in the same statement that reads it. What would otherwise be a read followed by a write is one
/// statement everywhere, which is what stops two replicas from acting on a state neither of them still holds.
/// </para>
/// <para>
/// The identifiers in every statement come from the mapped entities' own constants, so the statements and the schema
/// are one description; every value is a parameter, so nothing a client presented and nothing a model composed is ever
/// put into text.
/// </para>
/// <para>
/// <strong>Nothing here reaches a log.</strong> A payload is a composed block or a declared citation and quotes
/// somebody's mail, so it travels between this store and the response that reads it back and nowhere else — not into a
/// log line, a span attribute, a metric, or a failure message. What a failure carries is which table could not be
/// reached.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this store.")]
[RequiresIntegrationCoverage]
internal sealed class DiscoveryRunStore(NpgsqlDataSource dataSource) : IDiscoveryRunStore
{
    /// <summary>Removes what nobody can come back for, then opens the run while this person is under the deployment's bound.</summary>
    /// <remarks>
    /// The removal is housekeeping rather than part of the decision: every figure the count reads filters on the same
    /// two windows anyway, so a row the sweep is deleting in this statement is one the count has already left out.
    /// Counting and inserting are one statement because two requests that each read a count of seven and then inserted
    /// would leave the person running nine, and the bound is a number a client is told rather than approximately it.
    /// </remarks>
    private const string OpenRunStatement = $"""
        WITH forgotten AS (
            DELETE FROM "{DiscoveryRunEntity.TableName}"
            WHERE ("{DiscoveryRunEntity.EndedAtColumnName}" IS NOT NULL
                    AND "{DiscoveryRunEntity.LastUsedAtColumnName}" <= @forgettableFrom)
               OR "{DiscoveryRunEntity.StartedAtColumnName}" <= @unreportedFrom
        )
        INSERT INTO "{DiscoveryRunEntity.TableName}"
            ("{DiscoveryRunEntity.IdColumnName}", "{DiscoveryRunEntity.UserIdColumnName}", "{DiscoveryRunEntity.StartedAtColumnName}", "{DiscoveryRunEntity.LastUsedAtColumnName}")
        SELECT @id, @userId, @now, @now
        WHERE (
            SELECT count(*) FROM "{DiscoveryRunEntity.TableName}"
            WHERE "{DiscoveryRunEntity.UserIdColumnName}" = @userId
              AND "{DiscoveryRunEntity.EndedAtColumnName}" IS NULL
              AND "{DiscoveryRunEntity.StartedAtColumnName}" > @unreportedFrom) < @mostConcurrent;
        """;

    /// <summary>Writes one event at the next place in its run, and ends the run where the event is an ending.</summary>
    /// <remarks>
    /// <para>
    /// The sequence is <c>max + 1</c> read inside the insert rather than a number the caller carried, so it is the
    /// database that says what a run has reached — and the key over the run and the sequence is what makes a second row
    /// under one number impossible rather than unlikely.
    /// </para>
    /// <para>
    /// Four conditions refuse a write and all four mean the run is over: it has been forgotten, it has already written
    /// an ending, it has been asked to stop, or it has no room left for anything but an ending. The last two are lifted
    /// for an ending itself, because a run that could not write one would leave a client reading it until the ceiling
    /// forgot it.
    /// </para>
    /// </remarks>
    private const string AppendEventStatement = $"""
        WITH admitted AS (
            SELECT r."{DiscoveryRunEntity.IdColumnName}" AS run,
                   COALESCE((
                       SELECT max(e."{DiscoveryRunEventEntity.SequenceColumnName}")
                       FROM "{DiscoveryRunEventEntity.TableName}" e
                       WHERE e."{DiscoveryRunEventEntity.RunIdColumnName}" = r."{DiscoveryRunEntity.IdColumnName}"), 0) AS reached
            FROM "{DiscoveryRunEntity.TableName}" r
            WHERE r."{DiscoveryRunEntity.IdColumnName}" = @id
              AND r."{DiscoveryRunEntity.EndedAtColumnName}" IS NULL
              AND (@endsTheRun OR r."{DiscoveryRunEntity.StopRequestedAtColumnName}" IS NULL)
        ),
        written AS (
            INSERT INTO "{DiscoveryRunEventEntity.TableName}"
                ("{DiscoveryRunEventEntity.RunIdColumnName}", "{DiscoveryRunEventEntity.SequenceColumnName}", "{DiscoveryRunEventEntity.KindColumnName}", "{DiscoveryRunEventEntity.PayloadColumnName}", "{DiscoveryRunEventEntity.WrittenAtColumnName}")
            SELECT a.run, a.reached + 1, @kind, CAST(@payload AS jsonb), @now
            FROM admitted a
            WHERE @endsTheRun OR a.reached < @mostEvents - 1
            RETURNING "{DiscoveryRunEventEntity.RunIdColumnName}" AS run, "{DiscoveryRunEventEntity.SequenceColumnName}" AS sequence
        )
        UPDATE "{DiscoveryRunEntity.TableName}" r
        SET "{DiscoveryRunEntity.LastUsedAtColumnName}" = @now,
            "{DiscoveryRunEntity.EndedAtColumnName}" = CASE WHEN @endsTheRun THEN @now ELSE r."{DiscoveryRunEntity.EndedAtColumnName}" END
        FROM written w
        WHERE r."{DiscoveryRunEntity.IdColumnName}" = w.run
        RETURNING w.sequence;
        """;

    /// <summary>Reads a run this user started from a stated point, stamping it as used in the same statement.</summary>
    /// <remarks>
    /// <para>
    /// The update is what resolves the run, so a run that is somebody else's or has been forgotten returns no row at
    /// all — one answer for every way a run is not this caller's to see. A run that exists and has written nothing
    /// after the cursor returns one row with no event in it, which is how the two are told apart.
    /// </para>
    /// <para>
    /// A cursor past what the run has reached reads as the beginning, which is the safe direction the contract states:
    /// nobody can hold such a value honestly, so it belongs to some other run, and replaying costs a few events where
    /// honouring it would hand back a run missing everything before the number.
    /// </para>
    /// </remarks>
    private const string ReadRunStatement = $"""
        WITH used AS (
            UPDATE "{DiscoveryRunEntity.TableName}"
            SET "{DiscoveryRunEntity.LastUsedAtColumnName}" = @now
            WHERE "{DiscoveryRunEntity.IdColumnName}" = @id AND "{DiscoveryRunEntity.UserIdColumnName}" = @userId
            RETURNING "{DiscoveryRunEntity.IdColumnName}" AS run, "{DiscoveryRunEntity.EndedAtColumnName}" AS ended
        ),
        reach AS (
            SELECT COALESCE(max("{DiscoveryRunEventEntity.SequenceColumnName}"), 0) AS reached
            FROM "{DiscoveryRunEventEntity.TableName}"
            WHERE "{DiscoveryRunEventEntity.RunIdColumnName}" = @id
        )
        SELECT u.ended IS NULL, e."{DiscoveryRunEventEntity.PayloadColumnName}"
        FROM used u
        CROSS JOIN reach c
        LEFT JOIN "{DiscoveryRunEventEntity.TableName}" e
            ON e."{DiscoveryRunEventEntity.RunIdColumnName}" = u.run
           AND e."{DiscoveryRunEventEntity.SequenceColumnName}" > CASE WHEN @afterSequence > c.reached THEN 0 ELSE @afterSequence END
        ORDER BY e."{DiscoveryRunEventEntity.SequenceColumnName}";
        """;

    /// <summary>Records that a run this user started has been asked to stop, where there is still something to stop.</summary>
    /// <remarks>
    /// The update reaches the row whether or not the run is still running, so a run that finished a moment earlier is
    /// reported as found — which is the contract the stopping route keeps — while the column it would have set is left
    /// alone, a stop after an ending being a fact about nothing. Stamping the run as used keeps a run somebody is still
    /// stopping from being forgotten between the two statements.
    /// </remarks>
    private const string RequestStopStatement = $"""
        UPDATE "{DiscoveryRunEntity.TableName}"
        SET "{DiscoveryRunEntity.LastUsedAtColumnName}" = @now,
            "{DiscoveryRunEntity.StopRequestedAtColumnName}" = CASE
                WHEN "{DiscoveryRunEntity.EndedAtColumnName}" IS NULL
                    THEN COALESCE("{DiscoveryRunEntity.StopRequestedAtColumnName}", @now)
                ELSE "{DiscoveryRunEntity.StopRequestedAtColumnName}" END
        WHERE "{DiscoveryRunEntity.IdColumnName}" = @id AND "{DiscoveryRunEntity.UserIdColumnName}" = @userId;
        """;

    /// <summary>Removes the runs nobody can come back for, in one statement over the whole table.</summary>
    /// <remarks>
    /// A scan by design, for the reason <c>DiscoveryRunConfiguration</c> gives: the table holds the runs executing now
    /// plus whatever ended inside a five-minute window, and an index on the instant this reads would be rewritten on
    /// every read of every run to save a scan made twelve times an hour. What each run wrote goes with it through the
    /// events' cascade, which is what makes the storage limitation on a run's answer one statement against the runs.
    /// </remarks>
    private const string RemoveForgottenStatement = $"""
        DELETE FROM "{DiscoveryRunEntity.TableName}"
        WHERE ("{DiscoveryRunEntity.EndedAtColumnName}" IS NOT NULL
                AND "{DiscoveryRunEntity.LastUsedAtColumnName}" <= @forgettableFrom)
           OR "{DiscoveryRunEntity.StartedAtColumnName}" <= @unreportedFrom;
        """;

    /// <inheritdoc />
    public async Task<bool> TryOpenAsync(
        DiscoveryRunId id,
        MailUserId user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(OpenRunStatement);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("userId", user.Value);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("forgettableFrom", ForgettableFrom(now));
        command.Parameters.AddWithValue("unreportedFrom", UnreportedFrom(now));
        command.Parameters.AddWithValue("mostConcurrent", DiscoveryRunBounds.MaximumConcurrentRunsPerUser);

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <inheritdoc />
    public async Task<long?> AppendAsync(
        DiscoveryRunId id,
        DiscoveryRunEvent written,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(written);

        await using var command = dataSource.CreateCommand(AppendEventStatement);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("kind", written.EventName);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Text)
        {
            Value = JsonSerializer.Serialize(written, DiscoveryRunEventJsonContext.Default.DiscoveryRunEvent),
        });
        command.Parameters.AddWithValue("now", now.ToUniversalTime());
        command.Parameters.AddWithValue("endsTheRun", written.EndsTheRun);
        command.Parameters.AddWithValue("mostEvents", DiscoveryRunBounds.MaximumEvents);

        return await command.ExecuteScalarAsync(cancellationToken) as long?;
    }

    /// <inheritdoc />
    public async Task<DiscoveryRunReading?> ReadAsync(
        DiscoveryRunId id,
        MailUserId user,
        long afterSequence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        await using var command = dataSource.CreateCommand(ReadRunStatement);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("userId", user.Value);
        command.Parameters.AddWithValue("afterSequence", afterSequence);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        var found = false;
        var running = false;
        List<DiscoveryRunEvent> events = [];

        while (await reader.ReadAsync(cancellationToken))
        {
            found = true;
            running = reader.GetBoolean(0);

            if (!await reader.IsDBNullAsync(1, cancellationToken))
            {
                events.Add(Read(reader.GetString(1)));
            }
        }

        // No row at all is the one answer for every way a run is not this caller's to see: it never existed, it has
        // been forgotten, or it belongs to somebody else. A run that exists and has written nothing past the cursor
        // returns one row carrying no event, which is why the two cannot be told apart by the events alone.
        return found ? new DiscoveryRunReading(running, events) : null;
    }

    /// <inheritdoc />
    public async Task<bool> TryRequestStopAsync(
        DiscoveryRunId id,
        MailUserId user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(RequestStopStatement);
        command.Parameters.AddWithValue("id", id.Value);
        command.Parameters.AddWithValue("userId", user.Value);
        command.Parameters.AddWithValue("now", now.ToUniversalTime());

        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <inheritdoc />
    public async Task<int> RemoveForgottenAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(RemoveForgottenStatement);
        command.Parameters.AddWithValue("forgettableFrom", ForgettableFrom(now));
        command.Parameters.AddWithValue("unreportedFrom", UnreportedFrom(now));

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Reads one stored event back into the contract it was written from.</summary>
    /// <exception cref="InvalidOperationException">Thrown when a row holds a document this build cannot read as an event, which is a schema this build did not write.</exception>
    private static DiscoveryRunEvent Read(string payload) =>
        JsonSerializer.Deserialize(payload, DiscoveryRunEventJsonContext.Default.DiscoveryRunEvent)
        ?? throw new InvalidOperationException(
            $"A row in {DiscoveryRunEventEntity.TableName} does not hold a Discover run event. "
            + "Neither the run nor what it read is in this message.");

    /// <summary>The instant an ended run's retention window has to have started before for it to be forgotten.</summary>
    private static DateTimeOffset ForgettableFrom(DateTimeOffset now) =>
        now.ToUniversalTime() - DiscoveryRunBounds.RetentionAfterLastUse;

    /// <summary>The instant a run has to have started before for it to be one whose execution never reported at all.</summary>
    private static DateTimeOffset UnreportedFrom(DateTimeOffset now) =>
        now.ToUniversalTime() - (DiscoveryRunBounds.MaximumDuration + DiscoveryRunBounds.RetentionAfterLastUse);
}
