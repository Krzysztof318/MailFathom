// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MailFathom.Infrastructure.Persistence.Sessions;

/// <summary>Creates EF Core-backed persistence sessions for application write transactions.</summary>
[RequiresIntegrationCoverage]
internal sealed class PersistenceSessionFactory(
    MailFathomDbContext dbContext,
    PersistenceCommitTelemetry telemetry) : IPersistenceSessionFactory
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
            new EfCorePersistenceSessionResources(dbContext, transaction),
            telemetry);
    }

    private sealed class EfCorePersistenceSessionResources(
        MailFathomDbContext dbContext,
        IDbContextTransaction transaction)
        : IEfCorePersistenceSessionResources
    {
        public MailFathomDbContext DbContext => dbContext;

        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken) =>
            transaction.CommitAsync(cancellationToken);

        public Task RollbackTransactionAsync(CancellationToken cancellationToken) =>
            transaction.RollbackAsync(cancellationToken);

        /// <inheritdoc />
        public bool IsConcurrencyConflict(DbUpdateException exception) =>
            PersistenceConcurrencyConflicts.IsConcurrencyConflict(exception);

        public void ClearTrackedState() => dbContext.ChangeTracker.Clear();

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
