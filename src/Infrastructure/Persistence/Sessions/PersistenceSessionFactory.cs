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

        /// <summary>Recognizes the inserts a competing writer can win, and nothing else.</summary>
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
        /// <para>
        /// The fifth is one audit entry per mutation ending, and it is listed for a reason the others do not have: what
        /// reads the answer is a trail that swallows a failed append and counts it. An append repeated after a commit
        /// whose answer was lost is the benign case that constraint exists for, and leaving it unrecognized would report
        /// it as an entry the trail could not keep — on the very counter that makes swallowing defensible — while the
        /// trail in fact holds exactly the one entry it should.
        /// </para>
        /// <para>
        /// The last two are the embedding profile's, and both are races between two activations. A collision on the
        /// identity fingerprint is the mutation identity's case again: the retry resolves to the profile the winner
        /// registered, which is what makes activating one declaration twice register one row. A collision on the
        /// lifecycle index is two activations of <em>different</em> geometries, where the retry cannot resolve it —
        /// what recognizing it buys is that the loser meets a first-party conflict rather than a provider exception
        /// crossing the application boundary, and the activation turns that into the answer the operator needs, which
        /// is that a different reindex is already running.
        /// </para>
        /// </remarks>
        public bool IsConcurrencyConflict(DbUpdateException exception) =>
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: MailFathomDbContext.SynchronizationCheckpointPrimaryKeyConstraintName
                    or MailFathomDbContext.MailFolderBindingUniqueIndexName
                    or MailFathomDbContext.MailboxAccountPrimaryKeyConstraintName
                    or MailFathomDbContext.MailboxMutationIdentityUniqueIndexName
                    or MailFathomDbContext.MailboxMutationAuditEntryMutationUniqueIndexName
                    or MailFathomDbContext.EmbeddingProfileFingerprintUniqueIndexName
                    or MailFathomDbContext.EmbeddingProfileLifecycleUniqueIndexName,
            };

        public void ClearTrackedState() => dbContext.ChangeTracker.Clear();

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
