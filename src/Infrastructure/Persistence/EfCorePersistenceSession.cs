// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using MailMcp.Application.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Provides the EF Core and transaction operations owned by one persistence session.</summary>
internal interface IEfCorePersistenceSessionResources : IAsyncDisposable
{
    /// <summary>Gets the context used by repositories participating in the session.</summary>
    MailMcpDbContext DbContext { get; }

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
internal sealed class EfCorePersistenceSession(IEfCorePersistenceSessionResources resources)
    : IPersistenceSession, IEfCorePersistenceSession
{
    private bool completed;

    /// <inheritdoc />
    public MailMcpDbContext DbContext => resources.DbContext;

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
            await resources.RollbackTransactionAsync(cancellationToken);
            this.completed = true;

            return PersistenceCommitResult.ConcurrencyConflict;
        }
        catch (DbUpdateException exception) when (resources.IsConcurrencyConflict(exception))
        {
            await resources.RollbackTransactionAsync(cancellationToken);
            this.completed = true;

            return PersistenceCommitResult.ConcurrencyConflict;
        }

        await resources.CommitTransactionAsync(cancellationToken);

        this.completed = true;

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
        }

        if (firstCleanupException is not null)
        {
            ExceptionDispatchInfo.Capture(firstCleanupException).Throw();
        }
    }
}
