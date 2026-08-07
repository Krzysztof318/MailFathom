// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace MailFathom.Infrastructure.Persistence.Sessions;

/// <summary>Creates EF Core-backed persistence sessions for application write transactions.</summary>
[RequiresIntegrationCoverage]
internal sealed class PersistenceSessionFactory(MailFathomDbContext dbContext) : IPersistenceSessionFactory
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

        /// <summary>Recognizes the four inserts a competing writer can win, and nothing else.</summary>
        /// <remarks>
        /// <para>
        /// Each names a constraint whose violation means "another run got here first" rather than "this data is
        /// wrong": the first checkpoint of a folder, the first binding of an alias to a remote folder, and the account
        /// row that first binding creates on its way. The account is listed because it is part of that same insert:
        /// two runs binding an alias for the first time under an account nothing has stored yet collide on the account
        /// before they ever reach the alias, and reporting only one half of one race would leave the other half an
        /// unhandled failure. Every other unique violation stays a failure, because treating an unnamed collision as a
        /// race would retry a write that will never succeed.
        /// </para>
        /// <para>
        /// The fourth is the mutation identity, and it is the one where losing the race is the mechanism rather than an
        /// accident. Two callers asking for the same change reach the database together, one of them is refused here,
        /// and the retry reads back the record the winner wrote — which is exactly how the same request twice performs
        /// one mutation.
        /// </para>
        /// </remarks>
        public bool IsConcurrencyConflict(DbUpdateException exception) =>
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: MailFathomDbContext.SynchronizationCheckpointPrimaryKeyConstraintName
                    or MailFathomDbContext.MailFolderBindingUniqueIndexName
                    or MailFathomDbContext.MailboxAccountPrimaryKeyConstraintName
                    or MailFathomDbContext.MailboxMutationIdentityUniqueIndexName,
            };

        public void ClearTrackedState() => dbContext.ChangeTracker.Clear();

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
