// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Creates EF Core-backed persistence sessions for application write transactions.</summary>
// TODO: Remove this exclusion when the planned PostgreSQL integration tests are enabled.
[ExcludeFromCodeCoverage(Justification = "Will be covered later by PostgreSQL integration tests.")]
internal sealed class PersistenceSessionFactory(MailMcpDbContext dbContext) : IPersistenceSessionFactory
{
    /// <inheritdoc />
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the resource adapter and its transaction is transferred to the returned persistence session.")]
    public async Task<IPersistenceSession> BeginSessionAsync(CancellationToken cancellationToken)
    {
        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        return new EfCorePersistenceSession(
            new EfCorePersistenceSessionResources(dbContext, transaction));
    }

    private sealed class EfCorePersistenceSessionResources(
        MailMcpDbContext dbContext,
        IDbContextTransaction transaction)
        : IEfCorePersistenceSessionResources
    {
        public MailMcpDbContext DbContext => dbContext;

        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken) =>
            transaction.CommitAsync(cancellationToken);

        public Task RollbackTransactionAsync(CancellationToken cancellationToken) =>
            transaction.RollbackAsync(cancellationToken);

        public bool IsConcurrencyConflict(DbUpdateException exception) =>
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: MailMcpDbContext.SynchronizationCheckpointPrimaryKeyConstraintName,
            };

        public void ClearTrackedState() => dbContext.ChangeTracker.Clear();

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
