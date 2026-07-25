// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using MailMcp.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Creates EF Core-backed persistence sessions for application write transactions.</summary>
// TODO: Remove this exclusion when the planned PostgreSQL integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by PostgreSQL integration tests.")]
public sealed class PersistenceSessionFactory(MailMcpDbContext dbContext) : IPersistenceSessionFactory
{
    /// <inheritdoc />
    public async Task<IPersistenceSession> BeginSessionAsync(CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        return new EfCorePersistenceSession(dbContext, transaction);
    }

    private sealed class EfCorePersistenceSession(MailMcpDbContext dbContext, IDbContextTransaction transaction)
        : IPersistenceSession, IEfCorePersistenceSession
    {
        private bool completed;

        public MailMcpDbContext DbContext => dbContext;

        public async Task<PersistenceCommitResult> CommitAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(this.completed, this);

            try
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(cancellationToken);
                this.completed = true;

                return PersistenceCommitResult.ConcurrencyConflict;
            }

            await transaction.CommitAsync(cancellationToken);

            this.completed = true;

            return PersistenceCommitResult.Committed;
        }

        [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Transaction rollback and disposal must both be attempted while the first cleanup failure remains observable.")]
        public async ValueTask DisposeAsync()
        {
            Exception? firstCleanupException = null;
            try
            {
                if (!this.completed)
                {
                    await transaction.RollbackAsync();
                }
            }
            catch (Exception exception)
            {
                firstCleanupException = exception;
            }

            try
            {
                await transaction.DisposeAsync();
            }
            catch (Exception exception)
            {
                firstCleanupException ??= exception;
            }
            finally
            {
                dbContext.ChangeTracker.Clear();
                this.completed = true;
            }

            if (firstCleanupException is not null)
            {
                ExceptionDispatchInfo.Capture(firstCleanupException).Throw();
            }
        }
    }
}
