// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Persistence;
using MailMcp.CodeCoverage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Creates EF Core-backed persistence sessions for application write transactions.</summary>
[RequiresIntegrationCoverage]
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

        /// <summary>Recognizes the two inserts a competing writer can win, and nothing else.</summary>
        /// <remarks>
        /// Both name a constraint whose violation means "another run got here first" rather than "this data is
        /// wrong": the first checkpoint of a folder, and the first binding of an alias to a remote folder. Every
        /// other unique violation stays a failure, because treating an unnamed collision as a race would retry a
        /// write that will never succeed.
        /// </remarks>
        public bool IsConcurrencyConflict(DbUpdateException exception) =>
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: MailMcpDbContext.SynchronizationCheckpointPrimaryKeyConstraintName
                    or MailMcpDbContext.MailFolderBindingUniqueIndexName,
            };

        public void ClearTrackedState() => dbContext.ChangeTracker.Clear();

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
