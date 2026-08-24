// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using MailFathom.Application.Persistence;
using MailFathom.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Sessions;

/// <summary>Provides the EF Core and transaction operations owned by one persistence session.</summary>
/// <remarks>
/// EF Core publishes no interface over this seam. <see cref="Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction" />
/// covers committing and rolling back alone, while a session also has to open its transaction on demand, save tracked
/// changes, ask the provider whether an update failure is an optimistic conflict or a failure that can clear on its
/// own, and clear tracked state after cleanup — and the type that offers the rest, <see cref="DbContext" />, is a
/// concrete class no fake provider may stand in for. Bundling those operations here is what lets the commit,
/// rollback, and disposal ordering of <see cref="EfCorePersistenceSession" /> be asserted without a database.
/// </remarks>
internal interface IEfCorePersistenceSessionResources : IAsyncDisposable
{
    /// <summary>Opens this session's transaction if it is not open yet, and answers with the context enlisted in it.</summary>
    /// <param name="cancellationToken">Cancels opening the transaction.</param>
    /// <returns>The context used by repositories participating in the session.</returns>
    Task<MailFathomDbContext> JoinAsync(CancellationToken cancellationToken);

    /// <summary>Persists tracked changes through EF Core.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Commits the current database transaction, if one was opened.</summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken);

    /// <summary>Rolls back the current database transaction, if one was opened.</summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken);

    /// <summary>Determines whether a provider update failure represents a recognized optimistic conflict.</summary>
    bool IsConcurrencyConflict(DbUpdateException exception);

    /// <summary>Determines whether a provider failure is one the database may not produce again.</summary>
    bool IsTransientFailure(Exception exception);

    /// <summary>Releases all entities tracked by the scoped context after transaction cleanup.</summary>
    void ClearTrackedState();
}

/// <summary>Owns one short EF Core write transaction and translates concurrency failures.</summary>
/// <remarks>
/// Every ending is counted here rather than by the retry policy above it, because this is where a conflict is actually
/// observed: the policy sees only the conflicts that survived every attempt it was allowed, and the ones it resolved
/// are exactly the ones a rate exists to make visible.
/// </remarks>
internal sealed class EfCorePersistenceSession(
    IEfCorePersistenceSessionResources resources,
    PersistenceCommitTelemetry telemetry)
    : IPersistenceSession, IEfCorePersistenceSession
{
    private readonly List<ISessionScopedMeasurement> heldMeasurements = [];

    private bool completed;

    private bool endedOnTransientFailure;

    /// <inheritdoc />
    public Task<MailFathomDbContext> JoinAsync(CancellationToken cancellationToken) =>
        resources.JoinAsync(cancellationToken);

    /// <inheritdoc />
    public void MeasureOnEnding(ISessionScopedMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        this.heldMeasurements.Add(measurement);
    }

    /// <inheritdoc />
    /// <exception cref="PersistenceTransientFailureException">
    /// Thrown when the save met a failure the database may not produce again. The whole unit of work is what a
    /// caller repeats, so the failure is raised rather than reported as a result: nothing this session could repeat
    /// would help, because the transaction the statement belonged to is already gone.
    /// </exception>
    /// <exception cref="PersistenceCommitOutcomeUnknownException">
    /// Thrown when the commit round trip itself lost the connection. It is a separate failure from the one above
    /// because the save is provably uncommitted and this is not: the server may have committed before the client
    /// stopped hearing from it, so a caller that staged a write which accumulates or inserts blind must not repeat it.
    /// </exception>
    public async Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(this.completed, this);

        try
        {
            await resources.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await this.RollbackAfterConflictAsync(cancellationToken);

            return PersistenceCommitResult.ConcurrencyConflict;
        }
        catch (DbUpdateException exception) when (resources.IsConcurrencyConflict(exception))
        {
            await this.RollbackAfterConflictAsync(cancellationToken);

            return PersistenceCommitResult.ConcurrencyConflict;
        }
        catch (Exception exception) when (this.IsTransientCommitFailure(exception))
        {
            this.EndOnTransientFailure();

            throw new PersistenceTransientFailureException(
                "A local write did not commit because the database failed in a way that can clear on its own.",
                exception);
        }

        try
        {
            await resources.CommitTransactionAsync(cancellationToken);
        }
        catch (Exception exception) when (this.IsTransientCommitFailure(exception))
        {
            this.EndOnUnknownOutcome();

            throw new PersistenceCommitOutcomeUnknownException(
                "A local write lost its connection while committing, so whether it became durable is unknown.",
                exception);
        }

        this.completed = true;
        telemetry.RecordCommitted();
        this.PublishHeldMeasurements(sessionCommitted: true);

        return PersistenceCommitResult.Committed;
    }

    /// <inheritdoc />
    public bool TryEndOnTransientFailure(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        if (this.completed || !this.IsTransientStagingFailure(failure))
        {
            return false;
        }

        this.EndOnTransientFailure();

        return true;
    }

    /// <inheritdoc />
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Transaction rollback and disposal must both be attempted while the first cleanup failure remains observable.")]
    public async ValueTask DisposeAsync()
    {
        Exception? firstCleanupException = null;
        try
        {
            if (!this.completed)
            {
                await resources.RollbackTransactionAsync(CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            firstCleanupException = exception;
        }

        try
        {
            await resources.DisposeAsync();
        }
        catch (Exception exception)
        {
            firstCleanupException ??= exception;
        }
        finally
        {
            resources.ClearTrackedState();
            this.completed = true;
            this.PublishHeldMeasurements(sessionCommitted: false);
        }

        // A session the database dropped cannot be spoken to about its own cleanup, and the failure that ended it is
        // already on its way to the caller. Reporting what the rollback then said would replace the reason the write
        // did not commit with the consequence of it, and the retry that resolves the first would never see it.
        if (firstCleanupException is not null && !this.endedOnTransientFailure)
        {
            ExceptionDispatchInfo.Capture(firstCleanupException).Throw();
        }
    }

    /// <summary>Rolls one session back after a race it lost, and counts the conflict as one that happened.</summary>
    private async Task RollbackAfterConflictAsync(CancellationToken cancellationToken)
    {
        await resources.RollbackTransactionAsync(cancellationToken);

        this.completed = true;
        telemetry.RecordConcurrencyConflict();
        this.PublishHeldMeasurements(sessionCommitted: false);
    }

    /// <summary>Ends one session whose commit round trip went unanswered, leaving its outcome unread.</summary>
    /// <remarks>
    /// The same silence as below, and the same refusal to ask the connection anything further. What differs is the
    /// count: a session ended here may have committed, so counting it as a failure a replay resolves would report a
    /// write that possibly happened as one that provably did not.
    /// </remarks>
    private void EndOnUnknownOutcome()
    {
        this.completed = true;
        this.endedOnTransientFailure = true;
        telemetry.RecordCommitOutcomeUnknown();
        this.PublishHeldMeasurements(sessionCommitted: false);
    }

    /// <summary>Ends one session the database refused, without asking the connection that carried it anything more.</summary>
    /// <remarks>
    /// No rollback is issued. Where the connection went away the server has already discarded the transaction, so the
    /// statement would meet nothing to roll back and would fail in its turn; where it did not, disposing the
    /// transaction rolls it back anyway. Either way the caller is owed the failure that ended the write rather than
    /// whatever the cleanup went on to say about it.
    /// </remarks>
    private void EndOnTransientFailure()
    {
        this.completed = true;
        this.endedOnTransientFailure = true;
        telemetry.RecordTransientFailure();
        this.PublishHeldMeasurements(sessionCommitted: false);
    }

    /// <summary>Reports whether a failure a write raised while staging came from the database and may clear on its own.</summary>
    /// <remarks>
    /// A staged write is not only database work. It reaches whatever it has to reach before it joins the session —
    /// an object store above all, which is the ordering the transaction opening lazily exists to allow — and a socket
    /// that failed there looks exactly like a socket that failed to PostgreSQL. So provenance is required rather than
    /// shape: the failure classified is the database exception inside the chain, and a failure carrying none is
    /// somebody else's dependency, left to end the write rather than replayed against a database that was never asked
    /// anything. The commit path below needs no such guard, because the call that raised it was this session's own.
    /// </remarks>
    private bool IsTransientStagingFailure(Exception failure) =>
        failure is not OperationCanceledException
        && DatabaseFailureWithin(failure) is { } databaseFailure
        && resources.IsTransientFailure(databaseFailure);

    /// <summary>Finds the database failure a raised exception carries, at any depth, or none.</summary>
    private static DbException? DatabaseFailureWithin(Exception failure)
    {
        for (var candidate = failure; candidate is not null; candidate = candidate.InnerException)
        {
            if (candidate is DbException databaseFailure)
            {
                return databaseFailure;
            }
        }

        return null;
    }

    /// <summary>Reports whether a commit met a failure the database may not produce again.</summary>
    /// <remarks>
    /// The cause is classified beside the failure itself, because EF Core wraps what the provider raised: a dropped
    /// connection reaches a save as a <see cref="DbUpdateException" /> whose inner exception is the only part that
    /// knows the SQLSTATE class the answer rests on. A caller that cancelled is refused before either is asked, so
    /// reading a cause cannot turn a cancellation into a replay.
    /// </remarks>
    private bool IsTransientCommitFailure(Exception failure) =>
        failure is not OperationCanceledException
        && (resources.IsTransientFailure(failure)
            || (failure.InnerException is { } cause && resources.IsTransientFailure(cause)));

    /// <summary>Publishes what was staged here under the ending this session actually reached, once.</summary>
    /// <remarks>
    /// Draining the held measurements is what makes the second call a no-op: disposal runs after a commit and after a
    /// conflict alike, and an ending already reported must not be reported again as an abandoned one.
    /// </remarks>
    private void PublishHeldMeasurements(bool sessionCommitted)
    {
        foreach (var measurement in this.heldMeasurements)
        {
            measurement.PublishAfterSession(sessionCommitted);
        }

        this.heldMeasurements.Clear();
    }
}
