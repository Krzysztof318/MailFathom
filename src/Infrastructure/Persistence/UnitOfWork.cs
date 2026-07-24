// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.Synchronization;
using Microsoft.EntityFrameworkCore.Storage;

namespace MailMcp.Infrastructure.Persistence;

/// <summary>Creates EF Core-backed persistence sessions for application write transactions.</summary>
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

        public async ValueTask DisposeAsync()
        {
            if (!this.completed)
            {
                await transaction.RollbackAsync();
            }

            await transaction.DisposeAsync();
        }
    }
}
