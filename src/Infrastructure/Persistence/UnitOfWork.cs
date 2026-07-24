// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using MailMcp.Application.Synchronization;
using Microsoft.EntityFrameworkCore.Storage;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Creates EF Core-backed persistence sessions for application write transactions.</summary>
// TODO: Remove this exclusion when the planned PostgreSQL integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by PostgreSQL integration tests.")]
public sealed class UnitOfWork(MailMcpDbContext dbContext) : ISessionFactory
{
    /// <inheritdoc />
    public async Task<ISession> BeginSessionAsync(CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        return new EfCoreSession(dbContext, transaction);
    }

    private sealed class EfCoreSession(MailMcpDbContext dbContext, IDbContextTransaction transaction) : ISession
    {
        private bool completed;

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(this.completed, this);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            this.completed = true;
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
