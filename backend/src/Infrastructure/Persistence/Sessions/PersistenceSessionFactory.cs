// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Persistence;
using MailFathom.Application.Resilience;
using MailFathom.CodeCoverage;
using MailFathom.Infrastructure.ObjectStorage;
using MailFathom.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace MailFathom.Infrastructure.Persistence.Sessions;

/// <summary>Creates EF Core-backed persistence sessions for application write transactions.</summary>
[RequiresIntegrationCoverage]
internal sealed class PersistenceSessionFactory(
    MailFathomDbContext dbContext,
    ITransientFailureClassifier transientFailureClassifier,
    PersistenceCommitTelemetry telemetry,
    ReleasedContentObjectEraser releasedContentObjects) : IPersistenceSessionFactory
{
    /// <inheritdoc />
    /// <remarks>
    /// Creating a session opens no transaction. The transaction belongs to the writes that join the session, so it is
    /// opened by the first of them and never before: work a caller stages outside the database — an object handed to a
    /// content endpoint, a digest computed over it — then happens with no transaction held open across it, which is
    /// what
    /// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0001-application-owned-repositories-for-persistence-ports.md">ADR 0001</see>
    /// requires of anything that reaches a remote store.
    /// </remarks>
    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Ownership of the resource adapter, and of the transaction it later opens, is transferred to the returned persistence session.")]
    public Task<IPersistenceSession> BeginSessionAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IPersistenceSession>(
            new EfCorePersistenceSession(
                new EfCorePersistenceSessionResources(dbContext, transientFailureClassifier),
                telemetry,
                releasedContentObjects));

    private sealed class EfCorePersistenceSessionResources(
        MailFathomDbContext dbContext,
        ITransientFailureClassifier transientFailureClassifier)
        : IEfCorePersistenceSessionResources
    {
        private IDbContextTransaction? transaction;

        public async Task<MailFathomDbContext> JoinAsync(CancellationToken cancellationToken)
        {
            this.transaction ??= await dbContext.Database.BeginTransactionAsync(cancellationToken);

            return dbContext;
        }

        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        public Task CommitTransactionAsync(CancellationToken cancellationToken) =>
            this.transaction?.CommitAsync(cancellationToken) ?? Task.CompletedTask;

        public Task RollbackTransactionAsync(CancellationToken cancellationToken) =>
            this.transaction?.RollbackAsync(cancellationToken) ?? Task.CompletedTask;

        /// <inheritdoc />
        public bool IsConcurrencyConflict(DbUpdateException exception) =>
            PersistenceConcurrencyConflicts.IsConcurrencyConflict(exception);

        /// <inheritdoc />
        public bool IsTransientFailure(Exception exception) =>
            transientFailureClassifier.IsTransientFailure(OutboundDependency.DatabaseCommandExecution, exception);

        public void ClearTrackedState() => dbContext.ChangeTracker.Clear();

        public ValueTask DisposeAsync() => this.transaction?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
