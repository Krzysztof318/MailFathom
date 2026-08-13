// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using MailFathom.Application.Persistence;
using MailFathom.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Sessions;

/// <summary>Provides the EF Core and transaction operations owned by one persistence session.</summary>
/// <remarks>
/// EF Core publishes no interface over this seam. <see cref="Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction" />
/// covers committing and rolling back alone, while a session also has to save tracked changes, ask the provider
/// whether an update failure is an optimistic conflict, and clear tracked state after cleanup — and the type that
/// offers the rest, <see cref="DbContext" />, is a concrete class no fake provider may stand in for. Bundling those
/// operations here is what lets the commit, rollback, and disposal ordering of
/// <see cref="EfCorePersistenceSession" /> be asserted without a database.
/// </remarks>
internal interface IEfCorePersistenceSessionResources : IAsyncDisposable
{
    /// <summary>Gets the context used by repositories participating in the session.</summary>
    MailFathomDbContext DbContext { get; }

    /// <summary>Persists tracked changes through EF Core.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Commits the current database transaction.</summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken);

    /// <summary>Rolls back the current database transaction.</summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken);

    /// <summary>Determines whether a provider update failure represents a recognized optimistic conflict.</summary>
    bool IsConcurrencyConflict(DbUpdateException exception);

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

    /// <inheritdoc />
    public MailFathomDbContext DbContext => resources.DbContext;

    /// <inheritdoc />
    public void MeasureOnEnding(ISessionScopedMeasurement measurement)
    {
        ArgumentNullException.ThrowIfNull(measurement);

        this.heldMeasurements.Add(measurement);
    }

    /// <inheritdoc />
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

        await resources.CommitTransactionAsync(cancellationToken);

        this.completed = true;
        telemetry.RecordCommitted();
        this.PublishHeldMeasurements(sessionCommitted: true);

        return PersistenceCommitResult.Committed;
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

        if (firstCleanupException is not null)
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
